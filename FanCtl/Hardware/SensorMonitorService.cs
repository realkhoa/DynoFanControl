using System.Management;
using LibreHardwareMonitor.Hardware;

namespace FanCtl.Hardware;

public class SensorItem
{
    public string Category { get; set; } = ""; // "CPU", "GPU", "Thermal Zone", "Storage", "Battery"
    public string Name { get; set; } = "";
    public double Value { get; set; }
    public string Unit { get; set; } = "°C";
    public string FormattedValue => $"{Value:F1}{Unit}";
    public string Subtext { get; set; } = "";
}

public class MultiSensorSnapshot
{
    public double CpuTemp { get; set; }
    public double GpuTemp { get; set; }
    public string GpuName { get; set; } = "NVIDIA GeForce GTX 1650";
    public uint GpuUtilization { get; set; }
    public double GpuPowerWatts { get; set; }
    public bool GpuIsSleeping { get; set; }
    public List<SensorItem> AllSensors { get; } = new();
}

public sealed class SensorMonitorService : IDisposable
{
    private Computer? _computer;
    private bool _computerInitialized;
    private readonly object _lock = new();

    public static SensorMonitorService Instance { get; } = new();

    public void Initialize()
    {
        lock (_lock)
        {
            if (_computerInitialized) return;

            // 1. Initialize NVML for discrete GPU
            NvmlDriver.Initialize();

            // 2. Initialize LibreHardwareMonitor for CPU & System
            try
            {
                _computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsMotherboardEnabled = true,
                    IsStorageEnabled = true,
                    IsBatteryEnabled = true
                };
                _computer.Open();
                _computerInitialized = true;
            }
            catch
            {
                _computer = null;
                _computerInitialized = false;
            }
        }
    }

    public MultiSensorSnapshot ReadAllSensors()
    {
        var snapshot = new MultiSensorSnapshot();

        // 1. Query NVIDIA GPU via NVML (Primary Discrete GPU Sensor)
        try
        {
            var nv = NvmlDriver.QueryGpu();
            if (nv.IsAvailable)
            {
                snapshot.GpuName = nv.Name;
                snapshot.GpuUtilization = nv.UtilizationGpu;
                snapshot.GpuPowerWatts = nv.PowerWatts;
                snapshot.GpuIsSleeping = nv.IsSleeping;

                if (!nv.IsSleeping && nv.Temperature > 0)
                {
                    snapshot.GpuTemp = nv.Temperature;
                    snapshot.AllSensors.Add(new SensorItem
                    {
                        Category = "Discrete GPU",
                        Name = "GPU Core",
                        Value = nv.Temperature,
                        Unit = "°C",
                        Subtext = $"{nv.Name} • {nv.UtilizationGpu}% Load • {nv.PowerWatts}W"
                    });
                }
                else if (nv.IsSleeping)
                {
                    snapshot.AllSensors.Add(new SensorItem
                    {
                        Category = "Discrete GPU",
                        Name = "GPU Core (Sleep Mode)",
                        Value = 0,
                        Unit = "°C",
                        Subtext = $"{nv.Name} • Power Savings (0W)"
                    });
                }
            }
        }
        catch { }

        // 2. Query LibreHardwareMonitor (CPU, APU, NVMe Storage, Motherboard)
        lock (_lock)
        {
            if (_computer != null)
            {
                try
                {
                    foreach (var hw in _computer.Hardware)
                    {
                        hw.Update();

                        // Process sub-hardware (e.g. CPU cores)
                        foreach (var sub in hw.SubHardware)
                        {
                            sub.Update();
                            ExtractTemperatures(sub, snapshot);
                        }

                        ExtractTemperatures(hw, snapshot);
                    }
                }
                catch { }
            }
        }

        // 3. Fallback: Query ACPI Thermal Zones via WMI
        try
        {
            var scope = new ManagementScope(@"\\.\root\wmi");
            scope.Connect();

            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSAcpi_ThermalZoneTemperature"));
            int tzIndex = 1;
            foreach (var obj in searcher.Get())
            {
                var raw = obj["CurrentTemperature"];
                var name = obj["InstanceName"]?.ToString() ?? $"TZ0{tzIndex}";
                if (raw != null)
                {
                    var deciKelvin = Convert.ToUInt32(raw);
                    var celsius = Math.Round((deciKelvin / 10.0) - 273.15, 1);
                    if (celsius is > 0 and < 125)
                    {
                        var isCpuZone = name.Contains("TZ01", StringComparison.OrdinalIgnoreCase);
                        var isGpuZone = name.Contains("TZ02", StringComparison.OrdinalIgnoreCase);

                        if (isCpuZone && snapshot.CpuTemp == 0)
                            snapshot.CpuTemp = celsius;
                        if (isGpuZone && snapshot.GpuTemp == 0)
                            snapshot.GpuTemp = celsius;

                        var label = isCpuZone ? "ACPI CPU Zone (TZ01)" : (isGpuZone ? "ACPI GPU Zone (TZ02)" : $"ACPI Zone {tzIndex} ({name})");
                        snapshot.AllSensors.Add(new SensorItem
                        {
                            Category = "Thermal Zone",
                            Name = label,
                            Value = celsius,
                            Unit = "°C",
                            Subtext = name
                        });
                    }
                }
                tzIndex++;
            }
        }
        catch { }

        // 4. Ensure non-zero primary temperatures if available
        if (snapshot.CpuTemp == 0)
        {
            var cpuSensor = snapshot.AllSensors.FirstOrDefault(s => s.Category == "CPU" && s.Value > 0);
            if (cpuSensor != null) snapshot.CpuTemp = cpuSensor.Value;
        }

        if (snapshot.GpuTemp == 0)
        {
            var gpuSensor = snapshot.AllSensors.FirstOrDefault(s => (s.Category == "Discrete GPU" || s.Name.Contains("GPU")) && s.Value > 0);
            if (gpuSensor != null) snapshot.GpuTemp = gpuSensor.Value;
        }

        return snapshot;
    }

    private void ExtractTemperatures(IHardware hw, MultiSensorSnapshot snapshot)
    {
        var category = hw.HardwareType switch
        {
            HardwareType.Cpu => "CPU",
            HardwareType.GpuNvidia => "Discrete GPU",
            HardwareType.GpuAmd => "Integrated GPU",
            HardwareType.Storage => "Storage",
            HardwareType.Motherboard => "Motherboard",
            HardwareType.Battery => "Battery",
            _ => hw.HardwareType.ToString()
        };

        foreach (var sensor in hw.Sensors)
        {
            if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue)
            {
                var val = Math.Round((double)sensor.Value.Value, 1);
                if (val is > 0 and < 130)
                {
                    // Update primary CPU Temp
                    if (hw.HardwareType == HardwareType.Cpu)
                    {
                        if (sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                            sensor.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
                            sensor.Name.Contains("Core (Tctl/Tdie)", StringComparison.OrdinalIgnoreCase))
                        {
                            snapshot.CpuTemp = val;
                        }
                        else if (snapshot.CpuTemp == 0)
                        {
                            snapshot.CpuTemp = val;
                        }
                    }

                    // Update primary GPU Temp if not already set by NVML
                    if (hw.HardwareType == HardwareType.GpuNvidia && snapshot.GpuTemp == 0)
                    {
                        snapshot.GpuTemp = val;
                    }

                    snapshot.AllSensors.Add(new SensorItem
                    {
                        Category = category,
                        Name = sensor.Name,
                        Value = val,
                        Unit = "°C",
                        Subtext = hw.Name
                    });
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_computer != null)
            {
                try { _computer.Close(); } catch { }
                _computer = null;
                _computerInitialized = false;
            }
            NvmlDriver.Shutdown();
        }
    }
}
