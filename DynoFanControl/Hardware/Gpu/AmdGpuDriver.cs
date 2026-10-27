using System.IO;
using System.Runtime.InteropServices;

namespace DynoFanControl.Hardware.Gpu;

/// <summary>
/// Direct P/Invoke wrapper for AMD Display Library (ADL: atiadlxx.dll / atiadlxy.dll).
/// Supports AMD Radeon RX 5000/6000/7000/8000, Vega, Polaris, and integrated Radeon APUs.
/// </summary>
public static class AmdGpuDriver
{
    private const string DllName64 = "atiadlxx.dll";
    private static bool _initialized;
    private static bool _available;
    private static int _adapterIndex = -1;
    private static string _adapterName = "";

    [StructLayout(LayoutKind.Sequential)]
    private struct AdapterInfo
    {
        public int Size;
        public int AdapterIndex;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string UDID;
        public int BusNumber;
        public int DeviceNumber;
        public int FunctionNumber;
        public int VendorID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string AdapterString;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string DriverPath;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string DriverPathExt;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string PNPString;
        public int OSDisplayIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ADLTemperature
    {
        public int Size;
        public int Temperature; // In millidegrees Celsius (temp * 1000)
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ADLFanSpeedValue
    {
        public int Size;
        public int SpeedType;
        public int FanSpeed;
        public int Flags;
    }

    private delegate IntPtr ADL_Main_Memory_Alloc(int size);

    [DllImport(DllName64, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL_Main_Control_Create(ADL_Main_Memory_Alloc callback, int enumConnectedAdapters);

    [DllImport(DllName64, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL_Main_Control_Destroy();

    [DllImport(DllName64, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL_Adapter_NumberOfAdapters_Get(out int numAdapters);

    [DllImport(DllName64, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL_Adapter_AdapterInfo_Get(IntPtr info, int inputSize);

    [DllImport(DllName64, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL_Overdrive5_Temperature_Get(int adapterIndex, int thermalControllerIndex, out ADLTemperature temp);

    [DllImport(DllName64, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL_Overdrive5_FanSpeed_Get(int adapterIndex, int thermalControllerIndex, ref ADLFanSpeedValue fanSpeedValue);

    [DllImport(DllName64, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL_Overdrive5_FanSpeed_Set(int adapterIndex, int thermalControllerIndex, ref ADLFanSpeedValue fanSpeedValue);

    private static IntPtr AllocMemory(int size)
    {
        return Marshal.AllocHGlobal(size);
    }

    private static readonly ADL_Main_Memory_Alloc AllocCallback = AllocMemory;

    public static bool IsAvailable
    {
        get
        {
            if (!_initialized) Initialize();
            return _available;
        }
    }

    public static string AdapterName => _adapterName;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        var systemDll = Path.Combine(Environment.SystemDirectory, DllName64);
        if (!File.Exists(systemDll))
        {
            _available = false;
            return;
        }

        try
        {
            var res = ADL_Main_Control_Create(AllocCallback, 1);
            if (res != 0) // ADL_OK = 0
            {
                _available = false;
                return;
            }

            int numAdapters = 0;
            ADL_Adapter_NumberOfAdapters_Get(out numAdapters);
            if (numAdapters <= 0)
            {
                _available = false;
                return;
            }

            int size = Marshal.SizeOf<AdapterInfo>() * numAdapters;
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                var infoRes = ADL_Adapter_AdapterInfo_Get(ptr, size);
                if (infoRes == 0)
                {
                    for (int i = 0; i < numAdapters; i++)
                    {
                        var infoPtr = IntPtr.Add(ptr, i * Marshal.SizeOf<AdapterInfo>());
                        var info = Marshal.PtrToStructure<AdapterInfo>(infoPtr);
                        if (info.VendorID == 0x1002 && !string.IsNullOrWhiteSpace(info.AdapterString)) // 0x1002 = AMD/ATI
                        {
                            _adapterIndex = info.AdapterIndex;
                            _adapterName = info.AdapterString;
                            _available = true;
                            break;
                        }
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch
        {
            _available = false;
        }
    }

    public static double GetTemperature()
    {
        if (!_available || _adapterIndex < 0) return 0;
        try
        {
            var res = ADL_Overdrive5_Temperature_Get(_adapterIndex, 0, out var temp);
            if (res == 0 && temp.Temperature > 0)
            {
                return temp.Temperature / 1000.0;
            }
        }
        catch { }
        return 0;
    }

    public static uint GetFanRpm()
    {
        if (!_available || _adapterIndex < 0) return 0;
        try
        {
            var fanVal = new ADLFanSpeedValue
            {
                Size = Marshal.SizeOf<ADLFanSpeedValue>(),
                SpeedType = 1 // RPM
            };
            var res = ADL_Overdrive5_FanSpeed_Get(_adapterIndex, 0, ref fanVal);
            if (res == 0 && fanVal.FanSpeed > 0)
            {
                return (uint)fanVal.FanSpeed;
            }
        }
        catch { }
        return 0;
    }

    public static bool SetFanSpeed(int percent)
    {
        if (!_available || _adapterIndex < 0) return false;
        try
        {
            var fanVal = new ADLFanSpeedValue
            {
                Size = Marshal.SizeOf<ADLFanSpeedValue>(),
                SpeedType = 2, // Percentage
                FanSpeed = Math.Clamp(percent, 0, 100),
                Flags = 1 // ADL_DL_FANCTRL_FLAG_USER_DEFINED_SPEED
            };
            return ADL_Overdrive5_FanSpeed_Set(_adapterIndex, 0, ref fanVal) == 0;
        }
        catch
        {
            return false;
        }
    }

    public static void Shutdown()
    {
        if (_initialized && _available)
        {
            try
            {
                ADL_Main_Control_Destroy();
            }
            catch { }
            _available = false;
        }
    }
}
