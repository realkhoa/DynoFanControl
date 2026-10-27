using System.Diagnostics;
using System.IO;

namespace FanCtl.Services;

/// <summary>
/// Manages running FanCtl on Windows startup.
/// Uses Windows Task Scheduler with Highest Privileges to bypass UAC prompts on login.
/// </summary>
public static class StartupManager
{
    private const string TaskName = "FanCtl_NitroAutoStart";

    public static bool IsStartupEnabled()
    {
        try
        {
            var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/query /tn \"{TaskName}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            p.Start();
            p.WaitForExit(2000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool SetStartup(bool enable, bool startMinimized = true)
    {
        try
        {
            if (enable)
            {
                var exePath = Environment.ProcessPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FanCtl.exe");
                var args = startMinimized ? "--minimized" : "";
                var taskRun = $"\\\"{exePath}\\\" {args}".Trim();

                var p = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = $"/create /tn \"{TaskName}\" /tr \"{taskRun}\" /sc onlogon /rl highest /f",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                p.Start();
                p.WaitForExit(3000);
                return p.ExitCode == 0;
            }
            else
            {
                var p = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = $"/delete /tn \"{TaskName}\" /f",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                p.Start();
                p.WaitForExit(3000);
                return p.ExitCode == 0;
            }
        }
        catch
        {
            return false;
        }
    }
}
