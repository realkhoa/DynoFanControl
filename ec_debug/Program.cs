using System.Diagnostics;

// Write test
static string Sys(string f, string a)
{
    using var p = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = f, Arguments = a,
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        }
    };
    p.Start();
    var o = p.StandardOutput.ReadToEnd();
    var e = p.StandardError.ReadToEnd();
    p.WaitForExit(5000);
    return $"exit={p.ExitCode} out=[{o.Trim()}] err=[{e.Trim()}]";
}

var exe = @"C:\Program Files (x86)\NoteBook FanControl\ec-probe.exe";

var sb = new System.Text.StringBuilder();
sb.AppendLine(Sys(exe, "read 0x10"));
sb.AppendLine(Sys(exe, "read 0x13"));
sb.AppendLine(Sys(exe, "read 0x15"));
sb.AppendLine(Sys(exe, "read 0x21"));
sb.AppendLine(Sys(exe, "read 0x22"));

// Write test
sb.AppendLine("--- WRITE ---");
sb.AppendLine(Sys(exe, "write 0x22 12"));
sb.AppendLine(Sys(exe, "write 0x21 48"));
sb.AppendLine(Sys(exe, "write 0x37 100"));
sb.AppendLine(Sys(exe, "write 0x3A 100"));

System.Threading.Thread.Sleep(500);

sb.AppendLine("--- AFTER WRITE ---");
sb.AppendLine(Sys(exe, "read 0x22"));
sb.AppendLine(Sys(exe, "read 0x21"));
sb.AppendLine(Sys(exe, "read 0x37"));
sb.AppendLine(Sys(exe, "read 0x3A"));
sb.AppendLine(Sys(exe, "read 0x13"));
sb.AppendLine(Sys(exe, "read 0x15"));

// Reset
sb.AppendLine("--- RESET ---");
sb.AppendLine(Sys(exe, "write 0x22 4"));
sb.AppendLine(Sys(exe, "write 0x21 16"));
sb.AppendLine(Sys(exe, "write 0x37 0"));
sb.AppendLine(Sys(exe, "write 0x3A 0"));

File.WriteAllText(@"D:\sourcecode\fanctl\ec_debug.txt", sb.ToString());
Console.WriteLine(sb.ToString());
Console.ReadLine();
