using System.Runtime.InteropServices;

namespace DynoFanControl.Hardware.Gpu;

/// <summary>
/// NVIDIA NVAPI Direct Driver Wrapper via nvapi64.dll.
/// Interacts with NVIDIA's proprietary driver API for GPU overclocking, clock frequencies,
/// memory monitoring, power limits, and thermal performance states (P-States).
/// </summary>
public static class NvapiDriver
{
    private const string DllName = "nvapi64.dll";

    [DllImport(DllName, EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr NvAPI_QueryInterface(uint offset);

    // NVAPI Interface IDs
    private const uint ID_NvAPI_Initialize                      = 0x0150E828;
    private const uint ID_NvAPI_Unload                          = 0xD22BDD7E;
    private const uint ID_NvAPI_EnumPhysicalGPUs                = 0xE34407F2;
    private const uint ID_NvAPI_GPU_GetFullName                 = 0xCEEE8E9F;
    private const uint ID_NvAPI_GPU_GetAllClockFrequencies      = 0xDCB616C3;
    private const uint ID_NvAPI_GPU_GetPstates20                = 0x6FF81213;
    private const uint ID_NvAPI_GPU_SetPstates20                = 0x0F4DAE13;
    private const uint ID_NvAPI_GPU_ClientPowerPoliciesGetStatus= 0xAD131F37;
    private const uint ID_NvAPI_GPU_ClientPowerPoliciesSetStatus= 0x33C73588;
    private const uint ID_NvAPI_GPU_GetMemoryInfo               = 0x077F74F0;

    // Delegate Signatures
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_Initialize_Delegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_Unload_Delegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_EnumPhysicalGPUs_Delegate([Out] IntPtr[] gpuHandles, out int gpuCount);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_GPU_GetFullName_Delegate(IntPtr gpuHandle, [Out] byte[] name);

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct NV_GPU_CLOCK_FREQUENCIES
    {
        public uint Version;
        public uint ClockType;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public NV_GPU_CLOCK_DOMAIN[] Clocks;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct NV_GPU_CLOCK_DOMAIN
    {
        public uint bIsPresent;
        public uint FrequencyKhz;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_GPU_GetAllClockFrequencies_Delegate(IntPtr gpuHandle, ref NV_GPU_CLOCK_FREQUENCIES clockFrequencies);

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct NV_GPU_MEMORY_INFO
    {
        public uint Version;
        public uint DedicatedVideoMemoryKb;
        public uint AvailableDedicatedVideoMemoryKb;
        public uint SystemVideoMemoryKb;
        public uint SharedSystemMemoryKb;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_GPU_GetMemoryInfo_Delegate(IntPtr gpuHandle, ref NV_GPU_MEMORY_INFO memoryInfo);

    // P-States 2.0 Structures for Clock Offsets
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct NV_GPU_PERF_PSTATES20_INFO
    {
        public uint Version;
        public uint bIsEditable;
        public uint NumPstates;
        public uint NumClocks;
        public uint NumBaseVoltages;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public NV_GPU_PSTATE20_PSTATE[] Pstates;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public NV_GPU_PSTATE20_VOLTAGE[] Voltages;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct NV_GPU_PSTATE20_PSTATE
    {
        public int PstateId;
        public uint bIsEditable;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public NV_GPU_PSTATE20_CLOCK_ENTRY[] Clocks;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public NV_GPU_PSTATE20_BASE_VOLTAGE_ENTRY[] BaseVoltages;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct NV_GPU_PSTATE20_CLOCK_ENTRY
    {
        public int DomainId; // 0 = Graphics (Core), 4 = Memory
        public uint TypeId;
        public uint bIsEditable;
        public NV_GPU_PSTATE20_FREQ_DELTA FreqDeltaKhz;
        public uint Data0;
        public uint Data1;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct NV_GPU_PSTATE20_FREQ_DELTA
    {
        public int Value;
        public int Min;
        public int Max;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct NV_GPU_PSTATE20_BASE_VOLTAGE_ENTRY
    {
        public int DomainId;
        public uint bIsEditable;
        public int VoltDeltaUv;
        public int Min;
        public int Max;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct NV_GPU_PSTATE20_VOLTAGE
    {
        public int DomainId;
        public int VoltDeltaUv;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_GPU_GetPstates20_Delegate(IntPtr gpuHandle, ref NV_GPU_PERF_PSTATES20_INFO pstatesInfo);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_GPU_SetPstates20_Delegate(IntPtr gpuHandle, ref NV_GPU_PERF_PSTATES20_INFO pstatesInfo);

    // Delegates
    private static NvAPI_Initialize_Delegate? _initialize;
    private static NvAPI_Unload_Delegate? _unload;
    private static NvAPI_EnumPhysicalGPUs_Delegate? _enumGPUs;
    private static NvAPI_GPU_GetFullName_Delegate? _getFullName;
    private static NvAPI_GPU_GetAllClockFrequencies_Delegate? _getClocks;
    private static NvAPI_GPU_GetMemoryInfo_Delegate? _getMemoryInfo;
    private static NvAPI_GPU_GetPstates20_Delegate? _getPstates20;
    private static NvAPI_GPU_SetPstates20_Delegate? _setPstates20;

    private static bool _initialized;
    private static bool _available;
    private static IntPtr _primaryGpuHandle = IntPtr.Zero;
    private static string _gpuName = "NVIDIA GeForce";
    private static readonly object _lock = new();

    public static bool IsAvailable
    {
        get
        {
            if (!_initialized) Initialize();
            return _available;
        }
    }

    public static string GpuName => _gpuName;

    public static bool Initialize()
    {
        lock (_lock)
        {
            if (_initialized) return _available;
            _initialized = true;

            try
            {
                var initPtr = NvAPI_QueryInterface(ID_NvAPI_Initialize);
                if (initPtr == IntPtr.Zero) return false;
                _initialize = Marshal.GetDelegateForFunctionPointer<NvAPI_Initialize_Delegate>(initPtr);

                int status = _initialize();
                if (status != 0) return false;

                // Bind other delegates
                _unload = GetDelegate<NvAPI_Unload_Delegate>(ID_NvAPI_Unload);
                _enumGPUs = GetDelegate<NvAPI_EnumPhysicalGPUs_Delegate>(ID_NvAPI_EnumPhysicalGPUs);
                _getFullName = GetDelegate<NvAPI_GPU_GetFullName_Delegate>(ID_NvAPI_GPU_GetFullName);
                _getClocks = GetDelegate<NvAPI_GPU_GetAllClockFrequencies_Delegate>(ID_NvAPI_GPU_GetAllClockFrequencies);
                _getMemoryInfo = GetDelegate<NvAPI_GPU_GetMemoryInfo_Delegate>(ID_NvAPI_GPU_GetMemoryInfo);
                _getPstates20 = GetDelegate<NvAPI_GPU_GetPstates20_Delegate>(ID_NvAPI_GPU_GetPstates20);
                _setPstates20 = GetDelegate<NvAPI_GPU_SetPstates20_Delegate>(ID_NvAPI_GPU_SetPstates20);

                if (_enumGPUs != null)
                {
                    var handles = new IntPtr[64];
                    if (_enumGPUs(handles, out int count) == 0 && count > 0)
                    {
                        _primaryGpuHandle = handles[0];
                        if (_getFullName != null)
                        {
                            var nameBuf = new byte[64];
                            _getFullName(_primaryGpuHandle, nameBuf);
                            var name = System.Text.Encoding.ASCII.GetString(nameBuf).TrimEnd('\0').Trim();
                            if (!string.IsNullOrEmpty(name)) _gpuName = name;
                        }
                        _available = true;
                    }
                }

                return _available;
            }
            catch
            {
                _available = false;
                return false;
            }
        }
    }

    private static T? GetDelegate<T>(uint id) where T : class
    {
        var ptr = NvAPI_QueryInterface(id);
        if (ptr == IntPtr.Zero) return null;
        return Marshal.GetDelegateForFunctionPointer(ptr, typeof(T)) as T;
    }

    public static (uint coreClockMhz, uint memoryClockMhz, uint videoClockMhz) GetClockFrequencies()
    {
        if (!_available || _getClocks == null || _primaryGpuHandle == IntPtr.Zero)
            return (0, 0, 0);

        try
        {
            var cf = new NV_GPU_CLOCK_FREQUENCIES
            {
                Version = (uint)Marshal.SizeOf<NV_GPU_CLOCK_FREQUENCIES>() | 0x20000,
                ClockType = 0 // Current Clocks
            };

            int status = _getClocks(_primaryGpuHandle, ref cf);
            if (status == 0)
            {
                uint core = cf.Clocks[0].FrequencyKhz / 1000;
                uint mem = cf.Clocks[8].FrequencyKhz / 1000;
                uint video = cf.Clocks[1].FrequencyKhz / 1000;
                return (core, mem, video);
            }
        }
        catch { }

        return (0, 0, 0);
    }

    public static (uint totalMb, uint freeMb, uint usedMb) GetMemoryInfo()
    {
        if (!_available || _getMemoryInfo == null || _primaryGpuHandle == IntPtr.Zero)
            return (0, 0, 0);

        try
        {
            var mem = new NV_GPU_MEMORY_INFO
            {
                Version = (uint)Marshal.SizeOf<NV_GPU_MEMORY_INFO>() | 0x20000
            };

            if (_getMemoryInfo(_primaryGpuHandle, ref mem) == 0)
            {
                uint total = mem.DedicatedVideoMemoryKb / 1024;
                uint free = mem.AvailableDedicatedVideoMemoryKb / 1024;
                uint used = total > free ? total - free : 0;
                return (total, free, used);
            }
        }
        catch { }

        return (0, 0, 0);
    }

    public static bool SetClockOffsets(int coreOffsetMhz, int memoryOffsetMhz)
    {
        if (!_available || _getPstates20 == null || _setPstates20 == null || _primaryGpuHandle == IntPtr.Zero)
            return false;

        try
        {
            var pstates = new NV_GPU_PERF_PSTATES20_INFO
            {
                Version = (uint)Marshal.SizeOf<NV_GPU_PERF_PSTATES20_INFO>() | 0x10000
            };

            int status = _getPstates20(_primaryGpuHandle, ref pstates);
            if (status != 0) return false;

            // Apply offsets to P0 (highest performance state)
            for (int i = 0; i < pstates.NumPstates; i++)
            {
                if (pstates.Pstates[i].PstateId == 0) // P0 State
                {
                    for (int c = 0; c < 8; c++)
                    {
                        if (pstates.Pstates[i].Clocks[c].DomainId == 0) // Core Clock
                        {
                            pstates.Pstates[i].Clocks[c].FreqDeltaKhz.Value = coreOffsetMhz * 1000;
                        }
                        else if (pstates.Pstates[i].Clocks[c].DomainId == 4) // Memory Clock
                        {
                            pstates.Pstates[i].Clocks[c].FreqDeltaKhz.Value = memoryOffsetMhz * 1000;
                        }
                    }
                    break;
                }
            }

            int setStatus = _setPstates20(_primaryGpuHandle, ref pstates);
            return setStatus == 0;
        }
        catch
        {
            return false;
        }
    }

    public static void ResetToStock()
    {
        SetClockOffsets(0, 0);
    }
}
