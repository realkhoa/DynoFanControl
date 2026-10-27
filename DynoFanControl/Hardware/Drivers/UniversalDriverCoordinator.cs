using LibreHardwareMonitor.Hardware;

namespace DynoFanControl.Hardware.Drivers;

/// <summary>
/// Universal Hardware Driver Coordinator and Dispatcher.
/// Auto-detects host laptop or PC desktop architecture, selects the optimal driver stack,
/// and routes fan speed and tachometer commands across multi-vendor hardware.
/// </summary>
public sealed class UniversalDriverCoordinator : IDisposable
{
    private static readonly Lazy<UniversalDriverCoordinator> _instance = new(() => new UniversalDriverCoordinator());
    public static UniversalDriverCoordinator Instance => _instance.Value;

    private readonly List<IFanControlDriver> _registeredDrivers = new();
    private IFanControlDriver? _activeDriver;
    private SuperIoFanDriver? _superIoDriver;
    private DesktopGpuFanDriver? _desktopGpuDriver;
    private bool _initialized;
    private readonly object _lock = new();

    public IFanControlDriver ActiveDriver => _activeDriver ?? _registeredDrivers.First();
    public string ActiveDriverName => _activeDriver?.DriverName ?? "Hardware Auto-Detection";
    public string PlatformSummary => PlatformDetector.GetProfile().SummaryText;
    public bool IsControlAvailable => _activeDriver?.IsSupported == true;

    public void Initialize(IComputer? lhmComputer)
    {
        lock (_lock)
        {
            if (_initialized) return;
            _initialized = true;

            // 1. Create Driver Instances
            var acerEc = new AcerEcDriver();
            var acerWmi = new AcerWmiDriver();
            var asusWmi = new AsusWmiDriver();
            var lenovoEc = new LenovoEcDriver();
            var msiEc = new MsiEcDriver();
            var dellWmi = new DellAlienwareWmiDriver();
            _superIoDriver = new SuperIoFanDriver();
            _desktopGpuDriver = new DesktopGpuFanDriver();

            if (lhmComputer != null)
            {
                _superIoDriver.AttachComputer(lhmComputer);
                _desktopGpuDriver.AttachComputer(lhmComputer);
            }

            _registeredDrivers.Add(acerEc);
            _registeredDrivers.Add(acerWmi);
            _registeredDrivers.Add(asusWmi);
            _registeredDrivers.Add(lenovoEc);
            _registeredDrivers.Add(msiEc);
            _registeredDrivers.Add(dellWmi);
            _registeredDrivers.Add(_superIoDriver);
            _registeredDrivers.Add(_desktopGpuDriver);

            // 2. Select Best Driver by Matching Platform Profile & Priority Score
            var profile = PlatformDetector.GetProfile();
            IFanControlDriver? selected = null;

            // Search matching platform type drivers first
            var candidates = _registeredDrivers
                .Where(d => d.PlatformType == profile.PlatformType || d.IsSupported)
                .OrderByDescending(d => d.PriorityScore)
                .ToList();

            foreach (var driver in candidates)
            {
                try
                {
                    if (driver.Initialize())
                    {
                        selected = driver;
                        break;
                    }
                }
                catch { }
            }

            // Fallback: Try initializing any driver that reports support
            if (selected == null)
            {
                foreach (var driver in _registeredDrivers.OrderByDescending(d => d.PriorityScore))
                {
                    try
                    {
                        if (driver.IsSupported && driver.Initialize())
                        {
                            selected = driver;
                            break;
                        }
                    }
                    catch { }
                }
            }

            _activeDriver = selected ?? acerEc; // Fallback to default Acer EC
        }
    }

    public bool SetCpuFan(bool isAuto, int speedPercent)
    {
        if (_activeDriver != null)
        {
            var ok = _activeDriver.SetCpuFan(isAuto, speedPercent);
            if (ok) return true;
        }

        // Secondary Desktop Super I/O fallback
        if (_superIoDriver != null && _superIoDriver.IsSupported && _activeDriver != _superIoDriver)
        {
            return _superIoDriver.SetCpuFan(isAuto, speedPercent);
        }

        return false;
    }

    public bool SetGpuFan(bool isAuto, int speedPercent)
    {
        if (_activeDriver != null)
        {
            var ok = _activeDriver.SetGpuFan(isAuto, speedPercent);
            if (ok) return true;
        }

        // Desktop GPU fallback if on desktop or if primary driver doesn't support separate GPU fan
        if (_desktopGpuDriver != null && _activeDriver != _desktopGpuDriver)
        {
            var ok = _desktopGpuDriver.SetGpuFan(isAuto, speedPercent);
            if (ok) return true;
        }

        if (_superIoDriver != null && _superIoDriver.IsSupported && _activeDriver != _superIoDriver)
        {
            return _superIoDriver.SetGpuFan(isAuto, speedPercent);
        }

        return false;
    }

    public bool SetCoolBoost(bool enabled)
    {
        if (_activeDriver != null)
        {
            return _activeDriver.SetCoolBoost(enabled);
        }
        return false;
    }

    public FanTachometerReading ReadTachometers()
    {
        var reading = _activeDriver?.ReadTachometers() ?? new FanTachometerReading();

        // If active driver didn't report GPU fan RPM, check Desktop GPU or Super I/O
        if (reading.GpuFanRpm == 0 && _desktopGpuDriver != null)
        {
            var gpuReading = _desktopGpuDriver.ReadTachometers();
            if (gpuReading.GpuFanRpm > 0) reading.GpuFanRpm = gpuReading.GpuFanRpm;
        }

        if (reading.CpuFanRpm == 0 && _superIoDriver != null)
        {
            var sioReading = _superIoDriver.ReadTachometers();
            if (sioReading.CpuFanRpm > 0) reading.CpuFanRpm = sioReading.CpuFanRpm;
            if (reading.GpuFanRpm == 0 && sioReading.GpuFanRpm > 0) reading.GpuFanRpm = sioReading.GpuFanRpm;
        }

        return reading;
    }

    public void EmergencyReset()
    {
        foreach (var driver in _registeredDrivers)
        {
            try { driver.EmergencyReset(); } catch { }
        }
    }

    public void Dispose()
    {
        foreach (var driver in _registeredDrivers)
        {
            try { driver.Dispose(); } catch { }
        }
    }
}
