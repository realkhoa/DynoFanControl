namespace DynoFanControl.Hardware.Drivers;

/// <summary>
/// Driver for Acer Nitro & Predator gaming laptops via Direct Embedded Controller (EC) I/O.
/// Works on Acer Nitro 5 (AN515-41..58), Nitro 16/17, Predator Helios 300, Triton 300/500.
/// </summary>
public sealed class AcerEcDriver : IFanControlDriver
{
    public const byte REG_GPU_MANUAL   = 0x21;
    public const byte REG_CPU_MANUAL   = 0x22;
    public const byte REG_CPU_SPEED_RD = 0x13;
    public const byte REG_GPU_SPEED_RD = 0x15;
    public const byte REG_CPU_SPEED_WR = 0x37;
    public const byte REG_GPU_SPEED_WR = 0x3A;
    public const byte REG_COOLBOOST    = 0x10;

    public const byte CPU_MANUAL_ON    = 0x0C;
    public const byte CPU_MANUAL_OFF   = 0x04;
    public const byte GPU_MANUAL_ON    = 0x30;
    public const byte GPU_MANUAL_OFF   = 0x10;
    public const byte COOLBOOST_OFF    = 0x00;
    public const byte COOLBOOST_ON     = 0x01;

    private readonly DirectEcDriver _directEc = new();
    private bool _initialized;
    private bool _directReady;

    public string DriverName => _directReady ? "Acer Nitro Direct EC (WinRing0)" : "Acer Nitro EC (ec-probe)";
    public string PlatformDescription => "Acer Nitro & Predator Gaming Laptops (Embedded Controller)";
    public HardwarePlatformType PlatformType => HardwarePlatformType.LaptopAcer;
    public int PriorityScore => 100;

    public bool IsSupported
    {
        get
        {
            var p = PlatformDetector.GetProfile();
            return p.PlatformType == HardwarePlatformType.LaptopAcer ||
                   p.Manufacturer.Contains("Acer", StringComparison.OrdinalIgnoreCase);
        }
    }

    public bool Initialize()
    {
        if (_initialized) return _directReady || EcProbeFallback.IsAvailable;
        _initialized = true;

        EcProbeFallback.EnsureDriverService();
        _directReady = _directEc.Initialize();
        return _directReady || EcProbeFallback.IsAvailable;
    }

    public bool SetCpuFan(bool isAuto, int speedPercent)
    {
        if (isAuto || speedPercent <= 0)
        {
            WriteRegister(REG_CPU_SPEED_WR, 0);
            return WriteRegister(REG_CPU_MANUAL, CPU_MANUAL_OFF);
        }
        else
        {
            var clamped = (byte)Math.Clamp(speedPercent, 1, 100);
            var ok1 = WriteRegister(REG_CPU_MANUAL, CPU_MANUAL_ON);
            var ok2 = WriteRegister(REG_CPU_SPEED_WR, clamped);
            return ok1 && ok2;
        }
    }

    public bool SetGpuFan(bool isAuto, int speedPercent)
    {
        if (isAuto || speedPercent <= 0)
        {
            WriteRegister(REG_GPU_SPEED_WR, 0);
            return WriteRegister(REG_GPU_MANUAL, GPU_MANUAL_OFF);
        }
        else
        {
            var clamped = (byte)Math.Clamp(speedPercent, 1, 100);
            var ok1 = WriteRegister(REG_GPU_MANUAL, GPU_MANUAL_ON);
            var ok2 = WriteRegister(REG_GPU_SPEED_WR, clamped);
            return ok1 && ok2;
        }
    }

    public bool SetCoolBoost(bool enabled)
    {
        return WriteRegister(REG_COOLBOOST, enabled ? COOLBOOST_ON : COOLBOOST_OFF);
    }

    public FanTachometerReading ReadTachometers()
    {
        var reading = new FanTachometerReading();

        var cpuRaw = ReadRegister(REG_CPU_SPEED_RD);
        if (cpuRaw.HasValue && cpuRaw.Value > 0)
            reading.CpuFanRpm = (uint)cpuRaw.Value * 60;

        var gpuRaw = ReadRegister(REG_GPU_SPEED_RD);
        if (gpuRaw.HasValue && gpuRaw.Value > 0)
            reading.GpuFanRpm = (uint)gpuRaw.Value * 60;

        var cpuManual = ReadRegister(REG_CPU_MANUAL);
        if (cpuManual.HasValue)
            reading.CpuIsAuto = cpuManual.Value == CPU_MANUAL_OFF;

        var gpuManual = ReadRegister(REG_GPU_MANUAL);
        if (gpuManual.HasValue)
            reading.GpuIsAuto = gpuManual.Value == GPU_MANUAL_OFF;

        return reading;
    }

    public void EmergencyReset()
    {
        try
        {
            SetCpuFan(true, 0);
            SetGpuFan(true, 0);
            SetCoolBoost(false);
        }
        catch { }
    }

    public bool WriteRegister(byte reg, byte val)
    {
        if (_directReady)
        {
            try
            {
                _directEc.WriteRegister(reg, val);
                return true;
            }
            catch { }
        }
        return EcProbeFallback.TryWriteRegister(reg, val);
    }

    public byte? ReadRegister(byte reg)
    {
        if (_directReady)
        {
            try
            {
                return _directEc.ReadRegister(reg);
            }
            catch { }
        }
        return EcProbeFallback.TryReadRegister(reg);
    }

    public void Dispose()
    {
        _directEc.Dispose();
    }
}
