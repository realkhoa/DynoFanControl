using System.IO;
using System.Text.Json;

namespace FanCtl;

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

    public string ActiveCurvePreset { get; set; } = "Balanced"; // Silent, Balanced, Gaming, Custom
    public List<CurvePoint> CustomCurvePoints { get; set; } = new()
    {
        new(40, 20),
        new(50, 35),
        new(60, 50),
        new(72, 70),
        new(82, 90),
        new(88, 100)
    };
}

public static class ConfigManager
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FanCtl");
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");
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
