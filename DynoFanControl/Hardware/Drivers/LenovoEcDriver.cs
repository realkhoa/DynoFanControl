using System.Management;
using DynoFanControl.Hardware;

namespace DynoFanControl.Hardware.Drivers;

/// <summary>
/// Lenovo Legion, LOQ, IdeaPad Gaming, and ThinkPad fan driver.
/// Operates through a dual-engine architecture: direct ACPI Embedded Controller (EC) I/O via WinRing0
/// and Lenovo GameZone/Thermal WMI method dispatch.
/// </summary>
public sealed class LenovoEcDriver : IFanControlDriver
{
    // Lenovo EC Fan Control Registers
    private const byte REG_FAN_TURBO      = 0xBD; // 0x00=Auto/Balanced, 0x01=Quiet, 0x02=Performance, 0x40=Max Fan
    private const byte REG_POWER_MODE     = 0x01; // Fn+Q mode: 0x01=Quiet, 0x02=Balanced, 0x03=Performance
    private const byte REG_POWER_MODE_ALT = 0x8A; // Alternate Power Mode register on newer firmware
    private const byte REG_FAN_LOCK       = 0xF4; // Fan speed lock register

    // Lenovo EC Tachometer Register Pairs (High Byte, Low Byte)
    private static readonly (byte Hi, byte Lo)[] CpuFanRpmPairs =
    [
        (0xFE, 0xFF), // Legion Gen 5 / Gen 6 / Gen 7 / Gen 8
        (0x84, 0x85), // Legion Gen 6/7 alternate & IdeaPad Gaming
        (0x94, 0x95), // LOQ / IdeaPad series
        (0x13, 0x14)  // Legacy Legion
    ];

    private static readonly (byte Hi, byte Lo)[] GpuFanRpmPairs =
    [
        (0xEC, 0xED), // Legion Gen 5 / Gen 6 / Gen 7 / Gen 8
        (0x86, 0x87), // Legion Gen 6/7 alternate & IdeaPad Gaming
        (0x96, 0x97), // LOQ / IdeaPad series
        (0x15, 0x16)  // Legacy Legion
    ];

    private readonly DirectEcDriver _directEc = new();
    private ManagementScope? _scope;
    private bool _initialized;
    private bool _directReady;
    private bool _wmiReady;
    private bool _available;
    private int _confirmedPairIndex = -1;

    public string DriverName => _directReady
        ? "Lenovo Legion Direct EC (WinRing0)"
        : (_wmiReady ? "Lenovo Legion WMI Driver" : "Lenovo Legion Thermal Driver");

    public string PlatformDescription => "Lenovo Legion, LOQ & IdeaPad Gaming Dual-Engine Thermal Architecture";
    public HardwarePlatformType PlatformType => HardwarePlatformType.LaptopLenovo;
    public int PriorityScore => 100;

    public bool IsSupported
    {
        get
        {
            var p = PlatformDetector.GetProfile();
            var mfg = (p.Manufacturer + " " + p.MotherboardManufacturer).ToLowerInvariant();
            var model = (p.Model + " " + p.MotherboardModel).ToLowerInvariant();

            return p.PlatformType == HardwarePlatformType.LaptopLenovo ||
                   mfg.Contains("lenovo") ||
                   model.Contains("legion") ||
                   model.Contains("loq") ||
                   model.Contains("ideapad") ||
                   model.Contains("thinkpad") ||
                   model.Contains("thinkbook");
        }
    }

    public bool Initialize()
    {
        if (_initialized) return _available;
        _initialized = true;

        if (!IsSupported) return false;

        // 1. Initialize Direct Embedded Controller (WinRing0 IOCTL)
        try
        {
            EcProbeFallback.EnsureDriverService();
            _directReady = _directEc.Initialize();
        }
        catch
        {
            _directReady = false;
        }

        // 2. Initialize Lenovo ACPI WMI Interface
        try
        {
            _scope = new ManagementScope(@"\\.\root\wmi");
            _scope.Connect();
            _wmiReady = ProbeWmi();
        }
        catch
        {
            _wmiReady = false;
        }

        _available = _directReady || _wmiReady || EcProbeFallback.IsAvailable;
        return _available;
    }

    private bool ProbeWmi()
    {
        if (_scope == null) return false;
        string[] candidateClasses =
        [
            "LENOVO_GAMEZONE_DATA",
            "LENOVO_THERMAL_METHOD",
            "LENOVO_OTHER_METHOD",
            "LENOVO_BIOM_METHOD",
            "Lenovo_ThermalInformation"
        ];

        foreach (var cls in candidateClasses)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(_scope, new ObjectQuery($"SELECT * FROM {cls}"));
                foreach (var _ in searcher.Get())
                {
                    return true;
                }
            }
            catch { }
        }

        return false;
    }

    private bool WriteRegister(byte reg, byte val)
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

    private byte? ReadRegister(byte reg)
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

    private bool SetSmartFanModeWmi(uint mode)
    {
        if (!_wmiReady || _scope == null) return false;

        bool success = false;

        // Try LENOVO_GAMEZONE_DATA -> SetSmartFanMode(uint32 Data)
        try
        {
            using var searcher = new ManagementObjectSearcher(_scope, new ObjectQuery("SELECT * FROM LENOVO_GAMEZONE_DATA"));
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    var inParams = obj.GetMethodParameters("SetSmartFanMode");
                    inParams["Data"] = mode;
                    obj.InvokeMethod("SetSmartFanMode", inParams, null);
                    success = true;
                }
            }
        }
        catch { }

        // Try LENOVO_THERMAL_METHOD -> SetThermalMode(uint32 Mode)
        try
        {
            using var searcher = new ManagementObjectSearcher(_scope, new ObjectQuery("SELECT * FROM LENOVO_THERMAL_METHOD"));
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    var inParams = obj.GetMethodParameters("SetThermalMode");
                    inParams["Mode"] = mode;
                    obj.InvokeMethod("SetThermalMode", inParams, null);
                    success = true;
                }
            }
        }
        catch { }

        // Try LENOVO_OTHER_METHOD -> SetThermalMode(uint32 Mode)
        try
        {
            using var searcher = new ManagementObjectSearcher(_scope, new ObjectQuery("SELECT * FROM LENOVO_OTHER_METHOD"));
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    var inParams = obj.GetMethodParameters("SetThermalMode");
                    inParams["Mode"] = mode;
                    obj.InvokeMethod("SetThermalMode", inParams, null);
                    success = true;
                }
            }
        }
        catch { }

        // Try LENOVO_BIOM_METHOD -> SetThermalMode(uint32 Mode)
        try
        {
            using var searcher = new ManagementObjectSearcher(_scope, new ObjectQuery("SELECT * FROM LENOVO_BIOM_METHOD"));
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    var inParams = obj.GetMethodParameters("SetThermalMode");
                    inParams["Mode"] = mode;
                    obj.InvokeMethod("SetThermalMode", inParams, null);
                    success = true;
                }
            }
        }
        catch { }

        return success;
    }

    public bool SetCpuFan(bool isAuto, int speedPercent)
    {
        return ApplyThermalProfile(isAuto, speedPercent);
    }

    public bool SetGpuFan(bool isAuto, int speedPercent)
    {
        return ApplyThermalProfile(isAuto, speedPercent);
    }

    public bool SetCoolBoost(bool enabled)
    {
        return ApplyThermalProfile(false, enabled ? 100 : 50);
    }

    private bool ApplyThermalProfile(bool isAuto, int speedPercent)
    {
        if (!_available) return false;

        bool result = false;

        if (isAuto || speedPercent <= 0)
        {
            // Auto / Balanced mode
            result |= SetSmartFanModeWmi(2);
            result |= WriteRegister(REG_FAN_TURBO, 0x00);
            result |= WriteRegister(REG_POWER_MODE, 0x02);
            result |= WriteRegister(REG_POWER_MODE_ALT, 0x02);
        }
        else if (speedPercent >= 80)
        {
            // Performance / Turbo Max fan mode
            result |= SetSmartFanModeWmi(3);
            result |= SetSmartFanModeWmi(224); // Custom / Max on Legion Go & Gen 8
            result |= WriteRegister(REG_FAN_TURBO, 0x02);
            result |= WriteRegister(REG_POWER_MODE, 0x03);
            result |= WriteRegister(REG_POWER_MODE_ALT, 0x03);
        }
        else if (speedPercent <= 45)
        {
            // Quiet mode
            result |= SetSmartFanModeWmi(1);
            result |= WriteRegister(REG_FAN_TURBO, 0x01);
            result |= WriteRegister(REG_POWER_MODE, 0x01);
            result |= WriteRegister(REG_POWER_MODE_ALT, 0x01);
        }
        else
        {
            // Balanced mode
            result |= SetSmartFanModeWmi(2);
            result |= WriteRegister(REG_FAN_TURBO, 0x00);
            result |= WriteRegister(REG_POWER_MODE, 0x02);
            result |= WriteRegister(REG_POWER_MODE_ALT, 0x02);
        }

        return result || _available;
    }

    public FanTachometerReading ReadTachometers()
    {
        if (!_available) return new FanTachometerReading();

        // 1. If we have a cached working register pair, read directly
        if (_confirmedPairIndex >= 0 && _confirmedPairIndex < CpuFanRpmPairs.Length)
        {
            var cpuPair = CpuFanRpmPairs[_confirmedPairIndex];
            var gpuPair = GpuFanRpmPairs[_confirmedPairIndex];
            var cpuRpm = ReadRpmFromPair(cpuPair.Hi, cpuPair.Lo);
            var gpuRpm = ReadRpmFromPair(gpuPair.Hi, gpuPair.Lo);

            if (cpuRpm > 0 || gpuRpm > 0)
            {
                return new FanTachometerReading
                {
                    CpuFanRpm = (uint)Math.Max(0, cpuRpm),
                    GpuFanRpm = (uint)Math.Max(0, gpuRpm),
                    CpuIsAuto = true,
                    GpuIsAuto = true
                };
            }
        }

        // 2. Scan through known register pairs to locate active tachometers
        for (int i = 0; i < CpuFanRpmPairs.Length; i++)
        {
            var cpuPair = CpuFanRpmPairs[i];
            var gpuPair = GpuFanRpmPairs[i];
            var cpuRpm = ReadRpmFromPair(cpuPair.Hi, cpuPair.Lo);
            var gpuRpm = ReadRpmFromPair(gpuPair.Hi, gpuPair.Lo);

            if (cpuRpm > 200 || gpuRpm > 200)
            {
                _confirmedPairIndex = i;
                return new FanTachometerReading
                {
                    CpuFanRpm = (uint)Math.Max(0, cpuRpm),
                    GpuFanRpm = (uint)Math.Max(0, gpuRpm),
                    CpuIsAuto = true,
                    GpuIsAuto = true
                };
            }
        }

        // 3. Fallback: Query WMI Lenovo_ThermalInformation or LENOVO_GAMEZONE_DATA
        if (_wmiReady && _scope != null)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(_scope, new ObjectQuery("SELECT * FROM Lenovo_ThermalInformation"));
                foreach (ManagementObject obj in searcher.Get())
                {
                    using (obj)
                    {
                        var cpu = Convert.ToUInt32(obj["CPUFanSpeed"] ?? obj["FanSpeed"] ?? 0);
                        var gpu = Convert.ToUInt32(obj["GPUFanSpeed"] ?? 0);
                        if (cpu > 0 || gpu > 0)
                        {
                            return new FanTachometerReading
                            {
                                CpuFanRpm = cpu,
                                GpuFanRpm = gpu > 0 ? gpu : cpu,
                                CpuIsAuto = true,
                                GpuIsAuto = true
                            };
                        }
                    }
                }
            }
            catch { }
        }

        return new FanTachometerReading();
    }

    private int ReadRpmFromPair(byte hiReg, byte loReg)
    {
        var hi = ReadRegister(hiReg);
        var lo = ReadRegister(loReg);
        if (!hi.HasValue || !lo.HasValue) return 0;

        // Big-Endian check (e.g. 0x0B, 0xB8 = 3000 RPM)
        int beRpm = (hi.Value << 8) | lo.Value;
        if (beRpm >= 200 && beRpm <= 9500)
            return beRpm;

        // Little-Endian check
        int leRpm = (lo.Value << 8) | hi.Value;
        if (leRpm >= 200 && leRpm <= 9500)
            return leRpm;

        // Single byte hundreds check (e.g. 35 -> 3500 RPM)
        if (hi.Value >= 10 && hi.Value <= 85 && lo.Value == 0)
            return hi.Value * 100;

        return 0;
    }

    public void EmergencyReset()
    {
        SetSmartFanModeWmi(2);
        WriteRegister(REG_FAN_TURBO, 0x00);
        WriteRegister(REG_POWER_MODE, 0x02);
        WriteRegister(REG_POWER_MODE_ALT, 0x02);
    }

    public void Dispose()
    {
        _directEc.Dispose();
    }
}
