using System.IO;
using System.Text.Json;
using DynoFanControl.Hardware.Tuning;
using DynoFanControl.Services.Osd;

namespace DynoFanControl;

public class CurvePoint
{
    public int Temperature { get; set; }
    public int FanSpeed { get; set; }

    public CurvePoint() { }
    public CurvePoint(int temp, int speed)
    {
        Temperature = temp;
        FanSpeed = speed;
    }
}

public class AppSettings
{
    public bool StartWithWindows { get; set; } = true; // Default auto-start with Windows
    public bool StartMinimized { get; set; } = false;
    public bool CloseToTray { get; set; } = true;
    public bool LinkFans { get; set; } = false;
    public bool CoolBoost { get; set; } = false;
    public bool OverheatProtection { get; set; } = true;
    public int OverheatThreshold { get; set; } = 88;
    public int PollIntervalSeconds { get; set; } = 2;

    public string ActivePreset { get; set; } = "Balanced"; // Auto, Silent, Balanced, Turbo, Curve, Custom
    public int CpuManualSpeed { get; set; } = 50;
    public int GpuManualSpeed { get; set; } = 50;
    public string? PreferredGpuId { get; set; }

    public bool SyncFanCurves { get; set; } = false; // False = Separate independent curves for CPU & GPU
    public string ActiveCurvePreset { get; set; } = "Balanced"; // Silent, Balanced, Gaming, Custom
    public string CpuCurvePreset { get; set; } = "Balanced"; // Silent, Balanced, Gaming, Custom
    public string GpuCurvePreset { get; set; } = "Balanced"; // Silent, Balanced, Gaming, Custom

    public List<CurvePoint> CustomCurvePoints { get; set; } = new()
    {
        new(40, 20),
        new(50, 35),
        new(60, 50),
        new(72, 70),
        new(82, 90),
        new(88, 100)
    };

    public List<CurvePoint> CustomCpuCurvePoints { get; set; } = new()
    {
        new(40, 20),
        new(50, 35),
        new(60, 50),
        new(72, 70),
        new(82, 90),
        new(88, 100)
    };

    public List<CurvePoint> CustomGpuCurvePoints { get; set; } = new()
    {
        new(40, 20),
        new(50, 35),
        new(60, 50),
        new(70, 75),
        new(78, 90),
        new(85, 100)
    };

    // MSI Afterburner Suite: GPU Tuning & OSD
    public GpuTuningProfile CurrentTuningProfile { get; set; } = GpuTuningProfile.Stock;
    public bool ApplyTuningAtStartup { get; set; } = false;
    public OsdSettings Osd { get; set; } = new();
}

public static class ConfigManager
{
    // 1. User LocalAppData: %LocalAppData%\DynoFanControl\config.json
    private static readonly string UserConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynoFanControl");
    private static readonly string UserConfigPath = Path.Combine(UserConfigDir, "config.json");

    // 2. Global System-wide ProgramData: %ProgramData%\DynoFanControl\config.json
    private static readonly string GlobalConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DynoFanControl");
    private static readonly string GlobalConfigPath = Path.Combine(GlobalConfigDir, "config.json");

    // 3. Application Directory (Portable config if present)
    private static readonly string AppDirConfigPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "config.json");

    // 4. Legacy migration path: %LocalAppData%\FanCtl\config.json
    private static readonly string LegacyConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FanCtl", "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            // 1. Check portable config next to executable
            if (File.Exists(AppDirConfigPath))
            {
                var settings = ReadSettingsFromFile(AppDirConfigPath);
                if (settings != null) return settings;
            }

            // 2. Check User LocalAppData
            if (File.Exists(UserConfigPath))
            {
                var settings = ReadSettingsFromFile(UserConfigPath);
                if (settings != null) return settings;
            }

            // 3. Check Global ProgramData
            if (File.Exists(GlobalConfigPath))
            {
                var settings = ReadSettingsFromFile(GlobalConfigPath);
                if (settings != null) return settings;
            }

            // 4. Check Legacy FanCtl path
            if (File.Exists(LegacyConfigPath))
            {
                var settings = ReadSettingsFromFile(LegacyConfigPath);
                if (settings != null)
                {
                    Save(settings);
                    return settings;
                }
            }
        }
        catch
        {
            // Fallback to defaults on corrupt config
        }

        var defaultSettings = new AppSettings { StartWithWindows = true };
        Save(defaultSettings);
        return defaultSettings;
    }

    private static AppSettings? ReadSettingsFromFile(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOptions);

            // 1. Save to User LocalAppData
            try
            {
                Directory.CreateDirectory(UserConfigDir);
                File.WriteAllText(UserConfigPath, json);
            }
            catch { }

            // 2. Save to Global System-wide ProgramData (All users / elevated services)
            try
            {
                Directory.CreateDirectory(GlobalConfigDir);
                File.WriteAllText(GlobalConfigPath, json);
            }
            catch { }

            // 3. Save to portable config next to executable if it exists
            try
            {
                if (File.Exists(AppDirConfigPath))
                {
                    File.WriteAllText(AppDirConfigPath, json);
                }
            }
            catch { }
        }
        catch
        {
            // Suppress IO errors on save
        }
    }
}
