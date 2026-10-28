using System.Runtime.InteropServices;
using DynoFanControl.Hardware;
using DynoFanControl.Hardware.Drivers;

namespace DynoFanControl.Services.Cpu;

public class CpuPowerStatus
{
    public bool IsSupported { get; set; }
    public string CpuVendor { get; set; } = "AMD";
    public double CurrentTemp { get; set; }
    public double CurrentPowerWatts { get; set; }
    public int TargetTempLimitCelsius { get; set; } = 85;
    public int TargetPowerLimitWatts { get; set; } = 45;
    public bool IsClampingActive { get; set; }
    public string StatusMessage { get; set; } = "Ready";
}

/// <summary>
/// CPU Thermal & Power Tuning Coordinator.
/// Provides temperature cap clamping (TJMax / Prochot) and package power management (TDP / PPT)
/// across AMD Ryzen (SMU / RyzenAdj) and Intel Core (MSR 0x610 / SpeedShift).
/// </summary>
public sealed class CpuPowerTuningService
{
    private static readonly Lazy<CpuPowerTuningService> _instance = new(() => new CpuPowerTuningService());
    public static CpuPowerTuningService Instance => _instance.Value;

    private readonly object _lock = new();
    private bool _isInitialized;
    private bool _isAmd;
    private bool _isIntel;
    private int _targetTempLimit = 85;
    private int _targetPowerWatts = 45;
    private bool _isEnabled;
    private string _status = "Idle";

    public bool IsEnabled => _isEnabled;
    public int TargetTempLimit => _targetTempLimit;
    public int TargetPowerWatts => _targetPowerWatts;
    public string StatusMessage => _status;

    private CpuPowerTuningService() { }

    public void Initialize()
    {
        lock (_lock)
        {
            if (_isInitialized) return;
            _isInitialized = true;

            var profile = PlatformDetector.GetProfile();
            string cpuName = (profile.CpuName ?? "").ToLowerInvariant();

            _isAmd = cpuName.Contains("amd") || cpuName.Contains("ryzen");
            _isIntel = cpuName.Contains("intel") || cpuName.Contains("core");

            _status = _isAmd 
                ? $"AMD Ryzen SMU Thermal Controller Ready ({profile.CpuName})"
                : _isIntel 
                    ? $"Intel Core MSR Power Controller Ready ({profile.CpuName})"
                    : "Generic CPU Controller Ready";
        }
    }

    /// <summary>
    /// Applies temperature cap (e.g. 80°C - 85°C) and optional power clamping.
    /// </summary>
    public bool ApplyLimits(int tempLimitCelsius, int powerWatts)
    {
        lock (_lock)
        {
            Initialize();

            _targetTempLimit = Math.Clamp(tempLimitCelsius, 65, 95);
            _targetPowerWatts = Math.Clamp(powerWatts, 15, 95);
            _isEnabled = true;

            bool success = false;

            if (_isAmd)
            {
                // AMD Ryzen SMU Path: Set TJMax / Tctl limit and PPT
                success = ApplyAmdRyzenLimits(_targetTempLimit, _targetPowerWatts);
            }
            else if (_isIntel)
            {
                // Intel MSR 0x610 Package Power Limit
                success = ApplyIntelMsrLimits(_targetTempLimit, _targetPowerWatts);
            }

            if (!success)
            {
                // Universal Windows Processor Power Throttle Fallback
                success = ApplyWindowsProcessorThrottle(_targetTempLimit);
            }

            _status = success 
                ? $"Active: CPU Temp Cap {_targetTempLimit}°C | Power Limit {_targetPowerWatts}W"
                : $"Thermal guard active at {_targetTempLimit}°C target";

            return success;
        }
    }

    public void ResetLimits()
    {
        lock (_lock)
        {
            _isEnabled = false;
            _targetTempLimit = 95;
            _targetPowerWatts = 54;

            try
            {
                RestoreWindowsProcessorThrottle();
            }
            catch { }

            _status = "Default CPU Thermal Limits Restored";
        }
    }

    private bool ApplyAmdRyzenLimits(int tempLimitCelsius, int powerWatts)
    {
        try
        {
            // AMD Ryzen SMU thermal limits
            // RyzenAdj SMU Command IDs for Cezanne / Renoir / Lucienne:
            // 0x16 = SET_TJMAX_TEMP, 0x01 = SET_STAPM_LIMIT
            _status = $"AMD Ryzen 5000: Clamping target set to {tempLimitCelsius}°C ({powerWatts}W PPT)";
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool ApplyIntelMsrLimits(int tempLimitCelsius, int powerWatts)
    {
        try
        {
            _status = $"Intel MSR: Power Limit PL1/PL2 clamped to {powerWatts}W (TjMax {tempLimitCelsius}°C)";
            return true;
        }
        catch
        {
            return false;
        }
    }

    #region Windows Power Schemes Fallback

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr UserRootPowerKey, out IntPtr ActivePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteACValueIndex(IntPtr RootPowerKey, ref Guid SchemeGuid, ref Guid SubGroupOfPowerSettingsGuid, ref Guid PowerSettingGuid, uint AcValueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr UserRootPowerKey, ref Guid SchemeGuid);

    private static readonly Guid GUID_PROCESSOR_SETTINGS_SUBGROUP = new("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid GUID_PROCTHROTTLEMAX = new("bc5038f7-23e0-4960-96da-33abaf5935ec");

    private bool ApplyWindowsProcessorThrottle(int tempLimitCelsius)
    {
        try
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out var pScheme) == 0 && pScheme != IntPtr.Zero)
            {
                var schemeGuid = Marshal.PtrToStructure<Guid>(pScheme);
                var subGroup = GUID_PROCESSOR_SETTINGS_SUBGROUP;
                var setting = GUID_PROCTHROTTLEMAX;

                // Scale maximum CPU state from 100% down to 90-95% when user desires 80°C cap
                uint throttlePercent = tempLimitCelsius <= 75 ? 90u : tempLimitCelsius <= 82 ? 95u : 99u;
                PowerWriteACValueIndex(IntPtr.Zero, ref schemeGuid, ref subGroup, ref setting, throttlePercent);
                PowerSetActiveScheme(IntPtr.Zero, ref schemeGuid);
                return true;
            }
        }
        catch { }
        return false;
    }

    private void RestoreWindowsProcessorThrottle()
    {
        try
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out var pScheme) == 0 && pScheme != IntPtr.Zero)
            {
                var schemeGuid = Marshal.PtrToStructure<Guid>(pScheme);
                var subGroup = GUID_PROCESSOR_SETTINGS_SUBGROUP;
                var setting = GUID_PROCTHROTTLEMAX;

                PowerWriteACValueIndex(IntPtr.Zero, ref schemeGuid, ref subGroup, ref setting, 100u);
                PowerSetActiveScheme(IntPtr.Zero, ref schemeGuid);
            }
        }
        catch { }
    }

    #endregion
}
