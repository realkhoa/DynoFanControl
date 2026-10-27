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
