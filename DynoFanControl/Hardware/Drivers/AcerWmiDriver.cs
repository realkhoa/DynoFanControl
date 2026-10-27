using System.Management;

namespace DynoFanControl.Hardware.Drivers;

/// <summary>
/// Acer OEM WMI driver using root\wmi: AcerGamingFunction.
/// Provides native BIOS-level fan control on Acer Nitro and Predator laptops without touching raw I/O ports.
/// </summary>
public sealed class AcerWmiDriver : IFanControlDriver
{
    private ManagementScope? _scope;
    private bool _initialized;
    private bool _available;

    public string DriverName => "Acer Gaming WMI Driver";
    public string PlatformDescription => "Acer Nitro & Predator Gaming Function WMI Interface";
    public HardwarePlatformType PlatformType => HardwarePlatformType.LaptopAcer;
    public int PriorityScore => 85;

    public bool IsSupported
    {
        get
        {
            var p = PlatformDetector.GetProfile();
            return p.PlatformType == HardwarePlatformType.LaptopAcer ||
                   p.Manufacturer.Contains("Acer", StringComparison.OrdinalIgnoreCase);
        }
    }

    public bool Initialize()
    {
        if (_initialized) return _available;
        _initialized = true;

        try
        {
            _scope = new ManagementScope(@"\\.\root\wmi");
            _scope.Connect();

            using var searcher = new ManagementObjectSearcher(_scope, new ObjectQuery("SELECT * FROM AcerGamingFunction"));
            foreach (var o in searcher.Get())
            {
                _available = true;
                break;
            }
        }
        catch
        {
            _available = false;
        }

        return _available;
    }

    public bool SetCpuFan(bool isAuto, int speedPercent)
    {
        return SetFanInternal(0, isAuto, speedPercent);
    }

    public bool SetGpuFan(bool isAuto, int speedPercent)
    {
        return SetFanInternal(1, isAuto, speedPercent);
    }

    private bool SetFanInternal(uint fanIndex, bool isAuto, int speedPercent)
    {
        if (!_available || _scope == null) return false;

        try
        {
            using var searcher = new ManagementObjectSearcher(_scope, new ObjectQuery("SELECT * FROM AcerGamingFunction"));
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    if (isAuto)
                    {
                        var inParams = obj.GetMethodParameters("SetGamingFanAutoControl");
                        inParams["gmInput"] = (fanIndex << 16) | 1u; // 1 = Auto
                        obj.InvokeMethod("SetGamingFanAutoControl", inParams, null);
                        return true;
                    }
                    else
                    {
                        // 1. Disable Auto
                        var inParamsAuto = obj.GetMethodParameters("SetGamingFanAutoControl");
                        inParamsAuto["gmInput"] = (fanIndex << 16) | 0u; // 0 = Manual
                        obj.InvokeMethod("SetGamingFanAutoControl", inParamsAuto, null);

                        // 2. Set Speed
                        var clamped = (uint)Math.Clamp(speedPercent, 1, 100);
                        var inParamsSpeed = obj.GetMethodParameters("SetGamingFanSpeed");
                        inParamsSpeed["gmInput"] = (fanIndex << 16) | clamped;
                        obj.InvokeMethod("SetGamingFanSpeed", inParamsSpeed, null);
                        return true;
                    }
                }
            }
        }
        catch { }

        return false;
    }

    public bool SetCoolBoost(bool enabled)
    {
        // AcerGamingFunction can toggle CoolBoost or custom Turbo mode
        return false;
    }

    public FanTachometerReading ReadTachometers()
    {
        var reading = new FanTachometerReading();
        if (!_available || _scope == null) return reading;

        try
        {
            using var searcher = new ManagementObjectSearcher(_scope, new ObjectQuery("SELECT * FROM AcerGamingFunction"));
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    // CPU Fan (Index 0)
                    var inParams0 = obj.GetMethodParameters("GetGamingFanSpeed");
                    inParams0["gmInput"] = 0u;
                    using var out0 = (ManagementBaseObject)obj.InvokeMethod("GetGamingFanSpeed", inParams0, null);
                    var raw0 = (ulong)out0["gmOutput"];
                    reading.CpuFanRpm = (uint)(raw0 >> 32);
                    reading.CpuIsAuto = (raw0 & 1) == 1;

                    // GPU Fan (Index 1)
                    var inParams1 = obj.GetMethodParameters("GetGamingFanSpeed");
                    inParams1["gmInput"] = 1u;
                    using var out1 = (ManagementBaseObject)obj.InvokeMethod("GetGamingFanSpeed", inParams1, null);
                    var raw1 = (ulong)out1["gmOutput"];
                    reading.GpuFanRpm = (uint)(raw1 >> 32);
                    reading.GpuIsAuto = (raw1 & 1) == 1;
                }
                break;
            }
        }
        catch { }

        return reading;
    }

    public void EmergencyReset()
    {
        SetCpuFan(true, 0);
        SetGpuFan(true, 0);
    }

    public void Dispose()
    {
        _scope = null;
        _available = false;
    }
}
