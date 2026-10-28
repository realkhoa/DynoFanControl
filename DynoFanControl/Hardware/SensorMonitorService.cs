using System.Management;
using DynoFanControl.Hardware.Gpu;
using LibreHardwareMonitor.Hardware;

namespace DynoFanControl.Hardware;

public class SensorItem
{
    public string Category { get; set; } = ""; // "CPU", "Discrete GPU", "Integrated GPU", "Motherboard", "Fan Tachometer", "Storage", "Battery"
    public string Name { get; set; } = "";
    public double Value { get; set; }
    public string Unit { get; set; } = "°C";
    public string FormattedValue => Unit == " RPM" ? $"{Value:N0} RPM" : $"{Value:F1}{Unit}";
    public string Subtext { get; set; } = "";
}

public class MultiSensorSnapshot
{
    public double CpuTemp { get; set; }
    public double GpuTemp { get; set; }
    public string GpuName { get; set; } = "Graphics Processor";
    public uint GpuUtilization { get; set; }
    public double GpuPowerWatts { get; set; }
    public bool GpuIsSleeping { get; set; }
    public uint GpuCoreClockMhz { get; set; }
    public uint GpuMemoryClockMhz { get; set; }
    public double GpuVramUsedMb { get; set; }
    public double GpuVramTotalMb { get; set; }
    public double CpuLoad { get; set; }
    public double CpuPowerWatts { get; set; }
    public double RamUsedGb { get; set; }
    public double RamTotalGb { get; set; }
    public List<GpuDeviceInfo> DetectedGpus { get; set; } = new();
    public List<SensorItem> AllSensors { get; } = new();
}

public sealed class SensorMonitorService : IDisposable
{
    private Computer? _computer;
    private bool _computerInitialized;
    private readonly object _lock = new();

    public static SensorMonitorService Instance { get; } = new();

    public Computer? ComputerInstance => _computer;

    public void Initialize()
    {
        lock (_lock)
        {
            if (_computerInitialized) return;

            // 1. Initialize Universal GPU Subsystem (NVIDIA NVML, AMD ADL, Intel)
            try
            {
                GpuManager.Instance.Initialize();
            }
            catch { }

            // 2. Initialize LibreHardwareMonitor for CPU, GPUs, Motherboard Super I/O, USB fan controllers
            try
            {
                _computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsMemoryEnabled = true,
                    IsMotherboardEnabled = true,
                    IsStorageEnabled = true,
                    IsBatteryEnabled = true,
                    IsControllerEnabled = true // Corsair Commander, NZXT Kraken, AquaComputer
                };
                _computer.Open();
                _computerInitialized = true;
            }
            catch
            {
                _computer = null;
                _computerInitialized = false;
            }
        }
    }

    public MultiSensorSnapshot ReadAllSensors()
    {
        var snapshot = new MultiSensorSnapshot();

        // 1. Query Universal GPU Manager (NVIDIA NVML + AMD ADL + LHM Intel/AMD)
        try
        {
            GpuManager.Instance.UpdateTelemetry(_computer);
            var gpus = GpuManager.Instance.DetectedGpus;
            snapshot.DetectedGpus = gpus.ToList();

            var activeGpu = GpuManager.Instance.ActiveGpu;
            if (activeGpu != null)
            {
                snapshot.GpuName = activeGpu.Name;
                snapshot.GpuUtilization = activeGpu.UtilizationGpu;
                snapshot.GpuPowerWatts = activeGpu.PowerWatts;
                snapshot.GpuIsSleeping = activeGpu.IsSleeping;
                snapshot.GpuCoreClockMhz = activeGpu.CoreClockMhz;
                snapshot.GpuMemoryClockMhz = activeGpu.MemoryClockMhz;
                snapshot.GpuVramUsedMb = activeGpu.VramUsedMb;
                snapshot.GpuVramTotalMb = activeGpu.VramTotalMb;
                if (!activeGpu.IsSleeping && activeGpu.Temperature > 0)
                {
                    snapshot.GpuTemp = activeGpu.Temperature;
                }
            }

            // Register all detected GPUs in the sensor list
            foreach (var gpu in gpus)
            {
                var cat = gpu.IsDiscrete ? "Discrete GPU" : "Integrated GPU";
                var status = gpu.IsSleeping ? "Power Savings (Sleep)" : $"{gpu.UtilizationGpu}% Load • {gpu.PowerWatts:F1}W";
                snapshot.AllSensors.Add(new SensorItem
                {
                    Category = cat,
                    Name = $"{gpu.VendorBadge}: {gpu.Name}",
                    Value = gpu.IsSleeping ? 0 : gpu.Temperature,
                    Unit = "°C",
                    Subtext = $"{gpu.TypeBadge} • {status} {(!string.IsNullOrEmpty(gpu.VramTotalText) ? "• " + gpu.VramTotalText : "")}"
                });
            }
        }
        catch { }

        // 2. Query LibreHardwareMonitor (CPU, Motherboard Super I/O, Storage, Fans)
        lock (_lock)
        {
            if (_computer != null)
            {
                try
                {
                    foreach (var hw in _computer.Hardware)
                    {
                        hw.Update();

                        // Process sub-hardware (e.g. CPU cores, Motherboard Super I/O)
                        foreach (var sub in hw.SubHardware)
                        {
                            sub.Update();
                            ExtractHardwareSensors(sub, snapshot);
                        }

                        ExtractHardwareSensors(hw, snapshot);
                    }
                }
                catch { }
            }
        }

        // 3. Fallback: Query ACPI Thermal Zones via WMI
        try
        {
            var scope = new ManagementScope(@"\\.\root\wmi");
            scope.Connect();

            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSAcpi_ThermalZoneTemperature"));
            int tzIndex = 1;
            foreach (var obj in searcher.Get())
            {
                var raw = obj["CurrentTemperature"];
                var name = obj["InstanceName"]?.ToString() ?? $"TZ0{tzIndex}";
                if (raw != null)
                {
                    var deciKelvin = Convert.ToUInt32(raw);
                    var celsius = Math.Round((deciKelvin / 10.0) - 273.15, 1);
                    if (celsius is > 0 and < 125)
                    {
                        var isCpuZone = name.Contains("TZ01", StringComparison.OrdinalIgnoreCase);
                        var isGpuZone = name.Contains("TZ02", StringComparison.OrdinalIgnoreCase);

                        if (isCpuZone && snapshot.CpuTemp == 0)
                            snapshot.CpuTemp = celsius;
                        if (isGpuZone && snapshot.GpuTemp == 0)
                            snapshot.GpuTemp = celsius;

                        var label = isCpuZone ? "ACPI CPU Zone (TZ01)" : (isGpuZone ? "ACPI GPU Zone (TZ02)" : $"ACPI Zone {tzIndex} ({name})");
                        snapshot.AllSensors.Add(new SensorItem
                        {
                            Category = "Thermal Zone",
                            Name = label,
                            Value = celsius,
                            Unit = "°C",
                            Subtext = name
                        });
                    }
                }
                tzIndex++;
            }
        }
        catch { }

        // 4. Ensure non-zero primary temperatures if available
        if (snapshot.CpuTemp == 0)
        {
            var cpuSensor = snapshot.AllSensors.FirstOrDefault(s => s.Category == "CPU" && s.Value > 0);
            if (cpuSensor != null) snapshot.CpuTemp = cpuSensor.Value;
        }

        if (snapshot.GpuTemp == 0)
        {
            var gpuSensor = snapshot.AllSensors.FirstOrDefault(s => (s.Category.Contains("GPU") || s.Name.Contains("GPU")) && s.Value > 0);
            if (gpuSensor != null) snapshot.GpuTemp = gpuSensor.Value;
        }

        return snapshot;
    }

    private void ExtractHardwareSensors(IHardware hw, MultiSensorSnapshot snapshot)
    {
        var category = hw.HardwareType switch
        {
            HardwareType.Cpu => "CPU",
            HardwareType.GpuNvidia => "Discrete GPU",
            HardwareType.GpuAmd => "AMD Radeon GPU",
            HardwareType.GpuIntel => "Intel GPU",
            HardwareType.Storage => "Storage",
            HardwareType.Motherboard => "Motherboard",
            HardwareType.SuperIO => "Super I/O",
            HardwareType.Cooler => "Liquid Cooler/Hub",
            HardwareType.Battery => "Battery",
            _ => hw.HardwareType.ToString()
        };

        foreach (var sensor in hw.Sensors)
        {
            if (!sensor.Value.HasValue) continue;
            var val = Math.Round((double)sensor.Value.Value, 1);

            // Temperatures
            if (sensor.SensorType == SensorType.Temperature)
            {
                if (val is > 0 and < 130)
                {
                    // Primary CPU Temperature update
                    if (hw.HardwareType == HardwareType.Cpu)
                    {
                        if (sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                            sensor.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
                            sensor.Name.Contains("Core (Tctl/Tdie)", StringComparison.OrdinalIgnoreCase))
                        {
                            snapshot.CpuTemp = val;
                        }
                        else if (snapshot.CpuTemp == 0)
                        {
                            snapshot.CpuTemp = val;
                        }
                    }

                    // Primary GPU Temp update if not set
                    if ((hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel) && snapshot.GpuTemp == 0)
                    {
                        snapshot.GpuTemp = val;
                    }

                    snapshot.AllSensors.Add(new SensorItem
                    {
                        Category = category,
                        Name = sensor.Name,
                        Value = val,
                        Unit = "°C",
                        Subtext = hw.Name
                    });
                }
            }
            // Fan Tachometers
            else if (sensor.SensorType == SensorType.Fan && val > 0)
            {
                snapshot.AllSensors.Add(new SensorItem
                {
                    Category = "Fan Tachometer",
                    Name = sensor.Name,
                    Value = val,
                    Unit = " RPM",
                    Subtext = hw.Name
                });
            }
            // Fan Controls
            else if (sensor.SensorType == SensorType.Control)
            {
                snapshot.AllSensors.Add(new SensorItem
                {
                    Category = "Fan Control",
                    Name = sensor.Name,
                    Value = val,
                    Unit = "%",
                    Subtext = hw.Name
                });
            }
            // CPU Total Load
            else if (hw.HardwareType == HardwareType.Cpu && sensor.SensorType == SensorType.Load && sensor.Name.Contains("Total", StringComparison.OrdinalIgnoreCase))
            {
                snapshot.CpuLoad = val;
            }
            // CPU Package Power
            else if (hw.HardwareType == HardwareType.Cpu && sensor.SensorType == SensorType.Power && (sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) || sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)))
            {
                snapshot.CpuPowerWatts = val;
            }
            // RAM Usage
            else if (hw.HardwareType == HardwareType.Memory)
            {
                if (sensor.SensorType == SensorType.Data && sensor.Name.Contains("Used", StringComparison.OrdinalIgnoreCase))
                {
                    snapshot.RamUsedGb = val;
                }
                else if (sensor.SensorType == SensorType.Data && sensor.Name.Contains("Available", StringComparison.OrdinalIgnoreCase))
                {
                    double used = snapshot.RamUsedGb > 0 ? snapshot.RamUsedGb : 0;
                    snapshot.RamTotalGb = Math.Round(used + val, 1);
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_computer != null)
            {
                try { _computer.Close(); } catch { }
                _computer = null;
                _computerInitialized = false;
            }
            GpuManager.Instance.Dispose();
            NvmlDriver.Shutdown();
        }
    }
}
