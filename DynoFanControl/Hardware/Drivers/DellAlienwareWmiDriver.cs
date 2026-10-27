using System.Management;

namespace DynoFanControl.Hardware.Drivers;

/// <summary>
/// Dell Alienware & Dell G-Series Gaming Laptop WMI driver.
/// Operates through AWCC (Alienware Command Center) WMI interface root\wmi: AWCC_WmiMethod.
/// </summary>
public sealed class DellAlienwareWmiDriver : IFanControlDriver
{
    private ManagementScope? _scope;
    private bool _initialized;
    private bool _available;

    public string DriverName => "Dell Alienware Command Center WMI";
    public string PlatformDescription => "Dell Alienware & G-Series Gaming Thermal Control";
    public HardwarePlatformType PlatformType => HardwarePlatformType.LaptopDellAlienware;
    public int PriorityScore => 90;

    public bool IsSupported
    {
        get
        {
            var p = PlatformDetector.GetProfile();
            return p.PlatformType == HardwarePlatformType.LaptopDellAlienware ||
                   p.Manufacturer.Contains("Alienware", StringComparison.OrdinalIgnoreCase) ||
                   p.Manufacturer.Contains("Dell", StringComparison.OrdinalIgnoreCase);
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

            using var searcher = new ManagementObjectSearcher(_scope, new ObjectQuery("SELECT * FROM AWCC_WmiMethod"));
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
        return SetThermalProfile(isAuto, speedPercent);
    }

    public bool SetGpuFan(bool isAuto, int speedPercent)
    {
        return SetThermalProfile(isAuto, speedPercent);
    }

    public bool SetCoolBoost(bool enabled)
    {
        return SetThermalProfile(false, enabled ? 100 : 50);
    }

    private bool SetThermalProfile(bool isAuto, int speedPercent)
    {
        if (!_available || _scope == null) return false;

        try
        {
            // Dell thermal profiles: 0x96=Balanced, 0x97=Performance, 0x98=Cool, 0x99=Quiet, 0xAB=FullSpeed
            uint profileId = isAuto ? 0x96u : (speedPercent >= 85 ? 0xABu : (speedPercent <= 40 ? 0x99u : 0x96u));

            using var searcher = new ManagementObjectSearcher(_scope, new ObjectQuery("SELECT * FROM AWCC_WmiMethod"));
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    var inParams = obj.GetMethodParameters("SetThermalProfile");
                    inParams["ThermalProfile"] = profileId;
                    obj.InvokeMethod("SetThermalProfile", inParams, null);
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
        SetThermalProfile(true, 50);
    }

    public void Dispose()
    {
        _scope = null;
        _available = false;
    }
}
