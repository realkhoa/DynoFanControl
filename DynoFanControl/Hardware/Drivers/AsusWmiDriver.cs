using System.Management;

namespace DynoFanControl.Hardware.Drivers;

/// <summary>
/// ASUS ROG, TUF, Zephyrus, and ZenBook WMI ACPI driver.
/// Communicates through root\wmi: ASUS_WMI or \\.\ATKACPI device driver.
/// </summary>
public sealed class AsusWmiDriver : IFanControlDriver
{
    private const uint DEVID_FAN_BOOST = 0x00110013; // 0=Balanced, 1=Turbo/Overboost, 2=Silent
    private const uint DEVID_CPU_FAN   = 0x00110024;
    private const uint DEVID_GPU_FAN   = 0x00110025;

    private ManagementScope? _scope;
    private bool _initialized;
    private bool _available;

    public string DriverName => "ASUS ROG/TUF WMI Driver";
    public string PlatformDescription => "ASUS ROG, TUF Gaming & Zephyrus ACPI WMI Protocol";
    public HardwarePlatformType PlatformType => HardwarePlatformType.LaptopAsus;
    public int PriorityScore => 90;

    public bool IsSupported
    {
        get
        {
            var p = PlatformDetector.GetProfile();
            return p.PlatformType == HardwarePlatformType.LaptopAsus ||
                   p.Manufacturer.Contains("ASUS", StringComparison.OrdinalIgnoreCase);
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

            using var searcher = new ManagementObjectSearcher(_scope, new ObjectQuery("SELECT * FROM ASUS_WMI"));
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
        if (!_available) return false;
        // In auto mode, restore balanced (0); in manual, scale speed
        var val = isAuto ? 0u : (speedPercent >= 80 ? 1u : (speedPercent <= 40 ? 2u : 0u));
        return CallDevs(DEVID_FAN_BOOST, val);
    }

    public bool SetGpuFan(bool isAuto, int speedPercent)
    {
        if (!_available) return false;
        var val = isAuto ? 0u : (speedPercent >= 80 ? 1u : (speedPercent <= 40 ? 2u : 0u));
        return CallDevs(DEVID_FAN_BOOST, val);
    }

    public bool SetCoolBoost(bool enabled)
    {
        // 1 = Overboost/Turbo mode
        return CallDevs(DEVID_FAN_BOOST, enabled ? 1u : 0u);
    }

    private bool CallDevs(uint deviceId, uint status)
    {
        if (!_available || _scope == null) return false;

        try
        {
            using var searcher = new ManagementObjectSearcher(_scope, new ObjectQuery("SELECT * FROM ASUS_WMI"));
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    var inParams = obj.GetMethodParameters("DEVS");
                    inParams["Dev_ID"] = deviceId;
                    inParams["Control_Status"] = status;
                    obj.InvokeMethod("DEVS", inParams, null);
                    return true;
                }
            }
        }
        catch { }

        return false;
    }

    public FanTachometerReading ReadTachometers()
    {
        var reading = new FanTachometerReading();
        // ASUS thermal tachometers can be queried through DSTS
        return reading;
    }

    public void EmergencyReset()
    {
        CallDevs(DEVID_FAN_BOOST, 0u); // Restore Balanced / Auto
    }

    public void Dispose()
    {
        _scope = null;
        _available = false;
    }
}
