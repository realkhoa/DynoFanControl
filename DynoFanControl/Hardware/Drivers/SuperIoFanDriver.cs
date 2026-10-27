using LibreHardwareMonitor.Hardware;

namespace DynoFanControl.Hardware.Drivers;

/// <summary>
/// Universal Desktop PC Motherboard Super I/O Fan Controller Driver.
/// Controls fans on desktop motherboards (Nuvoton NCT67xx, ITE IT86xx/IT87xx, Fintek, Winbond)
/// and USB AIO liquid coolers/fan hubs (Corsair Commander, NZXT Kraken, AquaComputer) via LibreHardwareMonitor.
/// </summary>
public sealed class SuperIoFanDriver : IFanControlDriver
{
    private IComputer? _lhmComputer;
    private readonly List<ISensor> _cpuControls = new();
    private readonly List<ISensor> _gpuChassisControls = new();
    private readonly List<ISensor> _allFanTachometers = new();
    private bool _initialized;
    private bool _available;
    private string _detectedChipName = "Desktop Motherboard Controller";

    public string DriverName => $"Desktop Motherboard Super I/O ({_detectedChipName})";
    public string PlatformDescription => "Desktop Motherboard Fan Headers (Nuvoton/ITE/Fintek Super I/O & AIO Controllers)";
    public HardwarePlatformType PlatformType => HardwarePlatformType.DesktopPc;
    public int PriorityScore => 80;

    public bool IsSupported
    {
        get
        {
            var p = PlatformDetector.GetProfile();
            return !p.IsLaptop || p.PlatformType == HardwarePlatformType.DesktopPc;
        }
    }

    public void AttachComputer(IComputer computer)
    {
        _lhmComputer = computer;
        ScanControls();
    }

    public bool Initialize()
    {
        if (_initialized) return _available;
        _initialized = true;

        if (_lhmComputer != null)
        {
            ScanControls();
        }

        return _available;
    }

    private void ScanControls()
    {
        if (_lhmComputer == null) return;

        _cpuControls.Clear();
        _gpuChassisControls.Clear();
        _allFanTachometers.Clear();

        try
        {
            foreach (var hw in _lhmComputer.Hardware)
            {
                hw.Update();
                if (hw.HardwareType is HardwareType.Motherboard or HardwareType.SuperIO or HardwareType.Cooler)
                {
                    if (!string.IsNullOrEmpty(hw.Name) && !hw.Name.Contains("Generic", StringComparison.OrdinalIgnoreCase))
                    {
                        _detectedChipName = hw.Name;
                    }

                    // Check SubHardware (e.g. Super I/O chips under Motherboard)
                    foreach (var sub in hw.SubHardware)
                    {
                        sub.Update();
                        if (!string.IsNullOrEmpty(sub.Name)) _detectedChipName = sub.Name;
                        InspectSensors(sub);
                    }

                    InspectSensors(hw);
                }
            }

            _available = _cpuControls.Count > 0 || _gpuChassisControls.Count > 0 || _allFanTachometers.Count > 0;
        }
        catch
        {
            _available = false;
        }
    }

    private void InspectSensors(IHardware hw)
    {
        foreach (var s in hw.Sensors)
        {
            if (s.SensorType == SensorType.Control && s.Control != null)
            {
                var name = s.Name.ToLowerInvariant();
                if (name.Contains("cpu") || name.Contains("pump") || name.Contains("fan #1") || name.Contains("fan 1"))
                {
                    _cpuControls.Add(s);
                }
                else
                {
                    _gpuChassisControls.Add(s);
                }
            }
            else if (s.SensorType == SensorType.Fan)
            {
                _allFanTachometers.Add(s);
            }
        }
    }

    public bool SetCpuFan(bool isAuto, int speedPercent)
    {
        if (!_available) return false;
        bool ok = false;
        foreach (var ctrl in _cpuControls)
        {
            try
            {
                if (isAuto)
                {
                    ctrl.Control?.SetDefault();
                }
                else
                {
                    ctrl.Control?.SetSoftware(Math.Clamp(speedPercent, 0, 100));
                }
                ok = true;
            }
            catch { }
        }
        return ok;
    }

    public bool SetGpuFan(bool isAuto, int speedPercent)
    {
        if (!_available) return false;
        bool ok = false;
        foreach (var ctrl in _gpuChassisControls)
        {
            try
            {
                if (isAuto)
                {
                    ctrl.Control?.SetDefault();
                }
                else
                {
                    ctrl.Control?.SetSoftware(Math.Clamp(speedPercent, 0, 100));
                }
                ok = true;
            }
            catch { }
        }
        return ok;
    }

    public bool SetCoolBoost(bool enabled)
    {
        // On desktop, CoolBoost sets all chassis fans to 100%
        if (enabled)
        {
            SetCpuFan(false, 100);
            SetGpuFan(false, 100);
            return true;
        }
        return false;
    }

    public FanTachometerReading ReadTachometers()
    {
        var reading = new FanTachometerReading();
        if (!_available) return reading;

        try
        {
            foreach (var fan in _allFanTachometers)
            {
                if (fan.Value.HasValue && fan.Value.Value > 0)
                {
                    var val = (uint)Math.Round(fan.Value.Value);
                    var name = fan.Name.ToLowerInvariant();
                    if (name.Contains("cpu") && reading.CpuFanRpm == 0)
                    {
                        reading.CpuFanRpm = val;
                    }
                    else if ((name.Contains("gpu") || name.Contains("chassis") || name.Contains("sys") || name.Contains("fan 2")) && reading.GpuFanRpm == 0)
                    {
                        reading.GpuFanRpm = val;
                    }
                    else
                    {
                        reading.AdditionalFans.Add((fan.Name, val));
                    }
                }
            }
        }
        catch { }

        return reading;
    }

    public void EmergencyReset()
    {
        SetCpuFan(true, 0);
        SetGpuFan(true, 0);
    }

    public void Dispose()
    {
        EmergencyReset();
        _available = false;
    }
}
