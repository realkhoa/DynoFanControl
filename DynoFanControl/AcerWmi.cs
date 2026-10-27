using System.Management;

namespace DynoFanControl;

public static class AcerWmi
{
    private const byte REG_CPU_MANUAL   = 0x22;
    private const byte REG_GPU_MANUAL   = 0x21;
    private const byte REG_CPU_SPEED_WR = 0x37;
    private const byte REG_GPU_SPEED_WR = 0x3A;

    private const byte CPU_MANUAL_ON    = 0x0C;
    private const byte CPU_MANUAL_OFF   = 0x04;
    private const byte GPU_MANUAL_ON    = 0x30;
    private const byte GPU_MANUAL_OFF   = 0x10;

    private static ManagementScope Connect()
    {
        var s = new ManagementScope(@"\\.\root\wmi");
        s.Connect();
        return s;
    }

    private static ManagementObject GetInstance(ManagementScope scope)
    {
        using var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM AcerGamingFunction"));
        foreach (var o in s.Get())
            return (ManagementObject)o;
        throw new InvalidOperationException("No AcerGamingFunction instance");
    }

    private static ulong CallGet(string method, uint input)
    {
        var scope = Connect();
        using var inst = GetInstance(scope);
        var p = inst.GetMethodParameters(method);
        p["gmInput"] = input;
        using var r = (ManagementBaseObject)inst.InvokeMethod(method, p, null);
        return (ulong)r["gmOutput"];
    }

    private static uint CallSet(string method, ulong input)
    {
        var scope = Connect();
        using var inst = GetInstance(scope);
        var p = inst.GetMethodParameters(method);
        p["gmInput"] = input;
        using var r = (ManagementBaseObject)inst.InvokeMethod(method, p, null);
        return (uint)r["gmOutput"];
    }

    private static FanInfo GetWmiFanInfo(int fanIndex)
    {
        var raw = CallGet("GetGamingFanSpeed", (uint)fanIndex);
        var upper32 = (uint)(raw >> 32);
        var isAuto = (raw & 1) == 1;
        return new FanInfo { Index = fanIndex, Speed = upper32, IsAuto = isAuto, Raw = raw };
    }

    /// <summary>Set fan speed via EC only (WMI messes up the other fan).
    /// Returns true if EC write succeeded.</summary>
    public static bool SetFanSpeed(int fanIndex, uint speedPercent)
    {
        if (fanIndex == 0)
        {
            if (speedPercent == 0)
                return EcProbe.TryWriteRegister(REG_CPU_MANUAL, CPU_MANUAL_OFF);
            else
                return EcProbe.TryWriteRegister(REG_CPU_MANUAL, CPU_MANUAL_ON) &&
                       EcProbe.TryWriteRegister(REG_CPU_SPEED_WR, (byte)speedPercent);
        }
        else
        {
            if (speedPercent == 0)
                return EcProbe.TryWriteRegister(REG_GPU_MANUAL, GPU_MANUAL_OFF);
            else
                return EcProbe.TryWriteRegister(REG_GPU_MANUAL, GPU_MANUAL_ON) &&
                       EcProbe.TryWriteRegister(REG_GPU_SPEED_WR, (byte)speedPercent);
        }
    }

    /// <summary>Get fan info from WMI. Does NOT read EC RPM registers (values are RPM, not %).</summary>
    public static FanInfo GetFanInfo(int fanIndex)
    {
        return GetWmiFanInfo(fanIndex);
    }

    public static TempReading[] GetLabelledTemps()
    {
        var list = new List<TempReading>();
        try
        {
            var scope = Connect();
            var q = new ObjectQuery("SELECT * FROM MSAcpi_ThermalZoneTemperature");
            using var s = new ManagementObjectSearcher(scope, q);
            int i = 0;
            foreach (var o in s.Get())
            {
                var raw = o["CurrentTemperature"];
                var name = o["InstanceName"]?.ToString() ?? "";
                if (raw != null)
                {
                    var decikelvin = Convert.ToUInt32(raw);
                    var celsius = (decikelvin / 10.0) - 273.15;
                    var label = name.Contains("TZ01") ? "CPU" : name.Contains("TZ02") ? "GPU" : $"Zone {i + 1}";
                    list.Add(new TempReading { Label = label, Value = Math.Round(celsius, 1) });
                }
                i++;
            }
        }
        catch { }
        return list.ToArray();
    }
}

public record FanInfo { public int Index; public uint Speed; public bool IsAuto; public ulong Raw; }
public record TempReading { public string Label { get; init; } = ""; public double Value { get; init; } }
