using System.Diagnostics;
using System.IO;

namespace DynoFanControl;

public static class EcProbe
{
    private static string? _exePath;

    public static bool IsAvailable
    {
        get
        {
            if (_exePath != null) return true;
            var p = FindExe();
            if (p != null) { _exePath = p; return true; }
            return false;
        }
    }

    private static string? FindExe()
    {
        // Must use NBFC installation — ec-probe needs Configs\ + Plugins\ in CWD
        // Local copy won't work without those folders
        if (File.Exists(@"C:\Program Files (x86)\NoteBook FanControl\ec-probe.exe"))
            return @"C:\Program Files (x86)\NoteBook FanControl\ec-probe.exe";
        return null;
    }

    public static bool EnsureDriver()
    {
        // Driver should already be installed from NBFC or manually
        // If not running, try to start it
        if (!IsDriverRunning())
            ExecHidden("sc", "start WinRing0_1_2_0");
        return IsDriverRunning();
    }

    private static bool IsDriverRunning()
    {
        try
        {
            var (o, _) = Run("sc", "query WinRing0_1_2_0");
            return o.Contains("RUNNING");
        }
        catch { return false; }
    }

    private static void ExecHidden(string f, string a)
    {
        var (_, _) = Run(f, a);
    }

    /// <summary>Try to read a register. Returns null on any failure (no throw).</summary>
    public static byte? TryReadRegister(byte reg)
    {
        try
        {
            var exe = _exePath ?? FindExe();
            if (exe == null) return null;
            var (stdout, _) = Run(exe, "read", $"0x{reg:X2}");
            var line = stdout.Trim();
            // ec-probe outputs like: "0 (0x00)" or "50 (0x32)" or "read 0x37: 0"
            var ci = line.LastIndexOf(':');
            var num = ci >= 0 ? line.Substring(ci + 1).Trim() : line;
            // Strip parenthetical hex: "0 (0x00)" -> "0"
            var paren = num.IndexOf('(');
            if (paren >= 0) num = num.Substring(0, paren).Trim();
            if (num.StartsWith("0x")) num = num.Substring(2);
            if (byte.TryParse(num, out var d)) return d;
            if (byte.TryParse(num, System.Globalization.NumberStyles.HexNumber, null, out var h)) return h;
            return null;
        }
        catch { return null; }
    }

    /// <summary>Write a register. Returns true on success.</summary>
    public static bool TryWriteRegister(byte reg, byte value)
    {
        try
        {
            var exe = _exePath ?? FindExe();
            if (exe == null) return false;
            var (_, err) = Run(exe, "write", $"0x{reg:X2}", $"{value}");
            return string.IsNullOrEmpty(err);
        }
        catch { return false; }
    }

    private static (string stdout, string stderr) Run(string file, params string[] args)
    {
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = file,
                Arguments = string.Join(" ", args),
                WorkingDirectory = Path.GetDirectoryName(file)!, // critical: ec-probe needs Plugins\ in CWD
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        };
        p.Start();
        var o = p.StandardOutput.ReadToEnd();
        var e = p.StandardError.ReadToEnd();
        p.WaitForExit(5000);
        if (p.ExitCode != 0) throw new InvalidOperationException($"exit={p.ExitCode} err=[{e.Trim()}]");
        return (o.Trim(), e.Trim());
    }
}
