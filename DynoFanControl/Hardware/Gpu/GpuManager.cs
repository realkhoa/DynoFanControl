using System.Management;
using LibreHardwareMonitor.Hardware;

namespace DynoFanControl.Hardware.Gpu;

/// <summary>
/// Universal multi-GPU coordinator supporting NVIDIA (NVML/NVAPI), AMD (ADL), and Intel (Arc/Xe/UHD).
/// Handles multi-GPU setups (iGPU + dGPU), dynamic GPU selection, and telemetry extraction.
/// </summary>
public sealed class GpuManager : IDisposable
{
    private static readonly Lazy<GpuManager> _instance = new(() => new GpuManager());
    public static GpuManager Instance => _instance.Value;

    private readonly List<GpuDeviceInfo> _detectedGpus = new();
    private readonly object _lock = new();
    private string? _preferredGpuId;
    private bool _initialized;

    public IReadOnlyList<GpuDeviceInfo> DetectedGpus
    {
        get
        {
            lock (_lock)
            {
                return _detectedGpus.ToList();
            }
        }
    }

    public GpuDeviceInfo? ActiveGpu
    {
        get
        {
            lock (_lock)
            {
                if (_detectedGpus.Count == 0) return null;
                if (!string.IsNullOrEmpty(_preferredGpuId))
                {
                    var match = _detectedGpus.FirstOrDefault(g => g.Id == _preferredGpuId);
                    if (match != null) return match;
                }

                // Default: First discrete GPU, or first GPU available
                return _detectedGpus.FirstOrDefault(g => g.IsDiscrete) ?? _detectedGpus.First();
            }
        }
    }

    public void SetPreferredGpu(string? gpuId)
    {
        lock (_lock)
        {
            _preferredGpuId = gpuId;
        }
    }

    public void Initialize()
    {
        lock (_lock)
        {
            if (_initialized) return;
            _initialized = true;

            // 1. Initialize NVIDIA NVML
            try { NvmlDriver.Initialize(); } catch { }

            // 2. Initialize AMD ADL
            try { AmdGpuDriver.Initialize(); } catch { }

            // 3. Scan system display devices via WMI
            ScanWmiAdapters();
        }
    }

    private void ScanWmiAdapters()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM, DriverVersion, VideoProcessor, PNPDeviceID FROM Win32_VideoController");
            int idx = 0;
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Remote", StringComparison.OrdinalIgnoreCase))
                {
                    continue; // Skip Microsoft Basic Display or Remote desktop drivers
                }

                var vendor = DetermineVendor(name);
                var isDiscrete = DetermineIsDiscrete(name);

                var gpu = new GpuDeviceInfo
                {
                    Id = $"GPU-{vendor}-{idx}",
                    Name = name,
                    Vendor = vendor,
                    IsDiscrete = isDiscrete,
                    DriverVersion = obj["DriverVersion"]?.ToString() ?? ""
                };

                if (ulong.TryParse(obj["AdapterRAM"]?.ToString(), out var ramBytes) && ramBytes > 0)
                {
                    var gb = ramBytes / (1024.0 * 1024 * 1024);
                    gpu.VramTotalText = $"{gb:F0} GB";
                }

                _detectedGpus.Add(gpu);
                idx++;
            }
        }
        catch { }
    }

    public void UpdateTelemetry(IComputer? lhmComputer)
    {
        lock (_lock)
        {
            // 1. Query NVIDIA GPUs via native NVML
            try
            {
                var nv = NvmlDriver.QueryGpu();
                if (nv.IsAvailable)
                {
                    var nvGpu = _detectedGpus.FirstOrDefault(g => g.Vendor == GpuVendor.Nvidia) ??
                                _detectedGpus.FirstOrDefault(g => g.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));

                    if (nvGpu == null)
                    {
                        nvGpu = new GpuDeviceInfo
                        {
                            Id = "GPU-NV-0",
                            Name = nv.Name,
                            Vendor = GpuVendor.Nvidia,
                            IsDiscrete = true
                        };
                        _detectedGpus.Insert(0, nvGpu);
                    }

                    nvGpu.Name = nv.Name;
                    nvGpu.Temperature = nv.IsSleeping ? 0 : nv.Temperature;
                    nvGpu.UtilizationGpu = nv.UtilizationGpu;
                    nvGpu.PowerWatts = nv.PowerWatts;
                    nvGpu.IsSleeping = nv.IsSleeping;
                }
            }
            catch { }

            // 2. Query AMD GPUs via native ADL
            try
            {
                if (AmdGpuDriver.IsAvailable)
                {
                    var amdGpu = _detectedGpus.FirstOrDefault(g => g.Vendor == GpuVendor.Amd);
                    if (amdGpu != null)
                    {
                        var temp = AmdGpuDriver.GetTemperature();
                        if (temp > 0) amdGpu.Temperature = temp;
                        var fan = AmdGpuDriver.GetFanRpm();
                        if (fan > 0) amdGpu.FanRpm = fan;
                    }
                }
            }
            catch { }

            // 3. Query LibreHardwareMonitor for Intel, AMD, and fallback NVIDIA
            if (lhmComputer != null)
            {
                try
                {
                    foreach (var hw in lhmComputer.Hardware)
                    {
                        if (hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
                        {
                            hw.Update();
                            var vendor = hw.HardwareType switch
                            {
                                HardwareType.GpuNvidia => GpuVendor.Nvidia,
                                HardwareType.GpuAmd => GpuVendor.Amd,
                                HardwareType.GpuIntel => GpuVendor.Intel,
                                _ => GpuVendor.Unknown
                            };

                            var target = _detectedGpus.FirstOrDefault(g => g.Vendor == vendor && g.Name.Contains(hw.Name, StringComparison.OrdinalIgnoreCase)) ??
                                         _detectedGpus.FirstOrDefault(g => g.Vendor == vendor);

                            if (target == null)
                            {
                                target = new GpuDeviceInfo
                                {
                                    Id = $"GPU-{vendor}-{_detectedGpus.Count}",
                                    Name = hw.Name,
                                    Vendor = vendor,
                                    IsDiscrete = DetermineIsDiscrete(hw.Name)
                                };
                                _detectedGpus.Add(target);
                            }

                            foreach (var s in hw.Sensors)
                            {
                                if (!s.Value.HasValue) continue;
                                var val = s.Value.Value;

                                if (s.SensorType == SensorType.Temperature)
                                {
                                    if (target.Temperature == 0 || target.Vendor != GpuVendor.Nvidia)
                                    {
                                        if (val is > 0 and < 130)
                                            target.Temperature = Math.Round(val, 1);
                                    }
                                }
                                else if (s.SensorType == SensorType.Load && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                                {
                                    target.UtilizationGpu = (uint)Math.Clamp(Math.Round(val), 0, 100);
                                }
                                else if (s.SensorType == SensorType.Power && (s.Name.Contains("GPU", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase)))
                                {
                                    target.PowerWatts = Math.Round(val, 1);
                                }
                                else if (s.SensorType == SensorType.Fan)
                                {
                                    target.FanRpm = (uint)Math.Max(0, Math.Round(val));
                                }
                                else if (s.SensorType == SensorType.Control)
                                {
                                    target.HasFanControl = true;
                                    target.FanSpeedPercent = (int)Math.Clamp(Math.Round(val), 0, 100);
                                }
                            }
                        }
                    }
                }
                catch { }
            }
        }
    }

    public bool SetGpuFanSpeed(int percent, IComputer? lhmComputer)
    {
        // 1. Try AMD ADL
        if (ActiveGpu?.Vendor == GpuVendor.Amd && AmdGpuDriver.IsAvailable)
        {
            if (AmdGpuDriver.SetFanSpeed(percent)) return true;
        }

        // 2. Try LibreHardwareMonitor GPU Controls
        if (lhmComputer != null)
        {
            try
            {
                foreach (var hw in lhmComputer.Hardware)
                {
                    if (hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
                    {
                        foreach (var s in hw.Sensors)
                        {
                            if (s.SensorType == SensorType.Control && s.Control != null)
                            {
                                s.Control.SetSoftware(percent);
                                return true;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        return false;
    }

    private static GpuVendor DetermineVendor(string name)
    {
        if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) || name.Contains("Quadro", StringComparison.OrdinalIgnoreCase) || name.Contains("RTX", StringComparison.OrdinalIgnoreCase) || name.Contains("GTX", StringComparison.OrdinalIgnoreCase))
            return GpuVendor.Nvidia;

        if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) || name.Contains("ATI", StringComparison.OrdinalIgnoreCase))
            return GpuVendor.Amd;

        if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase) || name.Contains("Arc", StringComparison.OrdinalIgnoreCase) || name.Contains("Iris", StringComparison.OrdinalIgnoreCase) || name.Contains("UHD", StringComparison.OrdinalIgnoreCase) || name.Contains("HD Graphics", StringComparison.OrdinalIgnoreCase))
            return GpuVendor.Intel;

        return GpuVendor.Unknown;
    }

    private static bool DetermineIsDiscrete(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.Contains("geforce") || lower.Contains("gtx") || lower.Contains("rtx") || lower.Contains("quadro") || lower.Contains("tesla"))
            return true;
        if (lower.Contains("rx ") || lower.Contains("radeon pro") || lower.Contains("rx 5") || lower.Contains("rx 6") || lower.Contains("rx 7") || lower.Contains("rx 8") || lower.Contains("vega 56") || lower.Contains("vega 64"))
            return true;
        if (lower.Contains("arc a") || lower.Contains("arc b") || lower.Contains("arc pro"))
            return true;

        if (lower.Contains("graphics") || lower.Contains("uhd") || lower.Contains("iris") || lower.Contains("vega 8") || lower.Contains("vega 7") || lower.Contains("680m") || lower.Contains("780m") || lower.Contains("890m"))
            return false;

        return true;
    }

    public void Dispose()
    {
        AmdGpuDriver.Shutdown();
    }
}
