using DynoFanControl.Hardware.Gpu;
using LibreHardwareMonitor.Hardware;

namespace DynoFanControl.Hardware.Drivers;

/// <summary>
/// Direct GPU Hardware Fan Controller for Desktop & Supported Laptop Graphics Cards.
/// Commands fans directly on NVIDIA (NVAPI/NVML) and AMD Radeon (ADL) discrete cards.
/// </summary>
public sealed class DesktopGpuFanDriver : IFanControlDriver
{
    private IComputer? _lhmComputer;
    private bool _initialized;
    private bool _available;

    public string DriverName => "Desktop Discrete GPU Fan Driver (NVAPI / AMD ADL)";
    public string PlatformDescription => "Direct GPU Heatsink Fan Speed Regulation for NVIDIA & AMD";
    public HardwarePlatformType PlatformType => HardwarePlatformType.DesktopPc;
    public int PriorityScore => 75;

    public bool IsSupported => GpuManager.Instance.ActiveGpu?.IsDiscrete == true;

    public void AttachComputer(IComputer computer)
    {
        _lhmComputer = computer;
        _available = true;
    }

    public bool Initialize()
    {
        if (_initialized) return _available;
        _initialized = true;
        _available = GpuManager.Instance.DetectedGpus.Any(g => g.IsDiscrete);
        return _available;
    }

    public bool SetCpuFan(bool isAuto, int speedPercent)
    {
        // This driver specializes in GPU fans
        return false;
    }

    public bool SetGpuFan(bool isAuto, int speedPercent)
    {
        if (isAuto)
        {
            return RestoreDefaultGpuControl();
        }
        else
        {
            return GpuManager.Instance.SetGpuFanSpeed(speedPercent, _lhmComputer);
        }
    }

    private bool RestoreDefaultGpuControl()
    {
        if (_lhmComputer != null)
        {
            try
            {
                foreach (var hw in _lhmComputer.Hardware)
                {
                    if (hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
                    {
                        foreach (var s in hw.Sensors)
                        {
                            if (s.SensorType == SensorType.Control && s.Control != null)
                            {
                                s.Control.SetDefault();
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

    public bool SetCoolBoost(bool enabled)
    {
        if (enabled) return SetGpuFan(false, 100);
        return false;
    }

    public FanTachometerReading ReadTachometers()
    {
        var reading = new FanTachometerReading();
        var activeGpu = GpuManager.Instance.ActiveGpu;
        if (activeGpu != null && activeGpu.FanRpm > 0)
        {
            reading.GpuFanRpm = activeGpu.FanRpm;
        }
        return reading;
    }

    public void EmergencyReset()
    {
        RestoreDefaultGpuControl();
    }

    public void Dispose()
    {
        EmergencyReset();
    }
}
