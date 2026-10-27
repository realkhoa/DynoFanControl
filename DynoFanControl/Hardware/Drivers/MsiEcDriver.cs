namespace DynoFanControl.Hardware.Drivers;

/// <summary>
/// MSI Gaming Laptop Driver (Dragon Center / MSI Center architecture).
/// Supports MSI Katana, Raider, Stealth, Thin, Cyborg, Pulse, Vector, and Titan.
/// </summary>
public sealed class MsiEcDriver : IFanControlDriver
{
    private const byte REG_FAN_MODE     = 0xF4; // 0x0C=Auto, 0x4C=Advanced, 0x8C=CoolerBoost
    private const byte REG_COOLER_BOOST = 0x98; // Bit 7: Cooler Boost toggle
    private const byte REG_CPU_RPM_HI   = 0x71;
    private const byte REG_CPU_RPM_LO   = 0x72;
    private const byte REG_GPU_RPM_HI   = 0x89;
    private const byte REG_GPU_RPM_LO   = 0x8A;
    private const byte REG_CPU_DUTY     = 0xD8; // CPU Current Target Duty %
    private const byte REG_GPU_DUTY     = 0xE8; // GPU Current Target Duty %

    private readonly DirectEcDriver _directEc = new();
    private bool _initialized;
    private bool _available;

    public string DriverName => "MSI Dragon Center EC Driver";
    public string PlatformDescription => "MSI Gaming Laptop EC Architecture (Cooler Boost & Advanced Curves)";
    public HardwarePlatformType PlatformType => HardwarePlatformType.LaptopMsi;
    public int PriorityScore => 90;

    public bool IsSupported
    {
        get
        {
            var p = PlatformDetector.GetProfile();
            return p.PlatformType == HardwarePlatformType.LaptopMsi ||
                   p.Manufacturer.Contains("MSI", StringComparison.OrdinalIgnoreCase) ||
                   p.Manufacturer.Contains("Micro-Star", StringComparison.OrdinalIgnoreCase);
        }
    }

    public bool Initialize()
    {
        if (_initialized) return _available;
        _initialized = true;

        if (IsSupported)
        {
            EcProbeFallback.EnsureDriverService();
            _available = _directEc.Initialize();
        }
        return _available || EcProbeFallback.IsAvailable;
    }

    private bool WriteRegister(byte reg, byte val)
    {
        if (_available)
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

    private byte? ReadRegister(byte reg)
    {
        if (_available)
        {
            try
            {
                return _directEc.ReadRegister(reg);
            }
            catch { }
        }
        return EcProbeFallback.TryReadRegister(reg);
    }

    public bool SetCpuFan(bool isAuto, int speedPercent)
    {
        if (!_available && !EcProbeFallback.IsAvailable) return false;
        if (isAuto)
        {
            return WriteRegister(REG_FAN_MODE, 0x0C); // Auto Mode
        }
        else
        {
            var clamped = (byte)Math.Clamp(speedPercent, 0, 100);
            WriteRegister(REG_FAN_MODE, 0x4C); // Advanced Manual Mode
            return WriteRegister(REG_CPU_DUTY, clamped);
        }
    }

    public bool SetGpuFan(bool isAuto, int speedPercent)
    {
        if (!_available && !EcProbeFallback.IsAvailable) return false;
        if (isAuto)
        {
            return WriteRegister(REG_FAN_MODE, 0x0C);
        }
        else
        {
            var clamped = (byte)Math.Clamp(speedPercent, 0, 100);
            WriteRegister(REG_FAN_MODE, 0x4C);
            return WriteRegister(REG_GPU_DUTY, clamped);
        }
    }

    public bool SetCoolBoost(bool enabled)
    {
        if (!_available && !EcProbeFallback.IsAvailable) return false;
        // MSI Cooler Boost
        return WriteRegister(REG_FAN_MODE, enabled ? (byte)0x8C : (byte)0x0C);
    }

    public FanTachometerReading ReadTachometers()
    {
        var reading = new FanTachometerReading();
        if (!_available && !EcProbeFallback.IsAvailable) return reading;

        try
        {
            var cpuHi = ReadRegister(REG_CPU_RPM_HI);
            var cpuLo = ReadRegister(REG_CPU_RPM_LO);
            if (cpuHi.HasValue && cpuLo.HasValue)
            {
                var raw = (cpuHi.Value << 8) | cpuLo.Value;
                if (raw is > 400 and < 9000) reading.CpuFanRpm = (uint)raw;
            }

            var gpuHi = ReadRegister(REG_GPU_RPM_HI);
            var gpuLo = ReadRegister(REG_GPU_RPM_LO);
            if (gpuHi.HasValue && gpuLo.HasValue)
            {
                var raw = (gpuHi.Value << 8) | gpuLo.Value;
                if (raw is > 400 and < 9000) reading.GpuFanRpm = (uint)raw;
            }
        }
        catch { }

        return reading;
    }

    public void EmergencyReset()
    {
        WriteRegister(REG_FAN_MODE, 0x0C); // Reset to Auto
    }

    public void Dispose()
    {
        _directEc.Dispose();
        _available = false;
    }
}
