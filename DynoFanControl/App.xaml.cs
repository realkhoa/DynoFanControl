using System.Threading;
using System.Windows;
using DynoFanControl.Hardware;

namespace DynoFanControl;

public partial class App : Application
{
    private const string MutexName = "Global\\DynoFanControl_Nitro_SingleInstance_Mutex";
    private Mutex? _mutex;

    public static bool StartMinimized { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        // 1. Single Instance Check
        try
        {
            _mutex = new Mutex(true, MutexName, out bool isNewInstance);
            if (!isNewInstance)
            {
                // Another instance is already active
                Shutdown();
                return;
            }
        }
        catch (AbandonedMutexException)
        {
            // Previous crashed instance abandoned mutex; safe to continue
        }
        catch
        {
            // Ignore mutex errors on non-standard environments
        }

        // 2. Check command line flags
        foreach (var arg in e.Args)
        {
            if (arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("/minimized", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-m", StringComparison.OrdinalIgnoreCase))
            {
                StartMinimized = true;
            }
        }

        // 3. Register Failsafe Hardware Reset Handlers
        AppDomain.CurrentDomain.ProcessExit += (_, _) => OnEmergencyExit();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => OnEmergencyExit();
        DispatcherUnhandledException += (_, args) =>
        {
            OnEmergencyExit();
            // Don't crash abruptly without restoring fan control
            args.Handled = false;
        };

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        OnEmergencyExit();
        try
        {
            _mutex?.ReleaseMutex();
        }
        catch
        {
            // Suppress error if calling thread does not own mutex
        }
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private static void OnEmergencyExit()
    {
        try
        {
            FanHardwareManager.Instance.EmergencyResetToAuto();
            FanHardwareManager.Instance.Dispose();
        }
        catch
        {
            // Suppress teardown errors
        }
    }
}
