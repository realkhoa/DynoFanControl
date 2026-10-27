using System.IO;
using System.Text.Json;

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
    public bool StartWithWindows { get; set; } = false;
    public bool StartMinimized { get; set; } = false;
    public bool CloseToTray { get; set; } = true;
    public bool LinkFans { get; set; } = false;
    public bool CoolBoost { get; set; } = false;
    public bool OverheatProtection { get; set; } = true;
    public int OverheatThreshold { get; set; } = 88;
    public int PollIntervalSeconds { get; set; } = 2;

    public string ActivePreset { get; set; } = "Auto"; // Auto, Silent, Balanced, Turbo, Curve, Custom
    public int CpuManualSpeed { get; set; } = 50;
    public int GpuManualSpeed { get; set; } = 50;

    public bool SyncFanCurves { get; set; } = false; // False = Separate independent curves for CPU & GPU
    public string ActiveCurvePreset { get; set; } = "Balanced"; // Legacy fallback
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
}

public static class ConfigManager
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynoFanControl");
    private static readonly string LegacyConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FanCtl");
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");
    private static readonly string LegacyConfigPath = Path.Combine(LegacyConfigDir, "config.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null) return settings;
            }
            else if (File.Exists(LegacyConfigPath))
            {
                var json = File.ReadAllText(LegacyConfigPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
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

        var defaultSettings = new AppSettings();
        Save(defaultSettings);
        return defaultSettings;
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(ConfigPath, json);
        }
        catch
        {
            // Suppress IO errors on save
        }
    }
}
