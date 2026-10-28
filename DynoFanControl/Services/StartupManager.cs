using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace DynoFanControl.Services;

/// <summary>
/// Manages running DynoFanControl on Windows startup.
/// Implements dual-redundancy registration:
/// 1. Primary: Windows Task Scheduler with Highest Privileges (/rl highest) for silent, elevated boot without UAC prompts.
/// 2. Secondary: HKCU\Software\Microsoft\Windows\CurrentVersion\Run registry key as standard user fallback.
/// </summary>
public static class StartupManager
{
    private const string TaskName = "DynoFanControl_NitroAutoStart";
    private const string RegistryKeyName = "DynoFanControl";
    private const string LegacyRegistryKeyName = "FanCtl";

    public static bool IsStartupEnabled()
    {
        // 1. Check Windows Task Scheduler
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("/query");
            psi.ArgumentList.Add("/tn");
            psi.ArgumentList.Add(TaskName);

            using var p = Process.Start(psi);
            if (p != null)
            {
                p.WaitForExit(2000);
                if (p.ExitCode == 0) return true;
            }
        }
        catch { }

        // 2. Check HKCU\Run registry key fallback
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
            var val = key?.GetValue(RegistryKeyName) as string ?? key?.GetValue(LegacyRegistryKeyName) as string;
            if (!string.IsNullOrEmpty(val)) return true;
        }
        catch { }

        return false;
    }

    public static bool SetStartup(bool enable, bool startMinimized = true)
    {
        bool taskSchedulerSuccess = false;
        bool registrySuccess = false;

        var exePath = Environment.ProcessPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DynoFanControl.exe");
        var args = startMinimized ? "--minimized" : "";
        var taskRun = string.IsNullOrEmpty(args) ? $"\"{exePath}\"" : $"\"{exePath}\" {args}";

        if (enable)
        {
            // 1. Register with Windows Task Scheduler (Elevated logon trigger, bypasses UAC)
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                psi.ArgumentList.Add("/create");
                psi.ArgumentList.Add("/tn");
                psi.ArgumentList.Add(TaskName);
                psi.ArgumentList.Add("/tr");
                psi.ArgumentList.Add(taskRun);
                psi.ArgumentList.Add("/sc");
                psi.ArgumentList.Add("onlogon");
                psi.ArgumentList.Add("/rl");
                psi.ArgumentList.Add("highest");
                psi.ArgumentList.Add("/f");

                using var p = Process.Start(psi);
                if (p != null)
                {
                    p.WaitForExit(3000);
                    taskSchedulerSuccess = (p.ExitCode == 0);
                }
            }
            catch { }

            // 2. Dual redundancy: Register in HKCU\Run
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key != null)
                {
                    key.SetValue(RegistryKeyName, taskRun);
                    registrySuccess = true;
                }
            }
            catch { }

            return taskSchedulerSuccess || registrySuccess;
        }
        else
        {
            // 1. Remove from Task Scheduler
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                psi.ArgumentList.Add("/delete");
                psi.ArgumentList.Add("/tn");
                psi.ArgumentList.Add(TaskName);
                psi.ArgumentList.Add("/f");

                using var p = Process.Start(psi);
                if (p != null)
                {
                    p.WaitForExit(3000);
                    taskSchedulerSuccess = (p.ExitCode == 0);
                }
            }
            catch { }

            // 2. Remove from HKCU\Run
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key != null)
                {
                    key.DeleteValue(RegistryKeyName, false);
                    key.DeleteValue(LegacyRegistryKeyName, false);
                    registrySuccess = true;
                }
            }
            catch { }

            return true;
        }
    }
}
