namespace DynoFanControl.Hardware.Gpu;

public enum GpuVendor
{
    Unknown,
    Nvidia,
    Amd,
    Intel
}

public class GpuDeviceInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "Generic Graphics Device";
    public GpuVendor Vendor { get; set; } = GpuVendor.Unknown;
    public bool IsDiscrete { get; set; } = true;
    public double Temperature { get; set; }
    public uint UtilizationGpu { get; set; }
    public uint UtilizationMemory { get; set; }
    public double PowerWatts { get; set; }
    public bool IsSleeping { get; set; }
    public uint FanRpm { get; set; }
    public int FanSpeedPercent { get; set; }
    public bool HasFanControl { get; set; }
    public string DriverVersion { get; set; } = "";
    public string VramTotalText { get; set; } = "";
    public uint CoreClockMhz { get; set; }
    public uint MemoryClockMhz { get; set; }
    public double VramUsedMb { get; set; }
    public double VramTotalMb { get; set; }

    public string FormattedClocks => CoreClockMhz > 0 ? $"{CoreClockMhz} MHz Core • {MemoryClockMhz} MHz Mem" : "";
    public string FormattedVram => VramTotalMb > 0 ? $"{VramUsedMb:F0} / {VramTotalMb:F0} MB ({((VramUsedMb / VramTotalMb) * 100):F0}%)" : "";

    public string VendorBadge => Vendor switch
    {
        GpuVendor.Nvidia => "NVIDIA",
        GpuVendor.Amd => "AMD RADEON",
        GpuVendor.Intel => "INTEL",
        _ => "GPU"
    };

    public string TypeBadge => IsDiscrete ? "dGPU" : "iGPU";

    public string DetailsText => IsSleeping
        ? "Power Savings (Sleep)"
        : $"{UtilizationGpu}% Load • {PowerWatts:F1}W";

    public string FormattedTemp => Temperature > 0 ? $"{Temperature:F1}°C" : (IsSleeping ? "Sleep" : "--.-°C");
}
