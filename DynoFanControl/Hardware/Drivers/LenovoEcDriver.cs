using System.Management;

namespace DynoFanControl.Hardware.Drivers;

/// <summary>
/// Lenovo Legion, IdeaPad Gaming, LOQ, and ThinkPad fan driver.
/// Operates through Lenovo GameZone WMI and Embedded Controller thermal tables.
/// </summary>
public sealed class LenovoEcDriver : IFanControlDriver
{
    private ManagementScope? _scope;
    private bool _initialized;
    private bool _available;

    public string DriverName => "Lenovo Legion/IdeaPad WMI Driver";
    public string PlatformDescription => "Lenovo Legion, LOQ & IdeaPad Gaming Thermal Architecture";
    public HardwarePlatformType PlatformType => HardwarePlatformType.LaptopLenovo;
    public int PriorityScore => 90;

    public bool IsSupported
    {
        get
        {
            var p = PlatformDetector.GetProfile();
            return p.PlatformType == HardwarePlatformType.LaptopLenovo ||
                   p.Manufacturer.Contains("Lenovo", StringComparison.OrdinalIgnoreCase);
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

            using var searcher = new ManagementObjectSearcher(_scope, new ObjectQuery("SELECT * FROM LENOVO_GAMEZONE_DATA"));
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
        return SetThermalMode(isAuto, speedPercent);
    }

    public bool SetGpuFan(bool isAuto, int speedPercent)
    {
        return SetThermalMode(isAuto, speedPercent);
    }

    public bool SetCoolBoost(bool enabled)
    {
        // Performance Mode / Full Speed
        return SetThermalMode(false, enabled ? 100 : 50);
    }

    private bool SetThermalMode(bool isAuto, int speedPercent)
    {
        if (!_available || _scope == null) return false;

        try
        {
            // Lenovo Thermal Modes: 1 = Quiet, 2 = Balanced, 3 = Performance / Extreme
            uint mode = isAuto ? 2u : (speedPercent >= 80 ? 3u : (speedPercent <= 40 ? 1u : 2u));

            using var searcher = new ManagementObjectSearcher(_scope, new ObjectQuery("SELECT * FROM LENOVO_THERMAL_METHOD"));
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    var inParams = obj.GetMethodParameters("SetThermalMode");
                    inParams["Mode"] = mode;
                    obj.InvokeMethod("SetThermalMode", inParams, null);
                    return true;
                }
            }
        }
        catch { }

        return false;
    }

    public FanTachometerReading ReadTachometers()
    {
        return new FanTachometerReading();
    }

    public void EmergencyReset()
    {
        SetThermalMode(true, 50);
    }

    public void Dispose()
    {
        _scope = null;
        _available = false;
    }
}
