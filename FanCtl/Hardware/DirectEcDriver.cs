using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FanCtl.Hardware;

/// <summary>
/// Direct ACPI Embedded Controller (EC) communication via WinRing0 kernel driver IOCTLs.
/// Operates in sub-millisecond time without spawning external processes.
/// </summary>
public sealed class DirectEcDriver : IDisposable
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    // WinRing0 OLS IOCTL codes
    private const uint IOCTL_OLS_READ_IO_PORT_BYTE = 0x9C4060CC;
    private const uint IOCTL_OLS_WRITE_IO_PORT_BYTE = 0x9C40A0D8;

    // Standard ACPI EC ports
    private const ushort EC_DATA = 0x62;
    private const ushort EC_SC   = 0x66;

    // Standard ACPI EC commands
    private const byte EC_CMD_READ  = 0x80;
    private const byte EC_CMD_WRITE = 0x81;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WriteIoPortInput
    {
        public uint PortNumber;
        public byte Value;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        ref uint lpInBuffer,
        uint nInBufferSize,
        out uint lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        ref WriteIoPortInput lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    private SafeFileHandle? _handle;
    private readonly object _lock = new();
    private bool _disposed;

    public bool IsOpen => _handle != null && !_handle.IsInvalid && !_handle.IsClosed;

    public bool Initialize()
    {
        lock (_lock)
        {
            if (IsOpen) return true;

            try
            {
                _handle = CreateFile(
                    @"\\.\WinRing0_1_2_0",
                    GENERIC_READ | GENERIC_WRITE,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    FILE_ATTRIBUTE_NORMAL,
                    IntPtr.Zero);

                if (_handle.IsInvalid)
                {
                    _handle.Dispose();
                    _handle = null;
                    return false;
                }

                return true;
            }
            catch
            {
                _handle = null;
                return false;
            }
        }
    }

    private byte ReadPort(ushort port)
    {
        if (!IsOpen) throw new InvalidOperationException("Driver handle is not open.");

        uint p = port;
        if (!DeviceIoControl(_handle!, IOCTL_OLS_READ_IO_PORT_BYTE, ref p, sizeof(uint), out uint outVal, sizeof(uint), out _, IntPtr.Zero))
        {
            throw new IOException($"Failed to read IO port 0x{port:X2}, Win32 error: {Marshal.GetLastWin32Error()}");
        }
        return (byte)outVal;
    }

    private void WritePort(ushort port, byte value)
    {
        if (!IsOpen) throw new InvalidOperationException("Driver handle is not open.");

        var input = new WriteIoPortInput { PortNumber = port, Value = value };
        if (!DeviceIoControl(_handle!, IOCTL_OLS_WRITE_IO_PORT_BYTE, ref input, (uint)Marshal.SizeOf<WriteIoPortInput>(), IntPtr.Zero, 0, out _, IntPtr.Zero))
        {
            throw new IOException($"Failed to write IO port 0x{port:X2}, Win32 error: {Marshal.GetLastWin32Error()}");
        }
    }

    private void WaitEc()
    {
        // Wait for EC Input Buffer Full (IBF, bit 1) to clear
        for (int i = 0; i < 2000; i++)
        {
            if ((ReadPort(EC_SC) & 0x02) == 0)
                return;
            Thread.SpinWait(10);
        }
        throw new TimeoutException("Timeout waiting for EC ready (IBF != 0)");
    }

    private void WaitObf()
    {
        // Wait for EC Output Buffer Full (OBF, bit 0) to be set
        for (int i = 0; i < 2000; i++)
        {
            if ((ReadPort(EC_SC) & 0x01) != 0)
                return;
            Thread.SpinWait(10);
        }
        throw new TimeoutException("Timeout waiting for EC data ready (OBF == 0)");
    }

    public byte ReadRegister(byte reg)
    {
        lock (_lock)
        {
            WaitEc();
            WritePort(EC_SC, EC_CMD_READ);
            WaitEc();
            WritePort(EC_DATA, reg);
            WaitObf();
            return ReadPort(EC_DATA);
        }
    }

    public void WriteRegister(byte reg, byte value)
    {
        lock (_lock)
        {
            WaitEc();
            WritePort(EC_SC, EC_CMD_WRITE);
            WaitEc();
            WritePort(EC_DATA, reg);
            WaitEc();
            WritePort(EC_DATA, value);
            Thread.Sleep(1);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (!_disposed)
            {
                _handle?.Dispose();
                _handle = null;
                _disposed = true;
            }
        }
    }
}
