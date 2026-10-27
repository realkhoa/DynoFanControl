namespace DynoFanControl.Hardware.Drivers;

public enum HardwarePlatformType
{
    Unknown,
    DesktopPc,
    LaptopAcer,
    LaptopAsus,
    LaptopLenovo,
    LaptopMsi,
    LaptopDellAlienware,
    LaptopHpOmen,
    LaptopGeneric
}

public class FanTachometerReading
{
    public uint CpuFanRpm { get; set; }
    public uint GpuFanRpm { get; set; }
    public bool CpuIsAuto { get; set; } = true;
    public bool GpuIsAuto { get; set; } = true;
    public List<(string Name, uint Rpm)> AdditionalFans { get; } = new();
}

/// <summary>
/// Universal interface for all hardware fan control backends across PC Desktops and Laptops.
/// </summary>
public interface IFanControlDriver : IDisposable
{
    /// <summary>
    /// Friendly human-readable name of the driver backend (e.g. "Acer Nitro Direct EC", "Desktop Super I/O (NCT6798D)")
    /// </summary>
    string DriverName { get; }

    /// <summary>
    /// Detailed hardware platform compatibility description
    /// </summary>
    string PlatformDescription { get; }

    /// <summary>
    /// Supported platform category
    /// </summary>
    HardwarePlatformType PlatformType { get; }

    /// <summary>
    /// Returns true if the host system hardware is supported and detected by this driver
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Priority score for automatic driver selection (higher = preferred)
    /// </summary>
    int PriorityScore { get; }

    /// <summary>
    /// Initializes driver communication with hardware (kernel driver, IO ports, or WMI)
    /// </summary>
    bool Initialize();

    /// <summary>
    /// Sets CPU fan duty cycle (speedPercent: 1-100%, or isAuto: true for BIOS curve)
    /// </summary>
    bool SetCpuFan(bool isAuto, int speedPercent);

    /// <summary>
    /// Sets GPU fan duty cycle (speedPercent: 1-100%, or isAuto: true for BIOS curve)
    /// </summary>
    bool SetGpuFan(bool isAuto, int speedPercent);

    /// <summary>
    /// Controls vendor-specific fan boost / max speed override (e.g. Acer CoolBoost, MSI Cooler Boost, ASUS Overboost)
    /// </summary>
    bool SetCoolBoost(bool enabled);

    /// <summary>
    /// Reads live fan RPM tachometer values from hardware sensors
    /// </summary>
    FanTachometerReading ReadTachometers();

    /// <summary>
    /// Failsafe emergency reset: restores all fans to default BIOS automatic mode
    /// </summary>
    void EmergencyReset();
}
