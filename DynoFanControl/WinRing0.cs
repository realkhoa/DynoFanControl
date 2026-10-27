using System.Runtime.InteropServices;

namespace DynoFanControl;

internal static class WinRing0
{
    private const string DllName = "WinRing0x64.dll";

    [DllImport(DllName)]
    public static extern int InitializeOls();

    [DllImport(DllName)]
    public static extern void DeinitializeOls();

    [DllImport(DllName)]
    public static extern uint GetDllStatus();

    [DllImport(DllName)]
    public static extern byte ReadIoPortByte(ushort port);

    [DllImport(DllName)]
    public static extern void WriteIoPortByte(ushort port, byte value);
}
