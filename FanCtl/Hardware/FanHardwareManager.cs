using System.Management;

namespace FanCtl.Hardware;

public class HardwareReading
{
    public double CpuTemp { get; set; }
    public double GpuTemp { get; set; }
    public double PeakTemp => Math.Max(CpuTemp, GpuTemp);
    public uint CpuRpm { get; set; }
    public uint GpuRpm { get; set; }
    public bool CpuIsAuto { get; set; }
    public bool GpuIsAuto { get; set; }
}

public sealed class FanHardwareManager : IDisposable
{
    // Acer Nitro 5 (AN515-45) EC Register Map
    public const byte REG_GPU_MANUAL    = 0x21; // GPU manual mode control
    public const byte REG_CPU_MANUAL    = 0x22; // CPU manual mode control
    public const byte REG_CPU_SPEED_RD  = 0x13; // CPU fan speed read
    public const byte REG_GPU_SPEED_RD  = 0x15; // GPU fan speed read
    public const byte REG_CPU_SPEED_WR  = 0x37; // CPU fan speed write (0-100%)
    public const byte REG_GPU_SPEED_WR  = 0x3A; // GPU fan speed write (0-100%)
    public const byte REG_COOLBOOST     = 0x10; // CoolBoost on/off

    // Register Values
    public const byte CPU_MANUAL_ON     = 0x0C; // Enable CPU manual control
    public const byte CPU_MANUAL_OFF    = 0x04; // Disable CPU manual control (BIOS Auto)
    public const byte GPU_MANUAL_ON     = 0x30; // Enable GPU manual control
    public const byte GPU_MANUAL_OFF    = 0x10; // Disable GPU manual control (BIOS Auto)
    public const byte COOLBOOST_OFF     = 0x00;
    public const byte COOLBOOST_ON      = 0x01;

    private readonly DirectEcDriver _directEc = new();
    private bool _directEcReady;
    private bool _disposed;

    public static FanHardwareManager Instance { get; } = new();

    public string BackendStatus
    {
        get
        {
            if (_directEcReady) return "Direct Kernel Driver (WinRing0)";
            if (EcProbeFallback.IsAvailable) return "ec-probe.exe Fallback";
            return "WMI Telemetry Only";
        }
    }

    public bool IsControlAvailable => _directEcReady || EcProbeFallback.IsAvailable;

    public void Initialize()
    {
        // Try starting kernel driver service if needed
        EcProbeFallback.EnsureDriverService();

        // Attempt direct EC driver initialization
        _directEcReady = _directEc.Initialize();
    }

    public bool WriteRegister(byte reg, byte val)
    {
        if (_directEcReady)
        {
            try
            {
                _directEc.WriteRegister(reg, val);
                return true;
            }
            catch
            {
                // Fallback to ec-probe on direct error
            }
        }

        return EcProbeFallback.TryWriteRegister(reg, val);
    }

    public byte? ReadRegister(byte reg)
    {
        if (_directEcReady)
        {
            try
            {
                return _directEc.ReadRegister(reg);
            }
            catch
            {
                // Fallback
            }
        }

        return EcProbeFallback.TryReadRegister(reg);
    }

    public bool SetCpuFan(bool isAuto, int speedPercent)
    {
        if (isAuto || speedPercent <= 0)
        {
            // Restore BIOS control
            WriteRegister(REG_CPU_SPEED_WR, 0);
            return WriteRegister(REG_CPU_MANUAL, CPU_MANUAL_OFF);
        }
        else
        {
            var clamped = Math.Clamp(speedPercent, 1, 100);
            var okManual = WriteRegister(REG_CPU_MANUAL, CPU_MANUAL_ON);
            var okSpeed = WriteRegister(REG_CPU_SPEED_WR, (byte)clamped);
            return okManual && okSpeed;
        }
    }

    public bool SetGpuFan(bool isAuto, int speedPercent)
    {
        if (isAuto || speedPercent <= 0)
        {
            // Restore BIOS control
            WriteRegister(REG_GPU_SPEED_WR, 0);
            return WriteRegister(REG_GPU_MANUAL, GPU_MANUAL_OFF);
        }
        else
        {
            var clamped = Math.Clamp(speedPercent, 1, 100);
            var okManual = WriteRegister(REG_GPU_MANUAL, GPU_MANUAL_ON);
            var okSpeed = WriteRegister(REG_GPU_SPEED_WR, (byte)clamped);
            return okManual && okSpeed;
        }
    }

    public bool SetCoolBoost(bool enabled)
    {
        return WriteRegister(REG_COOLBOOST, enabled ? COOLBOOST_ON : COOLBOOST_OFF);
    }

    public void EmergencyResetToAuto()
    {
        try
        {
            SetCpuFan(true, 0);
            SetGpuFan(true, 0);
            SetCoolBoost(false);
        }
        catch
        {
            // Suppress errors during emergency teardown
        }
    }

    public HardwareReading PollHardware()
    {
        var reading = new HardwareReading();

        // 1. Poll Temperatures from ACPI Thermal Zones
        try
        {
            var scope = new ManagementScope(@"\\.\root\wmi");
            scope.Connect();

            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSAcpi_ThermalZoneTemperature"));
            foreach (var obj in searcher.Get())
            {
                var raw = obj["CurrentTemperature"];
                var name = obj["InstanceName"]?.ToString() ?? "";
                if (raw != null)
                {
                    var deciKelvin = Convert.ToUInt32(raw);
                    var celsius = Math.Round((deciKelvin / 10.0) - 273.15, 1);

                    if (celsius is > 0 and < 125)
                    {
                        if (name.Contains("TZ01", StringComparison.OrdinalIgnoreCase))
                            reading.CpuTemp = celsius;
                        else if (name.Contains("TZ02", StringComparison.OrdinalIgnoreCase))
                            reading.GpuTemp = celsius;
                        else if (reading.CpuTemp == 0)
                            reading.CpuTemp = celsius;
                    }
                }
            }
        }
        catch
        {
            // Ignore WMI errors during poll
        }

        // 2. Poll Fan RPM via AcerGamingFunction WMI
        try
        {
            var scope = new ManagementScope(@"\\.\root\wmi");
            scope.Connect();

            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM AcerGamingFunction"));
            ManagementObject? gamingInst = null;
            foreach (var o in searcher.Get())
            {
                gamingInst = (ManagementObject)o;
                break;
            }

            if (gamingInst != null)
            {
                using (gamingInst)
                {
                    // CPU Fan (Index 0)
                    var inParams0 = gamingInst.GetMethodParameters("GetGamingFanSpeed");
                    inParams0["gmInput"] = 0u;
                    using var out0 = (ManagementBaseObject)gamingInst.InvokeMethod("GetGamingFanSpeed", inParams0, null);
                    var raw0 = (ulong)out0["gmOutput"];
                    reading.CpuRpm = (uint)(raw0 >> 32);
                    reading.CpuIsAuto = (raw0 & 1) == 1;

                    // GPU Fan (Index 1)
                    var inParams1 = gamingInst.GetMethodParameters("GetGamingFanSpeed");
                    inParams1["gmInput"] = 1u;
                    using var out1 = (ManagementBaseObject)gamingInst.InvokeMethod("GetGamingFanSpeed", inParams1, null);
                    var raw1 = (ulong)out1["gmOutput"];
                    reading.GpuRpm = (uint)(raw1 >> 32);
                    reading.GpuIsAuto = (raw1 & 1) == 1;
                }
            }
        }
        catch
        {
            // WMI RPM read error; try reading raw EC speed registers as fallback
            var cpuRaw = ReadRegister(REG_CPU_SPEED_RD);
            if (cpuRaw.HasValue && cpuRaw.Value > 0)
                reading.CpuRpm = (uint)cpuRaw.Value * 60; // Approximate RPM scale if WMI fails

            var gpuRaw = ReadRegister(REG_GPU_SPEED_RD);
            if (gpuRaw.HasValue && gpuRaw.Value > 0)
                reading.GpuRpm = (uint)gpuRaw.Value * 60;
        }

        return reading;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _directEc.Dispose();
            _disposed = true;
        }
    }
}
