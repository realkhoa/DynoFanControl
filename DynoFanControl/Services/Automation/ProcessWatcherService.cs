using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace DynoFanControl.Services.Automation;

/// <summary>
/// Monitors the active foreground application window and triggers automatic profile switches
/// when recognized games or applications gain focus.
/// </summary>
public sealed class ProcessWatcherService
{
    private static readonly Lazy<ProcessWatcherService> _instance = new(() => new ProcessWatcherService());
    public static ProcessWatcherService Instance => _instance.Value;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private readonly object _lock = new();
    private string _currentForegroundProcess = "";
    private AppProfileRule? _activeMatchedRule;
    private bool _inGameMode;

    public string CurrentForegroundProcess => _currentForegroundProcess;
    public AppProfileRule? ActiveMatchedRule => _activeMatchedRule;
    public bool IsInGameMode => _inGameMode;

    public event Action<AppProfileRule>? RuleActivated;
    public event Action? RuleDeactivated;

    private ProcessWatcherService() { }

    /// <summary>
    /// Evaluates the current foreground process against configured rules.
    /// Called periodically by the hardware polling loop.
    /// </summary>
    public void CheckForegroundProcess(IList<AppProfileRule> rules)
    {
        if (rules == null || rules.Count == 0) return;

        string activeProc = GetActiveProcessName();
        if (string.IsNullOrEmpty(activeProc)) return;

        lock (_lock)
        {
            if (string.Equals(_currentForegroundProcess, activeProc, StringComparison.OrdinalIgnoreCase))
                return; // No change in active application

            _currentForegroundProcess = activeProc;

            // Search for enabled matching rule
            var match = rules.FirstOrDefault(r => 
                r.IsEnabled && 
                string.Equals(r.ProcessName, activeProc, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                _activeMatchedRule = match;
                _inGameMode = true;
                RuleActivated?.Invoke(match);
            }
            else if (_inGameMode)
            {
                // Returned to desktop or non-game process
                _inGameMode = false;
                _activeMatchedRule = null;
                RuleDeactivated?.Invoke();
            }
        }
    }

    public static string GetActiveProcessName()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return string.Empty;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return string.Empty;

            using var proc = Process.GetProcessById((int)pid);
            return proc.ProcessName.ToLowerInvariant();
        }
        catch
        {
            return string.Empty;
        }
    }
}
