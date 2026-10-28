namespace DynoFanControl.Services.Automation;

/// <summary>
/// Automation rule associating an executable process with a designated cooling mode.
/// </summary>
public class AppProfileRule
{
    public string ProcessName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string TargetMode { get; set; } = "Turbo"; // Auto, Silent, Balanced, Turbo, Curve, Custom
    public string CurvePreset { get; set; } = "Gaming";
    public bool EnableOsd { get; set; } = true;
    public bool IsEnabled { get; set; } = true;

    public AppProfileRule() { }

    public AppProfileRule(string processName, string displayName, string targetMode, string curvePreset = "Gaming", bool enableOsd = true)
    {
        ProcessName = processName.ToLowerInvariant().Replace(".exe", "").Trim();
        DisplayName = displayName;
        TargetMode = targetMode;
        CurvePreset = curvePreset;
        EnableOsd = enableOsd;
        IsEnabled = true;
    }
}
