using System.Management;
using DynoFanControl.Hardware.Drivers;
using DynoFanControl.Hardware.Gpu;

namespace DynoFanControl.Hardware;

public class HardwareReading
{
    public double CpuTemp { get; set; }
    public double GpuTemp { get; set; }
    public double PeakTemp => Math.Max(CpuTemp, GpuTemp);
    public uint CpuRpm { get; set; }
    public uint GpuRpm { get; set; }
    public bool CpuIsAuto { get; set; } = true;
    public bool GpuIsAuto { get; set; } = true;
    public string GpuName { get; set; } = "Graphics Processor";
    public uint GpuUtilization { get; set; }
    public double GpuPowerWatts { get; set; }
    public bool GpuIsSleeping { get; set; }
    public List<GpuDeviceInfo> DetectedGpus { get; set; } = new();
    public List<SensorItem> AllSensors { get; set; } = new();
}

public sealed class FanHardwareManager : IDisposable
{
    private bool _disposed;

    public static FanHardwareManager Instance { get; } = new();

    public string BackendStatus => UniversalDriverCoordinator.Instance.ActiveDriverName;
    public string PlatformSummary => UniversalDriverCoordinator.Instance.PlatformSummary;
    public bool IsControlAvailable => UniversalDriverCoordinator.Instance.IsControlAvailable;

    public void Initialize()
    {
        // 1. Initialize Sensor Monitor (Universal GPU: NVML + AMD ADL + LHM Intel/AMD/NVIDIA + ACPI)
        try
        {
            SensorMonitorService.Instance.Initialize();
        }
        catch { }

        // 2. Initialize Universal Driver Coordinator (Acer, ASUS, Lenovo, MSI, Dell, Desktop Super I/O)
        try
        {
            UniversalDriverCoordinator.Instance.Initialize(SensorMonitorService.Instance.ComputerInstance);
        }
        catch { }
    }

    public bool SetCpuFan(bool isAuto, int speedPercent)
    {
        return UniversalDriverCoordinator.Instance.SetCpuFan(isAuto, speedPercent);
    }

    public bool SetGpuFan(bool isAuto, int speedPercent)
    {
        return UniversalDriverCoordinator.Instance.SetGpuFan(isAuto, speedPercent);
    }

    public bool SetCoolBoost(bool enabled)
    {
        return UniversalDriverCoordinator.Instance.SetCoolBoost(enabled);
    }

    public void EmergencyResetToAuto()
    {
        try
        {
            UniversalDriverCoordinator.Instance.EmergencyReset();
        }
        catch { }
    }

    public HardwareReading PollHardware()
    {
        var reading = new HardwareReading();

        // 1. Read All Sensors via SensorMonitorService (Universal GPU + CPU + Super I/O + ACPI)
        try
        {
            var snapshot = SensorMonitorService.Instance.ReadAllSensors();
            reading.CpuTemp = snapshot.CpuTemp;
            reading.GpuTemp = snapshot.GpuTemp;
            reading.GpuName = snapshot.GpuName;
            reading.GpuUtilization = snapshot.GpuUtilization;
            reading.GpuPowerWatts = snapshot.GpuPowerWatts;
            reading.GpuIsSleeping = snapshot.GpuIsSleeping;
            reading.DetectedGpus = snapshot.DetectedGpus;
            reading.AllSensors = snapshot.AllSensors;
        }
        catch { }

        // 2. Poll Fan Tachometers via Universal Driver Coordinator
        try
        {
            var tach = UniversalDriverCoordinator.Instance.ReadTachometers();
            reading.CpuRpm = tach.CpuFanRpm;
            reading.GpuRpm = tach.GpuFanRpm;
            reading.CpuIsAuto = tach.CpuIsAuto;
            reading.GpuIsAuto = tach.GpuIsAuto;

            // Acer WMI Fallback if on Acer and tachometer is 0
            if (reading.CpuRpm == 0 && reading.GpuRpm == 0)
            {
                PollAcerWmiTachometers(reading);
            }
        }
        catch { }

        return reading;
    }

    private void PollAcerWmiTachometers(HardwareReading reading)
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\wmi");
            scope.Connect();

            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM AcerGamingFunction"));
            foreach (ManagementObject gamingInst in searcher.Get())
            {
                using (gamingInst)
                {
                    // CPU Fan (Index 0)
                    var inParams0 = gamingInst.GetMethodParameters("GetGamingFanSpeed");
                    inParams0["gmInput"] = 0u;
                    using var out0 = (ManagementBaseObject)gamingInst.InvokeMethod("GetGamingFanSpeed", inParams0, null);
                    var raw0 = (ulong)out0["gmOutput"];
                    reading.CpuRpm = (uint)(raw0 >> 32);
                    reading.CpuIsAuto = (raw0 & 1) == 1;

                    // GPU Fan (Index 1)
                    var inParams1 = gamingInst.GetMethodParameters("GetGamingFanSpeed");
                    inParams1["gmInput"] = 1u;
                    using var out1 = (ManagementBaseObject)gamingInst.InvokeMethod("GetGamingFanSpeed", inParams1, null);
                    var raw1 = (ulong)out1["gmOutput"];
                    reading.GpuRpm = (uint)(raw1 >> 32);
                    reading.GpuIsAuto = (raw1 & 1) == 1;
                }
                break;
            }
        }
        catch { }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            UniversalDriverCoordinator.Instance.Dispose();
            SensorMonitorService.Instance.Dispose();
            _disposed = true;
        }
    }
}
