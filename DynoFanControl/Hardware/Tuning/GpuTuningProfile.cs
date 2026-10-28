namespace DynoFanControl.Hardware.Tuning;

/// <summary>
/// GPU Tuning Configuration Profile encapsulating Core/Memory Clock Offsets,
/// Power Target, Thermal Target, and Core Voltage.
/// </summary>
public sealed class GpuTuningProfile
{
    public string ProfileName { get; set; } = "Default Stock";

    /// <summary>
    /// Core Clock Boost Offset in MHz (-500 to +1000 MHz).
    /// </summary>
    public int CoreClockOffsetMhz { get; set; } = 0;

    /// <summary>
    /// Memory Clock Boost Offset in MHz (-500 to +1500 MHz).
    /// </summary>
    public int MemoryClockOffsetMhz { get; set; } = 0;

    /// <summary>
    /// GPU Power Limit Target Percentage (50% to 130%).
    /// </summary>
    public int PowerLimitPercent { get; set; } = 100;

    /// <summary>
    /// GPU Thermal Throttle Target in °C (65°C to 90°C).
    /// </summary>
    public int TempLimitCelsius { get; set; } = 83;

    /// <summary>
    /// Core Voltage Offset in mV (0 to 100 mV).
    /// </summary>
    public int VoltageOffsetMv { get; set; } = 0;

    /// <summary>
    /// If true, temperature limit takes priority over power limit.
    /// </summary>
    public bool PrioritizeTemperature { get; set; } = false;

    public GpuTuningProfile Clone(string newName)
    {
        return new GpuTuningProfile
        {
            ProfileName = newName,
            CoreClockOffsetMhz = CoreClockOffsetMhz,
            MemoryClockOffsetMhz = MemoryClockOffsetMhz,
            PowerLimitPercent = PowerLimitPercent,
            TempLimitCelsius = TempLimitCelsius,
            VoltageOffsetMv = VoltageOffsetMv,
            PrioritizeTemperature = PrioritizeTemperature
        };
    }

    public static GpuTuningProfile Stock => new()
    {
        ProfileName = "Stock Default",
        CoreClockOffsetMhz = 0,
        MemoryClockOffsetMhz = 0,
        PowerLimitPercent = 100,
        TempLimitCelsius = 83,
        VoltageOffsetMv = 0,
        PrioritizeTemperature = false
    };

    public static GpuTuningProfile Undervolt => new()
    {
        ProfileName = "Undervolt & Silent",
        CoreClockOffsetMhz = 45,
        MemoryClockOffsetMhz = 150,
        PowerLimitPercent = 85,
        TempLimitCelsius = 75,
        VoltageOffsetMv = 0,
        PrioritizeTemperature = true
    };

    public static GpuTuningProfile Balanced => new()
    {
        ProfileName = "Balanced Gaming",
        CoreClockOffsetMhz = 60,
        MemoryClockOffsetMhz = 300,
        PowerLimitPercent = 100,
        TempLimitCelsius = 83,
        VoltageOffsetMv = 0,
        PrioritizeTemperature = false
    };

    public static GpuTuningProfile MaxOverclock => new()
    {
        ProfileName = "Max Overclock",
        CoreClockOffsetMhz = 130,
        MemoryClockOffsetMhz = 600,
        PowerLimitPercent = 115,
        TempLimitCelsius = 87,
        VoltageOffsetMv = 20,
        PrioritizeTemperature = false
    };

    public static GpuTuningProfile Eco => new()
    {
        ProfileName = "Eco / Battery Saver",
        CoreClockOffsetMhz = -150,
        MemoryClockOffsetMhz = -100,
        PowerLimitPercent = 70,
        TempLimitCelsius = 70,
        VoltageOffsetMv = 0,
        PrioritizeTemperature = true
    };
}
