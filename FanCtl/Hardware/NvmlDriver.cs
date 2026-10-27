using System.IO;
using System.Runtime.InteropServices;

namespace FanCtl.Hardware;

public class NvGpuInfo
{
    public bool IsAvailable { get; set; }
    public string Name { get; set; } = "NVIDIA GPU";
    public double Temperature { get; set; }
    public uint UtilizationGpu { get; set; }
    public double PowerWatts { get; set; }
    public bool IsSleeping { get; set; }
}

/// <summary>
/// Direct NVIDIA Management Library (NVML) interface via nvml.dll.
/// Provides sub-millisecond, highly accurate NVIDIA GPU temperature and metrics.
/// </summary>
public static class NvmlDriver
{
    private const int NVML_SUCCESS = 0;
    private const int NVML_TEMPERATURE_GPU = 0;
    private const int NVML_ERROR_GPU_IS_LOST = 15;

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
                // Query GPU Core Temperature
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
                    // Reinitialize attempt next cycle
                    _initialized = false;
                    return info;
                }

                // Query Utilization
                if (nvmlDeviceGetUtilizationRates(_deviceHandle, out var util) == NVML_SUCCESS)
                {
                    info.UtilizationGpu = util.Gpu;
                }

                // Query Power Draw
                if (nvmlDeviceGetPowerUsage(_deviceHandle, out uint mw) == NVML_SUCCESS)
                {
                    info.PowerWatts = Math.Round(mw / 1000.0, 1);
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
