using System.Management;

namespace DynoFanControl.Hardware.Drivers;

public class SystemHardwareProfile
{
    public string Manufacturer { get; set; } = "Unknown";
    public string Model { get; set; } = "Unknown";
    public string MotherboardManufacturer { get; set; } = "Unknown";
    public string MotherboardModel { get; set; } = "Unknown";
    public bool IsLaptop { get; set; } = true;
    public HardwarePlatformType PlatformType { get; set; } = HardwarePlatformType.Unknown;
    public string BiosVersion { get; set; } = "";
    public string CpuName { get; set; } = "";
    public int PhysicalCores { get; set; }
    public int LogicalProcessors { get; set; }

    public string SummaryText => IsLaptop
        ? $"{Manufacturer} {Model} (Laptop)"
        : $"{MotherboardManufacturer} {MotherboardModel} (Desktop PC)";
}

/// <summary>
/// Detects host machine hardware, manufacturer, form factor, and board profile.
/// </summary>
public static class PlatformDetector
{
    private static SystemHardwareProfile? _cachedProfile;
    private static readonly object _lock = new();

    public static SystemHardwareProfile GetProfile()
    {
        lock (_lock)
        {
            if (_cachedProfile != null) return _cachedProfile;

            var profile = new SystemHardwareProfile();

            // 1. Query Win32_ComputerSystem
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Model, NumberOfProcessors, NumberOfLogicalProcessors FROM Win32_ComputerSystem");
                foreach (var obj in searcher.Get())
                {
                    profile.Manufacturer = (obj["Manufacturer"]?.ToString() ?? "").Trim();
                    profile.Model = (obj["Model"]?.ToString() ?? "").Trim();
                    if (int.TryParse(obj["NumberOfLogicalProcessors"]?.ToString(), out var logical))
                        profile.LogicalProcessors = logical;
                    break;
                }
            }
            catch { }

            // 1.5 Query Win32_ComputerSystemProduct (Critical for Lenovo Legion/LOQ/IdeaPad to obtain commercial branding)
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, Version, Vendor FROM Win32_ComputerSystemProduct");
                foreach (var obj in searcher.Get())
                {
                    var prodName = (obj["Name"]?.ToString() ?? "").Trim();
                    var prodVersion = (obj["Version"]?.ToString() ?? "").Trim();
                    var prodVendor = (obj["Vendor"]?.ToString() ?? "").Trim();

                    if (string.IsNullOrWhiteSpace(profile.Manufacturer) || profile.Manufacturer == "Unknown")
                    {
                        if (!string.IsNullOrWhiteSpace(prodVendor))
                            profile.Manufacturer = prodVendor;
                    }

                    // On Lenovo, Win32_ComputerSystemProduct.Version contains "Legion 5 15ACH6",
                    // while Win32_ComputerSystem.Model is an MTM code like "82JU".
                    // Prioritize friendlier product branding:
                    string[] brandingKeywords = ["Legion", "LOQ", "IdeaPad", "ThinkPad", "Nitro", "Predator", "ROG", "TUF", "Alienware", "OMEN", "Victus"];
                    bool matchedBranding = false;

                    foreach (var kw in brandingKeywords)
                    {
                        if (prodVersion.Contains(kw, StringComparison.OrdinalIgnoreCase))
                        {
                            profile.Model = prodVersion;
                            matchedBranding = true;
                            break;
                        }
                        if (prodName.Contains(kw, StringComparison.OrdinalIgnoreCase))
                        {
                            profile.Model = prodName;
                            matchedBranding = true;
                            break;
                        }
                    }

                    if (!matchedBranding && (string.IsNullOrWhiteSpace(profile.Model) || profile.Model == "Unknown"))
                    {
                        if (!string.IsNullOrWhiteSpace(prodName))
                            profile.Model = prodName;
                        else if (!string.IsNullOrWhiteSpace(prodVersion))
                            profile.Model = prodVersion;
                    }

                    break;
                }
            }
            catch { }

            // 2. Query Win32_BaseBoard
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");
                foreach (var obj in searcher.Get())
                {
                    profile.MotherboardManufacturer = (obj["Manufacturer"]?.ToString() ?? "").Trim();
                    profile.MotherboardModel = (obj["Product"]?.ToString() ?? "").Trim();
                    break;
                }
            }
            catch { }

            // 3. Query Win32_Processor
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores FROM Win32_Processor");
                foreach (var obj in searcher.Get())
                {
                    profile.CpuName = (obj["Name"]?.ToString() ?? "").Trim();
                    if (int.TryParse(obj["NumberOfCores"]?.ToString(), out var cores))
                        profile.PhysicalCores = cores;
                    break;
                }
            }
            catch { }

            // 4. Query Win32_SystemEnclosure for Chassis Types
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure");
                foreach (var obj in searcher.Get())
                {
                    if (obj["ChassisTypes"] is ushort[] types && types.Length > 0)
                    {
                        var t = types[0];
                        // 8=Portable, 9=Laptop, 10=Notebook, 11=HandHeld, 12=DockingStation, 14=SubNotebook, 30=Tablet, 31=Convertible, 32=Detachable
                        profile.IsLaptop = t is 8 or 9 or 10 or 11 or 12 or 14 or 30 or 31 or 32;
                    }
                    break;
                }
            }
            catch { }

            // 5. Query Win32_BIOS
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion FROM Win32_BIOS");
                foreach (var obj in searcher.Get())
                {
                    profile.BiosVersion = (obj["SMBIOSBIOSVersion"]?.ToString() ?? "").Trim();
                    break;
                }
            }
            catch { }

            // 6. Classify Platform Type
            profile.PlatformType = ClassifyPlatform(profile);

            _cachedProfile = profile;
            return profile;
        }
    }

    private static HardwarePlatformType ClassifyPlatform(SystemHardwareProfile p)
    {
        var mfg = (p.Manufacturer + " " + p.MotherboardManufacturer).ToLowerInvariant();
        var model = (p.Model + " " + p.MotherboardModel).ToLowerInvariant();

        if (mfg.Contains("acer"))
            return HardwarePlatformType.LaptopAcer;

        if (mfg.Contains("asus") || mfg.Contains("asustek"))
            return p.IsLaptop ? HardwarePlatformType.LaptopAsus : HardwarePlatformType.DesktopPc;

        if (mfg.Contains("lenovo") ||
            model.Contains("legion") ||
            model.Contains("loq") ||
            model.Contains("ideapad") ||
            model.Contains("thinkpad") ||
            model.Contains("thinkbook"))
            return HardwarePlatformType.LaptopLenovo;

        if (mfg.Contains("msi") || mfg.Contains("micro-star"))
            return p.IsLaptop ? HardwarePlatformType.LaptopMsi : HardwarePlatformType.DesktopPc;

        if (mfg.Contains("dell") || mfg.Contains("alienware"))
            return HardwarePlatformType.LaptopDellAlienware;

        if (mfg.Contains("hp") || mfg.Contains("hewlett-packard"))
            return HardwarePlatformType.LaptopHpOmen;

        if (p.IsLaptop)
            return HardwarePlatformType.LaptopGeneric;

        return HardwarePlatformType.DesktopPc;
    }
}
