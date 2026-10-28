using System.IO;
using System.Runtime.InteropServices;

namespace DynoFanControl.Hardware;

public class NvGpuInfo
{
    public bool IsAvailable { get; set; }
    public string Name { get; set; } = "NVIDIA GPU";
    public double Temperature { get; set; }
    public uint UtilizationGpu { get; set; }
    public uint UtilizationMemory { get; set; }
    public double PowerWatts { get; set; }
    public bool IsSleeping { get; set; }

    public uint CoreClockMhz { get; set; }
    public uint MemoryClockMhz { get; set; }
    public uint VideoClockMhz { get; set; }

    public ulong VramTotalBytes { get; set; }
    public ulong VramUsedBytes { get; set; }
    public ulong VramFreeBytes { get; set; }
    public double VramUsedMb => Math.Round(VramUsedBytes / (1024.0 * 1024.0), 0);
    public double VramTotalMb => Math.Round(VramTotalBytes / (1024.0 * 1024.0), 0);

    public uint PowerLimitMw { get; set; }
    public uint DefaultPowerLimitMw { get; set; }
    public uint MinPowerLimitMw { get; set; }
    public uint MaxPowerLimitMw { get; set; }
}

/// <summary>
/// Direct NVIDIA Management Library (NVML) interface via nvml.dll.
/// Provides sub-millisecond, highly accurate NVIDIA GPU temperature, clocks, VRAM, and power limits.
/// </summary>
public static class NvmlDriver
{
    private const int NVML_SUCCESS = 0;
    private const int NVML_TEMPERATURE_GPU = 0;
    private const int NVML_ERROR_GPU_IS_LOST = 15;

    private const uint NVML_CLOCK_GRAPHICS = 0;
    private const uint NVML_CLOCK_SM       = 1;
    private const uint NVML_CLOCK_MEM      = 2;
    private const uint NVML_CLOCK_VIDEO    = 3;

    [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlInit();

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetCount_v2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetCount(out uint deviceCount);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetHandleByIndex(uint index, out IntPtr device);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperature", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetTemperature(IntPtr device, int sensorType, out uint temp);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetName", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetName(IntPtr device, byte[] name, uint length);

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlUtilization
    {
        public uint Gpu;
        public uint Memory;
    }

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetUtilizationRates", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out NvmlUtilization rates);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetPowerUsage", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint milliwatts);

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlMemory
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetMemoryInfo", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetMemoryInfo(IntPtr device, out NvmlMemory memory);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetClockInfo", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetClockInfo(IntPtr device, uint clockType, out uint clockMHz);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetPowerManagementLimit", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetPowerManagementLimit(IntPtr device, out uint milliwatts);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetPowerManagementDefaultLimit", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetPowerManagementDefaultLimit(IntPtr device, out uint milliwatts);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetPowerManagementLimitConstraints", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetPowerManagementLimitConstraints(IntPtr device, out uint minMilliwatts, out uint maxMilliwatts);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceSetPowerManagementLimit", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceSetPowerManagementLimit(IntPtr device, uint milliwatts);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceSetGpuLockedClocks", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceSetGpuLockedClocks(IntPtr device, uint minGpuClockMHz, uint maxGpuClockMHz);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceResetGpuLockedClocks", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceResetGpuLockedClocks(IntPtr device);

    [DllImport("nvml.dll", EntryPoint = "nvmlShutdown", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlShutdown();

    private static bool _initialized;
    private static IntPtr _deviceHandle = IntPtr.Zero;
    private static string _gpuName = "NVIDIA GeForce GTX 1650";
    private static readonly object _lock = new();

    public static bool IsSupported
    {
        get
        {
            var path = Path.Combine(Environment.SystemDirectory, "nvml.dll");
            return File.Exists(path);
        }
    }

    public static bool Initialize()
    {
        lock (_lock)
        {
            if (_initialized && _deviceHandle != IntPtr.Zero) return true;
            if (!IsSupported) return false;

            try
            {
                int ret = nvmlInit();
                if (ret != NVML_SUCCESS) return false;

                ret = nvmlDeviceGetCount(out uint count);
                if (ret != NVML_SUCCESS || count == 0) return false;

                ret = nvmlDeviceGetHandleByIndex(0, out _deviceHandle);
                if (ret != NVML_SUCCESS || _deviceHandle == IntPtr.Zero) return false;

                var nameBytes = new byte[64];
                nvmlDeviceGetName(_deviceHandle, nameBytes, (uint)nameBytes.Length);
                var rawName = System.Text.Encoding.ASCII.GetString(nameBytes).TrimEnd('\0').Trim();
                if (!string.IsNullOrEmpty(rawName))
                    _gpuName = rawName;

                _initialized = true;
                return true;
            }
            catch
            {
                _initialized = false;
                _deviceHandle = IntPtr.Zero;
                return false;
            }
        }
    }

    public static NvGpuInfo QueryGpu()
    {
        lock (_lock)
        {
            var info = new NvGpuInfo { Name = _gpuName };
            if (!_initialized)
            {
                if (!Initialize())
                    return info;
            }

            try
            {
                // 1. Query GPU Core Temperature
                int ret = nvmlDeviceGetTemperature(_deviceHandle, NVML_TEMPERATURE_GPU, out uint temp);
                if (ret == NVML_SUCCESS)
                {
                    info.IsAvailable = true;
                    info.Temperature = temp;
                    info.IsSleeping = false;
                }
                else if (ret == NVML_ERROR_GPU_IS_LOST)
                {
                    // GPU in deep sleep (Optimized power savings)
                    info.IsAvailable = true;
                    info.IsSleeping = true;
                    return info;
                }
                else
                {
                    _initialized = false;
                    return info;
                }

                // 2. Query Utilization Rates
                if (nvmlDeviceGetUtilizationRates(_deviceHandle, out var util) == NVML_SUCCESS)
                {
                    info.UtilizationGpu = util.Gpu;
                    info.UtilizationMemory = util.Memory;
                }

                // 3. Query Power Draw
                if (nvmlDeviceGetPowerUsage(_deviceHandle, out uint mw) == NVML_SUCCESS)
                {
                    info.PowerWatts = Math.Round(mw / 1000.0, 1);
                }

                // 4. Query Clocks
                if (nvmlDeviceGetClockInfo(_deviceHandle, NVML_CLOCK_GRAPHICS, out uint coreClk) == NVML_SUCCESS)
                    info.CoreClockMhz = coreClk;

                if (nvmlDeviceGetClockInfo(_deviceHandle, NVML_CLOCK_MEM, out uint memClk) == NVML_SUCCESS)
                    info.MemoryClockMhz = memClk;

                if (nvmlDeviceGetClockInfo(_deviceHandle, NVML_CLOCK_VIDEO, out uint vidClk) == NVML_SUCCESS)
                    info.VideoClockMhz = vidClk;

                // 5. Query VRAM Memory
                if (nvmlDeviceGetMemoryInfo(_deviceHandle, out var mem) == NVML_SUCCESS)
                {
                    info.VramTotalBytes = mem.Total;
                    info.VramFreeBytes = mem.Free;
                    info.VramUsedBytes = mem.Used;
                }

                // 6. Query Power Limits
                if (nvmlDeviceGetPowerManagementLimit(_deviceHandle, out uint pwrLimit) == NVML_SUCCESS)
                    info.PowerLimitMw = pwrLimit;

                if (nvmlDeviceGetPowerManagementDefaultLimit(_deviceHandle, out uint defLimit) == NVML_SUCCESS)
                    info.DefaultPowerLimitMw = defLimit;

                if (nvmlDeviceGetPowerManagementLimitConstraints(_deviceHandle, out uint minLimit, out uint maxLimit) == NVML_SUCCESS)
                {
                    info.MinPowerLimitMw = minLimit;
                    info.MaxPowerLimitMw = maxLimit;
                }

                return info;
            }
            catch
            {
                _initialized = false;
                return info;
            }
        }
    }

    public static bool SetPowerLimitPercent(int percent)
    {
        lock (_lock)
        {
            if (!_initialized && !Initialize()) return false;
            try
            {
                if (nvmlDeviceGetPowerManagementDefaultLimit(_deviceHandle, out uint defMw) != NVML_SUCCESS || defMw == 0)
                    return false;

                uint targetMw = (uint)(defMw * (percent / 100.0));
                if (nvmlDeviceGetPowerManagementLimitConstraints(_deviceHandle, out uint minMw, out uint maxMw) == NVML_SUCCESS)
                {
                    targetMw = Math.Clamp(targetMw, minMw, maxMw);
                }

                return nvmlDeviceSetPowerManagementLimit(_deviceHandle, targetMw) == NVML_SUCCESS;
            }
            catch
            {
                return false;
            }
        }
    }

    public static bool SetLockedGpuClocks(uint minMhz, uint maxMhz)
    {
        lock (_lock)
        {
            if (!_initialized && !Initialize()) return false;
            try
            {
                return nvmlDeviceSetGpuLockedClocks(_deviceHandle, minMhz, maxMhz) == NVML_SUCCESS;
            }
            catch
            {
                return false;
            }
        }
    }

    public static bool ResetLockedGpuClocks()
    {
        lock (_lock)
        {
            if (!_initialized && !Initialize()) return false;
            try
            {
                return nvmlDeviceResetGpuLockedClocks(_deviceHandle) == NVML_SUCCESS;
            }
            catch
            {
                return false;
            }
        }
    }

    public static void Shutdown()
    {
        lock (_lock)
        {
            if (_initialized)
            {
                try { nvmlShutdown(); } catch { }
                _initialized = false;
                _deviceHandle = IntPtr.Zero;
            }
        }
    }
}
