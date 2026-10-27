namespace DynoFanControl;

/// <summary>
/// Direct EC register access for Acer Nitro AN515-45 via WinRing0 I/O ports.
/// EC registers from nbfc-linux config: RegisterWriteConfigurations + FanConfigurations
/// </summary>
public static class AcerEC
{
    // EC I/O ports (standard ACPI EC)
    private const ushort EC_DATA = 0x62;
    private const ushort EC_SC   = 0x66; // Status/Command port

    // EC commands
    private const byte EC_CMD_READ  = 0x80;
    private const byte EC_CMD_WRITE = 0x81;

    // EC register map for AN515-45 (from nbfc-linux config)
    // Register numbers are decimal (from JSON), here in hex for clarity
    public const byte REG_GPU_MANUAL    = 0x21; // GPU manual mode control
    public const byte REG_CPU_MANUAL    = 0x22; // CPU manual mode control
    public const byte REG_CPU_SPEED_RD  = 0x13; // CPU fan speed read (0-6122 RPM)
    public const byte REG_GPU_SPEED_RD  = 0x15; // GPU fan speed read (0-6122 RPM)
    public const byte REG_CPU_SPEED_WR  = 0x37; // CPU fan speed write (0-100%)
    public const byte REG_GPU_SPEED_WR  = 0x3A; // GPU fan speed write (0-100%)
    public const byte REG_COOLBOOST     = 0x10; // CoolBoost on/off

    // Values
    public const byte CPU_MANUAL_ON     = 0x0C; // Enable CPU manual control
    public const byte CPU_MANUAL_OFF    = 0x04; // Disable CPU manual control
    public const byte GPU_MANUAL_ON     = 0x30; // Enable GPU manual control
    public const byte GPU_MANUAL_OFF    = 0x10; // Disable GPU manual control
    public const byte COOLBOOST_OFF     = 0x00;
    public const byte COOLBOOST_ON      = 0x01;

    private static bool _initialized;

    public static bool IsAvailable => _initialized;

    public static bool Initialize()
    {
        if (_initialized) return true;

        try
        {
            var status = WinRing0.InitializeOls();
            if (status == 0)
            {
                _initialized = true;
                return true;
            }
        }
        catch
        {
            // DLL not found or driver not loaded
        }
        return false;
    }

    public static void Shutdown()
    {
        if (_initialized)
        {
            WinRing0.DeinitializeOls();
            _initialized = false;
        }
    }

    // --- EC protocol helpers ---

    private static void WaitEC()
    {
        // Wait for EC to be ready (IBF=0)
        var timeout = 0;
        while (timeout < 1000)
        {
            if ((WinRing0.ReadIoPortByte(EC_SC) & 0x02) == 0)
                return;
            Thread.SpinWait(10);
            timeout++;
        }
        throw new TimeoutException("EC busy timeout");
    }

    private static void WaitOBF()
    {
        // Wait for output buffer full (data available)
        var timeout = 0;
        while (timeout < 1000)
        {
            if ((WinRing0.ReadIoPortByte(EC_SC) & 0x01) != 0)
                return;
            Thread.SpinWait(10);
            timeout++;
        }
        throw new TimeoutException("EC OBF timeout");
    }

    public static byte ReadRegister(byte reg)
    {
        WaitEC();
        WinRing0.WriteIoPortByte(EC_SC, EC_CMD_READ);
        WaitEC();
        WinRing0.WriteIoPortByte(EC_DATA, reg);
        WaitOBF();
        return WinRing0.ReadIoPortByte(EC_DATA);
    }

    public static void WriteRegister(byte reg, byte value)
    {
        WaitEC();
        WinRing0.WriteIoPortByte(EC_SC, EC_CMD_WRITE);
        WaitEC();
        WinRing0.WriteIoPortByte(EC_DATA, reg);
        WaitEC();
        WinRing0.WriteIoPortByte(EC_DATA, value);
        // Wait for EC to accept the write
        Thread.Sleep(1);
    }

    // --- High-level fan control ---

    public static void EnableManualControl(int fanIndex)
    {
        if (fanIndex == 0)
            WriteRegister(REG_CPU_MANUAL, CPU_MANUAL_ON);
        else
            WriteRegister(REG_GPU_MANUAL, GPU_MANUAL_ON);
    }

    public static void DisableManualControl(int fanIndex)
    {
        if (fanIndex == 0)
            WriteRegister(REG_CPU_MANUAL, CPU_MANUAL_OFF);
        else
            WriteRegister(REG_GPU_MANUAL, GPU_MANUAL_OFF);
    }

    public static void SetFanSpeed(int fanIndex, byte speedPercent)
    {
        if (speedPercent > 100) speedPercent = 100;

        // First enable manual control
        EnableManualControl(fanIndex);

        var reg = fanIndex == 0 ? REG_CPU_SPEED_WR : REG_GPU_SPEED_WR;
        WriteRegister(reg, speedPercent);
    }

    public static byte GetFanSpeed(int fanIndex)
    {
        var reg = fanIndex == 0 ? REG_CPU_SPEED_RD : REG_GPU_SPEED_RD;
        return ReadRegister(reg);
    }

    public static void SetAutoMode(int fanIndex)
    {
        SetFanSpeed(fanIndex, 0);
        DisableManualControl(fanIndex);
    }

    public static void SetCoolBoost(bool on)
    {
        WriteRegister(REG_COOLBOOST, on ? COOLBOOST_ON : COOLBOOST_OFF);
    }
}
