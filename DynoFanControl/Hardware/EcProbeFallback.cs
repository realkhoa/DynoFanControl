using System.Diagnostics;
using System.IO;

namespace DynoFanControl.Hardware;

/// <summary>
/// Fallback backend that controls EC registers via ec-probe.exe process execution.
/// Used if direct driver handle cannot be opened.
/// </summary>
public static class EcProbeFallback
{
    private static string? _cachedExePath;
    private static readonly object _lock = new();

    public static bool IsAvailable => GetExePath() != null;

    public static string? GetExePath()
    {
        if (_cachedExePath != null && File.Exists(_cachedExePath))
            return _cachedExePath;

        // 1. Check application directory
        var local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ec-probe.exe");
        if (File.Exists(local))
        {
            _cachedExePath = local;
            return local;
        }

        // 2. Check standard NoteBook FanControl installation
        var nbfcPath = @"C:\Program Files (x86)\NoteBook FanControl\ec-probe.exe";
        if (File.Exists(nbfcPath))
        {
            _cachedExePath = nbfcPath;
            return nbfcPath;
        }

        return null;
    }

    public static bool EnsureDriverService()
    {
        try
        {
            var (queryOut, _) = RunCommand("sc.exe", "query WinRing0_1_2_0");
            if (queryOut.Contains("RUNNING"))
                return true;

            // Try starting it
            RunCommand("sc.exe", "start WinRing0_1_2_0");

            // Re-check
            var (recheck, _) = RunCommand("sc.exe", "query WinRing0_1_2_0");
            return recheck.Contains("RUNNING");
        }
        catch
        {
            return false;
        }
    }

    public static byte? TryReadRegister(byte reg)
    {
        lock (_lock)
        {
            try
            {
                var exe = GetExePath();
                if (exe == null) return null;

                var (stdout, _) = RunCommand(exe, $"read 0x{reg:X2}");
                var line = stdout.Trim();

                // Formats: "0 (0x00)" or "read 0x37: 50" or "50"
                var colonIndex = line.LastIndexOf(':');
                var num = colonIndex >= 0 ? line.Substring(colonIndex + 1).Trim() : line;

                var parenIndex = num.IndexOf('(');
                if (parenIndex >= 0) num = num.Substring(0, parenIndex).Trim();

                if (num.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    num = num.Substring(2);

                if (byte.TryParse(num, out var decVal)) return decVal;
                if (byte.TryParse(num, System.Globalization.NumberStyles.HexNumber, null, out var hexVal)) return hexVal;

                return null;
            }
            catch
            {
                return null;
            }
        }
    }

    public static bool TryWriteRegister(byte reg, byte value)
    {
        lock (_lock)
        {
            try
            {
                var exe = GetExePath();
                if (exe == null) return false;

                var (_, err) = RunCommand(exe, $"write 0x{reg:X2} {value}");
                return string.IsNullOrEmpty(err) || !err.Contains("Error");
            }
            catch
            {
                return false;
            }
        }
    }

    private static (string stdout, string stderr) RunCommand(string file, string arguments)
    {
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = file,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(file) ?? AppDomain.CurrentDomain.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        p.Start();
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit(3000);
        return (stdout.Trim(), stderr.Trim());
    }
}
