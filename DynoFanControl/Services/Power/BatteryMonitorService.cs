using System.Runtime.InteropServices;

namespace DynoFanControl.Services.Power;

/// <summary>
/// Monitors system AC line status and battery charge percentage using kernel32 GetSystemPowerStatus.
/// Enables automatic profile switching between plugged-in gaming performance and on-battery power saving.
/// </summary>
public sealed class BatteryMonitorService
{
    private static readonly Lazy<BatteryMonitorService> _instance = new(() => new BatteryMonitorService());
    public static BatteryMonitorService Instance => _instance.Value;

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;         // 0 = Offline, 1 = Online, 255 = Unknown
        public byte BatteryFlag;          // 1 = High, 2 = Low, 4 = Critical, 8 = Charging, 128 = No battery
        public byte BatteryLifePercent;    // 0-100%, 255 = Unknown
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    private readonly object _lock = new();
    private bool _hasBattery = true;
    private bool _isOnAc = true;
    private int _batteryPercent = 100;
    private bool _isInitialized;

    public bool HasBattery => _hasBattery;
    public bool IsOnAc => _isOnAc;
    public int BatteryPercent => _batteryPercent;
    public string PowerSourceText => !_hasBattery 
        ? "Desktop AC Power" 
        : _isOnAc 
            ? $"⚡ AC Power ({BatteryPercent}%)" 
            : $"🔋 Battery ({BatteryPercent}%)";

    public event Action<bool, int>? PowerStateChanged;

    private BatteryMonitorService() { }

    /// <summary>
    /// Checks the current power status and notifies listeners if the power source transitioned.
    /// </summary>
    public void PollPowerStatus()
    {
        if (!GetSystemPowerStatus(out var status)) return;

        lock (_lock)
        {
            bool hasBattery = (status.BatteryFlag & 128) == 0 && status.BatteryLifePercent <= 100;
            bool isOnAc = status.ACLineStatus == 1;
            int pct = status.BatteryLifePercent <= 100 ? status.BatteryLifePercent : 100;

            _hasBattery = hasBattery;
            _batteryPercent = pct;

            if (!_isInitialized)
            {
                _isOnAc = isOnAc;
                _isInitialized = true;
                return;
            }

            if (_isOnAc != isOnAc)
            {
                _isOnAc = isOnAc;
                PowerStateChanged?.Invoke(isOnAc, pct);
            }
        }
    }
}
