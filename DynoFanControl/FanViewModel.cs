using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DynoFanControl.Hardware;
using DynoFanControl.Hardware.Drivers;
using DynoFanControl.Hardware.Gpu;
using DynoFanControl.Hardware.Tuning;
using DynoFanControl.Models;
using DynoFanControl.Services;
using DynoFanControl.Services.Automation;
using DynoFanControl.Services.Calibration;
using DynoFanControl.Services.Cpu;
using DynoFanControl.Services.Osd;
using DynoFanControl.Services.Power;

namespace DynoFanControl;

public class FanViewModel : INotifyPropertyChanged
{
    private static readonly SolidColorBrush ActiveTabBg = new(Color.FromRgb(32, 42, 56));
    private static readonly SolidColorBrush InactiveTabBg = new(Color.FromRgb(24, 26, 32));
    private static readonly SolidColorBrush ActiveTabBorder = new(Color.FromRgb(0, 229, 255));
    private static readonly SolidColorBrush InactiveTabBorder = new(Color.FromRgb(45, 48, 57));
    private static readonly SolidColorBrush ActiveTabText = new(Color.FromRgb(0, 229, 255));
    private static readonly SolidColorBrush InactiveTabText = new(Color.FromRgb(144, 164, 174));

    // Preset button active/inactive states
    private static readonly SolidColorBrush ModeActiveBg = new(Color.FromRgb(27, 45, 60));
    private static readonly SolidColorBrush ModeInactiveBg = new(Color.FromRgb(30, 32, 38));
    private static readonly SolidColorBrush ModeActiveBorder = new(Color.FromRgb(0, 229, 255));
    private static readonly SolidColorBrush ModeInactiveBorder = new(Color.FromRgb(50, 54, 66));
    private static readonly SolidColorBrush ModeActiveFg = new(Color.FromRgb(0, 229, 255));
    private static readonly SolidColorBrush ModeInactiveFg = new(Color.FromRgb(236, 239, 241));

    // Turbo button active/inactive states
    private static readonly SolidColorBrush TurboActiveBg = new(Color.FromRgb(229, 57, 53));
    private static readonly SolidColorBrush TurboInactiveBg = new(Color.FromRgb(45, 25, 25));
    private static readonly SolidColorBrush TurboActiveBorder = new(Color.FromRgb(255, 82, 82));
    private static readonly SolidColorBrush TurboInactiveBorder = new(Color.FromRgb(85, 35, 35));
    private static readonly SolidColorBrush TurboActiveFg = new(Color.FromRgb(255, 255, 255));
    private static readonly SolidColorBrush TurboInactiveFg = new(Color.FromRgb(239, 154, 154));

    static FanViewModel()
    {
        ActiveTabBg.Freeze();
        InactiveTabBg.Freeze();
        ActiveTabBorder.Freeze();
        InactiveTabBorder.Freeze();
        ActiveTabText.Freeze();
        InactiveTabText.Freeze();

        ModeActiveBg.Freeze();
        ModeInactiveBg.Freeze();
        ModeActiveBorder.Freeze();
        ModeInactiveBorder.Freeze();
        ModeActiveFg.Freeze();
        ModeInactiveFg.Freeze();

        TurboActiveBg.Freeze();
        TurboInactiveBg.Freeze();
        TurboActiveBorder.Freeze();
        TurboInactiveBorder.Freeze();
        TurboActiveFg.Freeze();
        TurboInactiveFg.Freeze();
    }

    private readonly DispatcherTimer _pollTimer;
    private readonly FanCurveController _cpuCurveController = new();
    private readonly FanCurveController _gpuCurveController = new();
    private readonly AppSettings _settings;

    private double _cpuTemp = 45;
    private double _gpuTemp = 40;
    private uint _cpuFanRpm;
    private uint _gpuFanRpm;

    private int _cpuFanSpeed = 50;
    private int _gpuFanSpeed = 50;
    private bool _cpuFanAuto = true;
    private bool _gpuFanAuto = true;
    private bool _linkFans;
    private bool _coolBoost;
    private bool _turboActive;

    private string _activeMode = "Balanced"; // Auto, Silent, Balanced, Turbo, Curve, Custom
    private string _lastNonTurboMode = "Balanced";
    private bool _syncFanCurves;
    private string _cpuCurvePreset = "Balanced";
    private string _gpuCurvePreset = "Balanced";
    private int _selectedCurveTab = 0; // 0 = CPU, 1 = GPU
    private int _cpuCurveTargetSpeed = 50;
    private int _gpuCurveTargetSpeed = 50;

    private bool _overheatProtection = true;
    private int _overheatThreshold = 88;
    private bool _isOverheating;

    private bool _startWithWindows;
    private bool _startMinimized;
    private bool _closeToTray = true;

    private bool _isSettingsOpen;
    private bool _isCurveEditorOpen;
    private bool _isSensorsDrawerOpen;
    private bool _isTuningOpen;
    private bool _isMonitorOpen;
    private bool _isOsdOpen;
    private bool _isGameProfilesOpen;
    private bool _isCalibrationOpen;
    private bool _isCpuTuningOpen;
    private string _statusText = "Initializing...";
    private string _preGameMode = "Balanced";
    private bool _preGameOsd;

    // Advanced Features: Collections
    public ObservableCollection<AppProfileRule> AppProfileRules { get; } = new();

    // MSI Afterburner Suite: GPU Clock & Voltage Tuning Fields
    private int _coreClockOffset;
    private int _memoryClockOffset;
    private int _powerLimitPercent = 100;
    private int _tempLimitCelsius = 83;
    private int _voltageOffsetMv;
    private bool _applyTuningAtStartup;
    private string _tuningStatusText = "Stock Clocks";
    private int _activeTuningSlot = 1;
    private string _gpuClocksLiveText = "-- MHz Core | -- MHz Mem";

    private string _gpuName = "NVIDIA GeForce GTX 1650";
    private uint _gpuUtilization;
    private double _gpuPowerWatts;
    private bool _gpuIsSleeping;

    private bool _cpuDragging;
    private bool _gpuDragging;

    public ObservableCollection<CurvePoint> CpuCurvePoints { get; } = new();
    public ObservableCollection<CurvePoint> GpuCurvePoints { get; } = new();
    public ObservableCollection<CurvePoint> ActiveCurvePoints => SelectedCurveTab == 0 ? CpuCurvePoints : GpuCurvePoints;

    public ObservableCollection<SensorItem> AllSensors { get; } = new();
    public ObservableCollection<GpuDeviceInfo> DetectedGpus { get; } = new();

    // MSI Afterburner Suite: 11 Rolling Telemetry Monitoring Series
    public TelemetrySeries SeriesGpuTemp { get; } = new("GPU Temperature", "°C", "#FFD600", 60, 220, 60, 20, 100);
    public TelemetrySeries SeriesGpuCoreClock { get; } = new("GPU Core Clock", " MHz", "#00E5FF", 60, 220, 60, 300, 2200);
    public TelemetrySeries SeriesGpuMemoryClock { get; } = new("GPU Memory Clock", " MHz", "#7C4DFF", 60, 220, 60, 400, 8000);
    public TelemetrySeries SeriesGpuCoreLoad { get; } = new("GPU Core Load", "%", "#00E676", 60, 220, 60, 0, 100);
    public TelemetrySeries SeriesGpuPower { get; } = new("GPU Power Draw", "W", "#FF9100", 60, 220, 60, 0, 150);
    public TelemetrySeries SeriesGpuVramUsage { get; } = new("GPU VRAM Usage", " MB", "#E040FB", 60, 220, 60, 0, 8192);
    public TelemetrySeries SeriesGpuFanRpm { get; } = new("GPU Fan Tachometer", " RPM", "#40C4FF", 60, 220, 60, 0, 6000);
    public TelemetrySeries SeriesCpuTemp { get; } = new("CPU Temperature", "°C", "#FF5252", 60, 220, 60, 20, 105);
    public TelemetrySeries SeriesCpuPackageLoad { get; } = new("CPU Package Load", "%", "#69F0AE", 60, 220, 60, 0, 100);
    public TelemetrySeries SeriesCpuPackagePower { get; } = new("CPU Package Power", "W", "#FFD740", 60, 220, 60, 0, 120);
    public TelemetrySeries SeriesRamUsage { get; } = new("System RAM Usage", "%", "#B0BEC5", 60, 220, 60, 0, 100);

    public ObservableCollection<TelemetrySeries> TelemetrySeriesList { get; } = new();

    private GpuDeviceInfo? _selectedGpu;
    public GpuDeviceInfo? SelectedGpu
    {
        get => _selectedGpu;
        set
        {
            if (_selectedGpu != value)
            {
                _selectedGpu = value;
                OnPropertyChanged();
                if (value != null)
                {
                    GpuManager.Instance.SetPreferredGpu(value.Id);
                    GpuName = value.Name;
                    _settings.PreferredGpuId = value.Id;
                    ConfigManager.Save(_settings);
                }
            }
        }
    }

    public string PlatformSummaryText => FanHardwareManager.Instance.PlatformSummary;

    public string WindowTitle
    {
        get
        {
            var p = PlatformDetector.GetProfile();
            string name = "";
            if (!string.IsNullOrWhiteSpace(p.Model) && p.Model != "Unknown")
            {
                var mfg = (!string.IsNullOrWhiteSpace(p.Manufacturer) && p.Manufacturer != "Unknown" && !p.Model.Contains(p.Manufacturer, StringComparison.OrdinalIgnoreCase))
                    ? $"{p.Manufacturer} "
                    : "";
                name = $"{mfg}{p.Model}";
            }
            else if (!string.IsNullOrWhiteSpace(p.MotherboardModel) && p.MotherboardModel != "Unknown")
            {
                name = p.MotherboardModel;
            }

            return !string.IsNullOrEmpty(name)
                ? $"DynoFanControl — {name}"
                : "DynoFanControl — Universal Fan Control";
        }
    }

    public string CpuModelName
    {
        get
        {
            var p = PlatformDetector.GetProfile();
            return !string.IsNullOrEmpty(p.CpuName) ? p.CpuName : "AMD / Intel Processor";
        }
    }

    public event Action<string>? TooltipUpdateRequested;

    public FanViewModel()
    {
        // 1. Load persisted settings
        _settings = ConfigManager.Load();
        _linkFans = _settings.LinkFans;
        _coolBoost = _settings.CoolBoost;
        _overheatProtection = _settings.OverheatProtection;
        _overheatThreshold = _settings.OverheatThreshold;
        _startWithWindows = _settings.StartWithWindows;
        _startMinimized = _settings.StartMinimized;
        _closeToTray = _settings.CloseToTray;
        _activeMode = _settings.ActivePreset ?? "Balanced";
        _syncFanCurves = _settings.SyncFanCurves;
        _cpuCurvePreset = _settings.CpuCurvePreset ?? "Balanced";
        _gpuCurvePreset = _settings.GpuCurvePreset ?? "Balanced";
        _cpuFanSpeed = _settings.CpuManualSpeed;
        _gpuFanSpeed = _settings.GpuManualSpeed;

        if (_activeMode != "Turbo") _lastNonTurboMode = _activeMode;

        // Automatically enforce Windows Startup registration if requested
        if (_startWithWindows && !StartupManager.IsStartupEnabled())
        {
            StartupManager.SetStartup(true, _startMinimized);
        }

        if (!string.IsNullOrEmpty(_settings.PreferredGpuId))
        {
            GpuManager.Instance.SetPreferredGpu(_settings.PreferredGpuId);
        }

        // Initialize curves from presets or custom points
        LoadCpuCurvePreset(_cpuCurvePreset);
        LoadGpuCurvePreset(_gpuCurvePreset);

        // Populate Telemetry Series list for XAML binding
        TelemetrySeriesList.Add(SeriesGpuTemp);
        TelemetrySeriesList.Add(SeriesGpuCoreClock);
        TelemetrySeriesList.Add(SeriesGpuMemoryClock);
        TelemetrySeriesList.Add(SeriesGpuCoreLoad);
        TelemetrySeriesList.Add(SeriesGpuPower);
        TelemetrySeriesList.Add(SeriesGpuVramUsage);
        TelemetrySeriesList.Add(SeriesGpuFanRpm);
        TelemetrySeriesList.Add(SeriesCpuTemp);
        TelemetrySeriesList.Add(SeriesCpuPackageLoad);
        TelemetrySeriesList.Add(SeriesCpuPackagePower);
        TelemetrySeriesList.Add(SeriesRamUsage);

        // Initialize GPU Tuning Subsystem (MSI Afterburner Engine)
        _applyTuningAtStartup = _settings.ApplyTuningAtStartup;
        if (_settings.CurrentTuningProfile != null)
        {
            _coreClockOffset = _settings.CurrentTuningProfile.CoreClockOffsetMhz;
            _memoryClockOffset = _settings.CurrentTuningProfile.MemoryClockOffsetMhz;
            _powerLimitPercent = _settings.CurrentTuningProfile.PowerLimitPercent;
            _tempLimitCelsius = _settings.CurrentTuningProfile.TempLimitCelsius;
            _voltageOffsetMv = _settings.CurrentTuningProfile.VoltageOffsetMv;
        }

        GpuTuningManager.Instance.Initialize();
        if (_applyTuningAtStartup && _settings.CurrentTuningProfile != null)
        {
            try
            {
                GpuTuningManager.Instance.ApplyProfile(_settings.CurrentTuningProfile);
                _tuningStatusText = GpuTuningManager.Instance.LastStatus;
            }
            catch { }
        }

        // Initialize Per-Game Automation Rules
        foreach (var r in _settings.AppProfileRules)
            AppProfileRules.Add(r);

        ProcessWatcherService.Instance.RuleActivated += OnGameRuleActivated;
        ProcessWatcherService.Instance.RuleDeactivated += OnGameRuleDeactivated;

        // Initialize Battery-Aware Power Listener
        BatteryMonitorService.Instance.PowerStateChanged += OnPowerStateChanged;

        // Initialize CPU Thermal & Power Tuning
        CpuPowerTuningService.Instance.Initialize();
        if (_settings.EnableCpuThermalClamping)
        {
            CpuPowerTuningService.Instance.ApplyLimits(_settings.CpuMaxTempTarget, _settings.CpuPowerLimitWatts);
        }

        // Initialize Calibration Listeners
        FanCalibrationService.Instance.ProgressUpdated += (pct, msg) =>
        {
            OnPropertyChanged(nameof(CalibrationProgress));
            OnPropertyChanged(nameof(CalibrationStatus));
        };
        FanCalibrationService.Instance.CalibrationCompleted += (ok, msg) =>
        {
            OnPropertyChanged(nameof(IsCalibrating));
            OnPropertyChanged(nameof(CalibrationStatus));
        };

        // 2. Initialize hardware
        FanHardwareManager.Instance.Initialize();

        // 3. Setup hardware poll timer
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(1, _settings.PollIntervalSeconds)) };
        _pollTimer.Tick += OnPollTick;
        _pollTimer.Start();

        // 4. Initial poll & apply saved state
        try
        {
            PollHardware();
            ApplyInitialState();
        }
        catch
        {
            // Initial poll error suppressed; timer will retry
        }
    }

    #region Properties

    public double CpuTemp
    {
        get => _cpuTemp;
        private set { if (Math.Abs(_cpuTemp - value) > 0.05) { _cpuTemp = value; OnPropertyChanged(); OnPropertyChanged(nameof(CpuTempText)); OnPropertyChanged(nameof(PeakTemp)); } }
    }

    public double GpuTemp
    {
        get => _gpuTemp;
        private set { if (Math.Abs(_gpuTemp - value) > 0.05) { _gpuTemp = value; OnPropertyChanged(); OnPropertyChanged(nameof(GpuTempText)); OnPropertyChanged(nameof(PeakTemp)); } }
    }

    public double PeakTemp => Math.Max(CpuTemp, GpuTemp);

    public string CpuTempText => CpuTemp > 0 ? $"{CpuTemp:F1}°C" : "--.-°C";
    public string GpuTempText => GpuTemp > 0 ? $"{GpuTemp:F1}°C" : "--.-°C";

    public uint CpuFanRpm
    {
        get => _cpuFanRpm;
        private set { if (_cpuFanRpm != value) { _cpuFanRpm = value; OnPropertyChanged(); OnPropertyChanged(nameof(CpuFanRpmText)); } }
    }

    public uint GpuFanRpm
    {
        get => _gpuFanRpm;
        private set { if (_gpuFanRpm != value) { _gpuFanRpm = value; OnPropertyChanged(); OnPropertyChanged(nameof(GpuFanRpmText)); } }
    }

    public string CpuFanRpmText => CpuFanRpm > 0 ? $"{CpuFanRpm:N0} RPM" : "0 RPM";
    public string GpuFanRpmText => GpuFanRpm > 0 ? $"{GpuFanRpm:N0} RPM" : "0 RPM";

    public int CpuFanSpeed
    {
        get => _cpuFanSpeed;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (_cpuFanSpeed != clamped)
            {
                _cpuFanSpeed = clamped;
                OnPropertyChanged();
                if (!_cpuDragging && !_cpuFanAuto)
                    ApplyCpuSpeed();

                if (LinkFans && !_gpuDragging)
                {
                    _gpuFanSpeed = clamped;
                    OnPropertyChanged(nameof(GpuFanSpeed));
                    if (!_gpuFanAuto) ApplyGpuSpeed();
                }
            }
        }
    }

    public int GpuFanSpeed
    {
        get => _gpuFanSpeed;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (_gpuFanSpeed != clamped)
            {
                _gpuFanSpeed = clamped;
                OnPropertyChanged();
                if (!_gpuDragging && !_gpuFanAuto)
                    ApplyGpuSpeed();

                if (LinkFans && !_cpuDragging)
                {
                    _cpuFanSpeed = clamped;
                    OnPropertyChanged(nameof(CpuFanSpeed));
                    if (!_cpuFanAuto) ApplyCpuSpeed();
                }
            }
        }
    }

    public bool CpuFanAuto
    {
        get => _cpuFanAuto;
        set
        {
            if (_cpuFanAuto != value)
            {
                _cpuFanAuto = value;
                OnPropertyChanged();
                ApplyCpuSpeed();
                UpdateActiveModeFromFans();
            }
        }
    }

    public bool GpuFanAuto
    {
        get => _gpuFanAuto;
        set
        {
            if (_gpuFanAuto != value)
            {
                _gpuFanAuto = value;
                OnPropertyChanged();
                ApplyGpuSpeed();
                UpdateActiveModeFromFans();
            }
        }
    }

    public bool LinkFans
    {
        get => _linkFans;
        set
        {
            if (_linkFans != value)
            {
                _linkFans = value;
                OnPropertyChanged();
                _settings.LinkFans = value;
                ConfigManager.Save(_settings);

                if (value)
                {
                    GpuFanSpeed = CpuFanSpeed;
                    GpuFanAuto = CpuFanAuto;
                }
            }
        }
    }

    public bool CoolBoost
    {
        get => _coolBoost;
        set
        {
            if (_coolBoost != value)
            {
                _coolBoost = value;
                OnPropertyChanged();
                FanHardwareManager.Instance.SetCoolBoost(value);
                _settings.CoolBoost = value;
                ConfigManager.Save(_settings);
            }
        }
    }

    public bool TurboActive
    {
        get => _turboActive;
        private set { if (_turboActive != value) { _turboActive = value; OnPropertyChanged(); } }
    }

    public string ActiveMode
    {
        get => _activeMode;
        set
        {
            if (_activeMode != value)
            {
                _activeMode = value;
                OnPropertyChanged();
                NotifyPresetVisuals();
                _settings.ActivePreset = value;
                ConfigManager.Save(_settings);
            }
        }
    }

    public void NotifyPresetVisuals()
    {
        OnPropertyChanged(nameof(IsAutoActive));
        OnPropertyChanged(nameof(IsSilentActive));
        OnPropertyChanged(nameof(IsBalancedActive));
        OnPropertyChanged(nameof(IsCurveActive));
        OnPropertyChanged(nameof(IsCustomActive));
        OnPropertyChanged(nameof(IsTurboActive));

        OnPropertyChanged(nameof(AutoBtnBackground));
        OnPropertyChanged(nameof(AutoBtnBorder));
        OnPropertyChanged(nameof(AutoBtnForeground));
        OnPropertyChanged(nameof(AutoBtnFontWeight));

        OnPropertyChanged(nameof(SilentBtnBackground));
        OnPropertyChanged(nameof(SilentBtnBorder));
        OnPropertyChanged(nameof(SilentBtnForeground));
        OnPropertyChanged(nameof(SilentBtnFontWeight));

        OnPropertyChanged(nameof(BalancedBtnBackground));
        OnPropertyChanged(nameof(BalancedBtnBorder));
        OnPropertyChanged(nameof(BalancedBtnForeground));
        OnPropertyChanged(nameof(BalancedBtnFontWeight));

        OnPropertyChanged(nameof(CurveBtnBackground));
        OnPropertyChanged(nameof(CurveBtnBorder));
        OnPropertyChanged(nameof(CurveBtnForeground));
        OnPropertyChanged(nameof(CurveBtnFontWeight));

        OnPropertyChanged(nameof(TurboBtnBackground));
        OnPropertyChanged(nameof(TurboBtnBorder));
        OnPropertyChanged(nameof(TurboBtnForeground));

        OnPropertyChanged(nameof(ActivePresetBadgeText));
    }

    public bool IsAutoActive
    {
        get => ActiveMode == "Auto";
        set { if (value) SetMode("Auto"); }
    }

    public bool IsSilentActive
    {
        get => ActiveMode == "Silent";
        set { if (value) SetMode("Silent"); }
    }

    public bool IsBalancedActive
    {
        get => ActiveMode == "Balanced";
        set { if (value) SetMode("Balanced"); }
    }

    public bool IsCurveActive
    {
        get => ActiveMode == "Curve";
        set { if (value) SetMode("Curve"); }
    }

    public bool IsCustomActive
    {
        get => ActiveMode == "Custom";
        set { if (value) SetMode("Custom"); }
    }

    public bool IsTurboActive
    {
        get => TurboActive || ActiveMode == "Turbo";
        set
        {
            if (value) SetMode("Turbo");
            else if (ActiveMode == "Turbo") ToggleTurbo();
        }
    }

    public Brush AutoBtnBackground => IsAutoActive ? ModeActiveBg : ModeInactiveBg;
    public Brush AutoBtnBorder => IsAutoActive ? ModeActiveBorder : ModeInactiveBorder;
    public Brush AutoBtnForeground => IsAutoActive ? ModeActiveFg : ModeInactiveFg;
    public FontWeight AutoBtnFontWeight => IsAutoActive ? FontWeights.Bold : FontWeights.SemiBold;

    public Brush SilentBtnBackground => IsSilentActive ? ModeActiveBg : ModeInactiveBg;
    public Brush SilentBtnBorder => IsSilentActive ? ModeActiveBorder : ModeInactiveBorder;
    public Brush SilentBtnForeground => IsSilentActive ? ModeActiveFg : ModeInactiveFg;
    public FontWeight SilentBtnFontWeight => IsSilentActive ? FontWeights.Bold : FontWeights.SemiBold;

    public Brush BalancedBtnBackground => IsBalancedActive ? ModeActiveBg : ModeInactiveBg;
    public Brush BalancedBtnBorder => IsBalancedActive ? ModeActiveBorder : ModeInactiveBorder;
    public Brush BalancedBtnForeground => IsBalancedActive ? ModeActiveFg : ModeInactiveFg;
    public FontWeight BalancedBtnFontWeight => IsBalancedActive ? FontWeights.Bold : FontWeights.SemiBold;

    public Brush CurveBtnBackground => IsCurveActive ? ModeActiveBg : ModeInactiveBg;
    public Brush CurveBtnBorder => IsCurveActive ? ModeActiveBorder : ModeInactiveBorder;
    public Brush CurveBtnForeground => IsCurveActive ? ModeActiveFg : ModeInactiveFg;
    public FontWeight CurveBtnFontWeight => IsCurveActive ? FontWeights.Bold : FontWeights.SemiBold;

    public Brush TurboBtnBackground => IsTurboActive ? TurboActiveBg : TurboInactiveBg;
    public Brush TurboBtnBorder => IsTurboActive ? TurboActiveBorder : TurboInactiveBorder;
    public Brush TurboBtnForeground => IsTurboActive ? TurboActiveFg : TurboInactiveFg;

    public string ActivePresetBadgeText => ActiveMode switch
    {
        "Auto" => "⚙ BIOS AUTO",
        "Silent" => "🌙 SILENT",
        "Balanced" => "⚡ BALANCED",
        "Turbo" => "🔥 MAX TURBO",
        "Curve" => $"📈 CURVE ({CurrentCurvePreset})",
        "Custom" => $"🛠 CUSTOM ({CpuFanSpeed}%/{GpuFanSpeed}%)",
        _ => ActiveMode.ToUpperInvariant()
    };

    public Brush CurveSilentBtnBg => CurrentCurvePreset == "Silent" ? ModeActiveBg : InactiveTabBg;
    public Brush CurveSilentBtnBorder => CurrentCurvePreset == "Silent" ? ModeActiveBorder : InactiveTabBorder;
    public Brush CurveSilentBtnFg => CurrentCurvePreset == "Silent" ? ModeActiveFg : InactiveTabText;

    public Brush CurveBalancedBtnBg => CurrentCurvePreset == "Balanced" ? ModeActiveBg : InactiveTabBg;
    public Brush CurveBalancedBtnBorder => CurrentCurvePreset == "Balanced" ? ModeActiveBorder : InactiveTabBorder;
    public Brush CurveBalancedBtnFg => CurrentCurvePreset == "Balanced" ? ModeActiveFg : InactiveTabText;

    public Brush CurveGamingBtnBg => CurrentCurvePreset == "Gaming" ? ModeActiveBg : InactiveTabBg;
    public Brush CurveGamingBtnBorder => CurrentCurvePreset == "Gaming" ? ModeActiveBorder : InactiveTabBorder;
    public Brush CurveGamingBtnFg => CurrentCurvePreset == "Gaming" ? ModeActiveFg : InactiveTabText;

    public void NotifyCurveProfileVisuals()
    {
        OnPropertyChanged(nameof(CurrentCurvePreset));
        OnPropertyChanged(nameof(ActiveCurvePreset));
        OnPropertyChanged(nameof(ActivePresetBadgeText));
        OnPropertyChanged(nameof(CurveSilentBtnBg));
        OnPropertyChanged(nameof(CurveSilentBtnBorder));
        OnPropertyChanged(nameof(CurveSilentBtnFg));
        OnPropertyChanged(nameof(CurveBalancedBtnBg));
        OnPropertyChanged(nameof(CurveBalancedBtnBorder));
        OnPropertyChanged(nameof(CurveBalancedBtnFg));
        OnPropertyChanged(nameof(CurveGamingBtnBg));
        OnPropertyChanged(nameof(CurveGamingBtnBorder));
        OnPropertyChanged(nameof(CurveGamingBtnFg));
    }

    public bool SyncFanCurves
    {
        get => _syncFanCurves;
        set
        {
            if (_syncFanCurves != value)
            {
                _syncFanCurves = value;
                OnPropertyChanged();
                _settings.SyncFanCurves = value;
                ConfigManager.Save(_settings);

                if (value)
                {
                    GpuCurvePreset = CpuCurvePreset;
                    LoadGpuCurvePreset(GpuCurvePreset);
                }
                if (ActiveMode == "Curve")
                    EvaluateCurveMode();
            }
        }
    }

    public string CpuCurvePreset
    {
        get => _cpuCurvePreset;
        set
        {
            if (_cpuCurvePreset != value)
            {
                _cpuCurvePreset = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentCurvePreset));
                LoadCpuCurvePreset(value);
                _settings.CpuCurvePreset = value;
                ConfigManager.Save(_settings);

                if (SyncFanCurves && _gpuCurvePreset != value)
                {
                    _gpuCurvePreset = value;
                    OnPropertyChanged(nameof(GpuCurvePreset));
                    LoadGpuCurvePreset(value);
                    _settings.GpuCurvePreset = value;
                    ConfigManager.Save(_settings);
                }

                if (ActiveMode == "Curve") EvaluateCurveMode();
                NotifyCurveProfileVisuals();
            }
        }
    }

    public string GpuCurvePreset
    {
        get => _gpuCurvePreset;
        set
        {
            if (_gpuCurvePreset != value)
            {
                _gpuCurvePreset = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentCurvePreset));
                LoadGpuCurvePreset(value);
                _settings.GpuCurvePreset = value;
                ConfigManager.Save(_settings);

                if (SyncFanCurves && _cpuCurvePreset != value)
                {
                    _cpuCurvePreset = value;
                    OnPropertyChanged(nameof(CpuCurvePreset));
                    LoadCpuCurvePreset(value);
                    _settings.CpuCurvePreset = value;
                    ConfigManager.Save(_settings);
                }

                if (ActiveMode == "Curve") EvaluateCurveMode();
                NotifyCurveProfileVisuals();
            }
        }
    }

    public string ActiveCurvePreset
    {
        get => CurrentCurvePreset;
        set => SetCurvePreset(value);
    }

    public int SelectedCurveTab
    {
        get => _selectedCurveTab;
        set
        {
            if (_selectedCurveTab != value)
            {
                _selectedCurveTab = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCpuCurveTabSelected));
                OnPropertyChanged(nameof(IsGpuCurveTabSelected));
                OnPropertyChanged(nameof(ActiveCurvePoints));
                OnPropertyChanged(nameof(CurrentCurveTabTitle));
                OnPropertyChanged(nameof(CurrentCurvePreset));
                OnPropertyChanged(nameof(CurrentCurveLiveText));
                OnPropertyChanged(nameof(CpuTabBackground));
                OnPropertyChanged(nameof(CpuTabBorder));
                OnPropertyChanged(nameof(CpuTabForeground));
                OnPropertyChanged(nameof(GpuTabBackground));
                OnPropertyChanged(nameof(GpuTabBorder));
                OnPropertyChanged(nameof(GpuTabForeground));
                NotifyCurveProfileVisuals();
            }
        }
    }

    public bool IsCpuCurveTabSelected => SelectedCurveTab == 0;
    public bool IsGpuCurveTabSelected => SelectedCurveTab == 1;

    public Brush CpuTabBackground => SelectedCurveTab == 0 ? ActiveTabBg : InactiveTabBg;
    public Brush CpuTabBorder => SelectedCurveTab == 0 ? ActiveTabBorder : InactiveTabBorder;
    public Brush CpuTabForeground => SelectedCurveTab == 0 ? ActiveTabText : InactiveTabText;

    public Brush GpuTabBackground => SelectedCurveTab == 1 ? ActiveTabBg : InactiveTabBg;
    public Brush GpuTabBorder => SelectedCurveTab == 1 ? ActiveTabBorder : InactiveTabBorder;
    public Brush GpuTabForeground => SelectedCurveTab == 1 ? ActiveTabText : InactiveTabText;

    public string CurrentCurvePreset => SelectedCurveTab == 0 ? CpuCurvePreset : GpuCurvePreset;
    public string CurrentCurveTabTitle => SelectedCurveTab == 0 ? "CPU Fan Curve (Ryzen 5 5600H)" : "GPU Fan Curve (GTX 1650)";

    public string CpuCurveLiveText => $"{CpuTempText} ➔ {_cpuCurveTargetSpeed}%";
    public string GpuCurveLiveText => $"{GpuTempText} ➔ {_gpuCurveTargetSpeed}%";
    public string CurrentCurveLiveText => SelectedCurveTab == 0 ? CpuCurveLiveText : GpuCurveLiveText;

    public bool OverheatProtection
    {
        get => _overheatProtection;
        set
        {
            if (_overheatProtection != value)
            {
                _overheatProtection = value;
                OnPropertyChanged();
                _settings.OverheatProtection = value;
                ConfigManager.Save(_settings);
            }
        }
    }

    public int OverheatThreshold
    {
        get => _overheatThreshold;
        set
        {
            var clamped = Math.Clamp(value, 75, 95);
            if (_overheatThreshold != clamped)
            {
                _overheatThreshold = clamped;
                OnPropertyChanged();
                _settings.OverheatThreshold = clamped;
                ConfigManager.Save(_settings);
            }
        }
    }

    public bool IsOverheating
    {
        get => _isOverheating;
        private set { if (_isOverheating != value) { _isOverheating = value; OnPropertyChanged(); } }
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (_startWithWindows != value)
            {
                _startWithWindows = value;
                OnPropertyChanged();
                StartupManager.SetStartup(value, StartMinimized);
                _settings.StartWithWindows = value;
                ConfigManager.Save(_settings);
            }
        }
    }

    public bool StartMinimized
    {
        get => _startMinimized;
        set
        {
            if (_startMinimized != value)
            {
                _startMinimized = value;
                OnPropertyChanged();
                _settings.StartMinimized = value;
                ConfigManager.Save(_settings);
                if (StartWithWindows)
                    StartupManager.SetStartup(true, value);
            }
        }
    }

    public bool CloseToTray
    {
        get => _closeToTray;
        set
        {
            if (_closeToTray != value)
            {
                _closeToTray = value;
                OnPropertyChanged();
                _settings.CloseToTray = value;
                ConfigManager.Save(_settings);
            }
        }
    }

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set { if (_isSettingsOpen != value) { _isSettingsOpen = value; OnPropertyChanged(); } }
    }

    public bool IsCurveEditorOpen
    {
        get => _isCurveEditorOpen;
        set { if (_isCurveEditorOpen != value) { _isCurveEditorOpen = value; OnPropertyChanged(); } }
    }

    public bool IsSensorsDrawerOpen
    {
        get => _isSensorsDrawerOpen;
        set { if (_isSensorsDrawerOpen != value) { _isSensorsDrawerOpen = value; OnPropertyChanged(); } }
    }

    public bool IsTuningOpen
    {
        get => _isTuningOpen;
        set { if (_isTuningOpen != value) { _isTuningOpen = value; OnPropertyChanged(); } }
    }

    public bool IsMonitorOpen
    {
        get => _isMonitorOpen;
        set { if (_isMonitorOpen != value) { _isMonitorOpen = value; OnPropertyChanged(); } }
    }

    public bool IsOsdOpen
    {
        get => _isOsdOpen;
        set { if (_isOsdOpen != value) { _isOsdOpen = value; OnPropertyChanged(); } }
    }

    public bool IsGameProfilesOpen
    {
        get => _isGameProfilesOpen;
        set { if (_isGameProfilesOpen != value) { _isGameProfilesOpen = value; OnPropertyChanged(); } }
    }

    public bool IsCalibrationOpen
    {
        get => _isCalibrationOpen;
        set { if (_isCalibrationOpen != value) { _isCalibrationOpen = value; OnPropertyChanged(); } }
    }

    public bool IsCpuTuningOpen
    {
        get => _isCpuTuningOpen;
        set { if (_isCpuTuningOpen != value) { _isCpuTuningOpen = value; OnPropertyChanged(); } }
    }

    // Feature A: Per-Game Profiles
    public bool EnableAppProfiles
    {
        get => _settings.EnableAppProfiles;
        set
        {
            if (_settings.EnableAppProfiles != value)
            {
                _settings.EnableAppProfiles = value;
                OnPropertyChanged();
                _settings.EnableAppProfiles = value;
                ConfigManager.Save(_settings);
            }
        }
    }

    public string ActiveGameRuleText => ProcessWatcherService.Instance.IsInGameMode && ProcessWatcherService.Instance.ActiveMatchedRule != null
        ? $"🎮 Game Active: {ProcessWatcherService.Instance.ActiveMatchedRule.DisplayName}"
        : "Desktop / Standard";

    // Feature D: Battery-Aware Switching
    public bool EnableBatterySwitching
    {
        get => _settings.EnableBatterySwitching;
        set
        {
            if (_settings.EnableBatterySwitching != value)
            {
                _settings.EnableBatterySwitching = value;
                OnPropertyChanged();
                ConfigManager.Save(_settings);
            }
        }
    }

    public string BatteryMode
    {
        get => _settings.BatteryMode;
        set
        {
            if (_settings.BatteryMode != value)
            {
                _settings.BatteryMode = value;
                OnPropertyChanged();
                ConfigManager.Save(_settings);
            }
        }
    }

    public string AcMode
    {
        get => _settings.AcMode;
        set
        {
            if (_settings.AcMode != value)
            {
                _settings.AcMode = value;
                OnPropertyChanged();
                ConfigManager.Save(_settings);
            }
        }
    }

    public string PowerSourceStatusText => BatteryMonitorService.Instance.PowerSourceText;

    // Feature C: CPU Thermal & Power Clamping
    public bool EnableCpuThermalClamping
    {
        get => _settings.EnableCpuThermalClamping;
        set
        {
            if (_settings.EnableCpuThermalClamping != value)
            {
                _settings.EnableCpuThermalClamping = value;
                OnPropertyChanged();
                _settings.EnableCpuThermalClamping = value;
                ConfigManager.Save(_settings);

                if (value)
                    ApplyCpuClamping();
                else
                    ResetCpuClamping();
            }
        }
    }

    public int CpuMaxTempTarget
    {
        get => _settings.CpuMaxTempTarget;
        set
        {
            var clamped = Math.Clamp(value, 65, 95);
            if (_settings.CpuMaxTempTarget != clamped)
            {
                _settings.CpuMaxTempTarget = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CpuMaxTempTargetText));
                ConfigManager.Save(_settings);
                if (EnableCpuThermalClamping) ApplyCpuClamping();
            }
        }
    }
    public string CpuMaxTempTargetText => $"{CpuMaxTempTarget}°C";

    public int CpuPowerLimitWatts
    {
        get => _settings.CpuPowerLimitWatts;
        set
        {
            var clamped = Math.Clamp(value, 15, 95);
            if (_settings.CpuPowerLimitWatts != clamped)
            {
                _settings.CpuPowerLimitWatts = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CpuPowerLimitWattsText));
                ConfigManager.Save(_settings);
                if (EnableCpuThermalClamping) ApplyCpuClamping();
            }
        }
    }
    public string CpuPowerLimitWattsText => $"{CpuPowerLimitWatts}W";

    public string CpuClampingStatusText => CpuPowerTuningService.Instance.StatusMessage;

    // Feature F: Fan Acoustics & Calibration
    public bool IsCalibrating => FanCalibrationService.Instance.IsRunning;
    public int CalibrationProgress => FanCalibrationService.Instance.CurrentProgressPercent;
    public string CalibrationStatus => FanCalibrationService.Instance.CurrentStatus;
    public ObservableCollection<CalibrationResultPoint> CalibrationResults => FanCalibrationService.Instance.Results;

    // MSI Afterburner Suite: GPU Tuning Properties
    public int CoreClockOffset
    {
        get => _coreClockOffset;
        set { if (_coreClockOffset != value) { _coreClockOffset = value; OnPropertyChanged(); OnPropertyChanged(nameof(CoreClockOffsetText)); } }
    }
    public string CoreClockOffsetText => CoreClockOffset >= 0 ? $"+{CoreClockOffset} MHz" : $"{CoreClockOffset} MHz";

    public int MemoryClockOffset
    {
        get => _memoryClockOffset;
        set { if (_memoryClockOffset != value) { _memoryClockOffset = value; OnPropertyChanged(); OnPropertyChanged(nameof(MemoryClockOffsetText)); } }
    }
    public string MemoryClockOffsetText => MemoryClockOffset >= 0 ? $"+{MemoryClockOffset} MHz" : $"{MemoryClockOffset} MHz";

    public int PowerLimitPercent
    {
        get => _powerLimitPercent;
        set { if (_powerLimitPercent != value) { _powerLimitPercent = value; OnPropertyChanged(); OnPropertyChanged(nameof(PowerLimitPercentText)); } }
    }
    public string PowerLimitPercentText => $"{PowerLimitPercent}%";

    public int TempLimitCelsius
    {
        get => _tempLimitCelsius;
        set { if (_tempLimitCelsius != value) { _tempLimitCelsius = value; OnPropertyChanged(); OnPropertyChanged(nameof(TempLimitCelsiusText)); } }
    }
    public string TempLimitCelsiusText => $"{TempLimitCelsius}°C";

    public int VoltageOffsetMv
    {
        get => _voltageOffsetMv;
        set { if (_voltageOffsetMv != value) { _voltageOffsetMv = value; OnPropertyChanged(); OnPropertyChanged(nameof(VoltageOffsetMvText)); } }
    }
    public string VoltageOffsetMvText => VoltageOffsetMv > 0 ? $"+{VoltageOffsetMv} mV" : $"{VoltageOffsetMv} mV";

    public bool ApplyTuningAtStartup
    {
        get => _applyTuningAtStartup;
        set
        {
            if (_applyTuningAtStartup != value)
            {
                _applyTuningAtStartup = value;
                OnPropertyChanged();
                _settings.ApplyTuningAtStartup = value;
                ConfigManager.Save(_settings);
            }
        }
    }

    public string TuningStatusText
    {
        get => _tuningStatusText;
        set { if (_tuningStatusText != value) { _tuningStatusText = value; OnPropertyChanged(); } }
    }

    public int ActiveTuningSlot
    {
        get => _activeTuningSlot;
        set { if (_activeTuningSlot != value) { _activeTuningSlot = value; OnPropertyChanged(); } }
    }

    public string GpuClocksLiveText
    {
        get => _gpuClocksLiveText;
        private set { if (_gpuClocksLiveText != value) { _gpuClocksLiveText = value; OnPropertyChanged(); } }
    }

    // MSI Afterburner Suite: RTSS In-Game OSD Properties
    public bool OsdEnabled
    {
        get => _settings.Osd.Enabled;
        set
        {
            if (_settings.Osd.Enabled != value)
            {
                _settings.Osd.Enabled = value;
                OnPropertyChanged();
                if (!value) RtssOsdService.Instance.ClearOsd();
                ConfigManager.Save(_settings);
            }
        }
    }

    public bool OsdShowGpuTemp
    {
        get => _settings.Osd.ShowGpuTemp;
        set { if (_settings.Osd.ShowGpuTemp != value) { _settings.Osd.ShowGpuTemp = value; OnPropertyChanged(); ConfigManager.Save(_settings); } }
    }
    public bool OsdShowGpuLoad
    {
        get => _settings.Osd.ShowGpuLoad;
        set { if (_settings.Osd.ShowGpuLoad != value) { _settings.Osd.ShowGpuLoad = value; OnPropertyChanged(); ConfigManager.Save(_settings); } }
    }
    public bool OsdShowGpuClocks
    {
        get => _settings.Osd.ShowGpuClocks;
        set { if (_settings.Osd.ShowGpuClocks != value) { _settings.Osd.ShowGpuClocks = value; OnPropertyChanged(); ConfigManager.Save(_settings); } }
    }
    public bool OsdShowGpuPower
    {
        get => _settings.Osd.ShowGpuPower;
        set { if (_settings.Osd.ShowGpuPower != value) { _settings.Osd.ShowGpuPower = value; OnPropertyChanged(); ConfigManager.Save(_settings); } }
    }
    public bool OsdShowGpuVram
    {
        get => _settings.Osd.ShowGpuVram;
        set { if (_settings.Osd.ShowGpuVram != value) { _settings.Osd.ShowGpuVram = value; OnPropertyChanged(); ConfigManager.Save(_settings); } }
    }
    public bool OsdShowGpuFanRpm
    {
        get => _settings.Osd.ShowGpuFanRpm;
        set { if (_settings.Osd.ShowGpuFanRpm != value) { _settings.Osd.ShowGpuFanRpm = value; OnPropertyChanged(); ConfigManager.Save(_settings); } }
    }
    public bool OsdShowCpuTemp
    {
        get => _settings.Osd.ShowCpuTemp;
        set { if (_settings.Osd.ShowCpuTemp != value) { _settings.Osd.ShowCpuTemp = value; OnPropertyChanged(); ConfigManager.Save(_settings); } }
    }
    public bool OsdShowCpuLoad
    {
        get => _settings.Osd.ShowCpuLoad;
        set { if (_settings.Osd.ShowCpuLoad != value) { _settings.Osd.ShowCpuLoad = value; OnPropertyChanged(); ConfigManager.Save(_settings); } }
    }
    public bool OsdShowCpuPower
    {
        get => _settings.Osd.ShowCpuPower;
        set { if (_settings.Osd.ShowCpuPower != value) { _settings.Osd.ShowCpuPower = value; OnPropertyChanged(); ConfigManager.Save(_settings); } }
    }
    public bool OsdShowCpuFanRpm
    {
        get => _settings.Osd.ShowCpuFanRpm;
        set { if (_settings.Osd.ShowCpuFanRpm != value) { _settings.Osd.ShowCpuFanRpm = value; OnPropertyChanged(); ConfigManager.Save(_settings); } }
    }
    public bool OsdShowRamUsage
    {
        get => _settings.Osd.ShowRamUsage;
        set { if (_settings.Osd.ShowRamUsage != value) { _settings.Osd.ShowRamUsage = value; OnPropertyChanged(); ConfigManager.Save(_settings); } }
    }
    public bool OsdShowActivePreset
    {
        get => _settings.Osd.ShowActivePreset;
        set { if (_settings.Osd.ShowActivePreset != value) { _settings.Osd.ShowActivePreset = value; OnPropertyChanged(); ConfigManager.Save(_settings); } }
    }

    private string _rtssStatusText = "⚪ RTSS Not Detected";
    public string RtssStatusText
    {
        get => _rtssStatusText;
        private set { if (_rtssStatusText != value) { _rtssStatusText = value; OnPropertyChanged(); } }
    }

    private string _osdPreviewText = "GPU 45°C  40%  1530MHz  1420MB  45.0W\nCPU 50°C  25%  35.0W  2100RPM\nRAM 8.5GB / 16.0GB  [BALANCED]";
    public string OsdPreviewText
    {
        get => _osdPreviewText;
        private set { if (_osdPreviewText != value) { _osdPreviewText = value; OnPropertyChanged(); } }
    }

    public string GpuName
    {
        get => _gpuName;
        private set { if (_gpuName != value) { _gpuName = value; OnPropertyChanged(); } }
    }

    public uint GpuUtilization
    {
        get => _gpuUtilization;
        private set { if (_gpuUtilization != value) { _gpuUtilization = value; OnPropertyChanged(); OnPropertyChanged(nameof(GpuDetailsText)); } }
    }

    public double GpuPowerWatts
    {
        get => _gpuPowerWatts;
        private set { if (Math.Abs(_gpuPowerWatts - value) > 0.05) { _gpuPowerWatts = value; OnPropertyChanged(); OnPropertyChanged(nameof(GpuDetailsText)); } }
    }

    public bool GpuIsSleeping
    {
        get => _gpuIsSleeping;
        private set { if (_gpuIsSleeping != value) { _gpuIsSleeping = value; OnPropertyChanged(); OnPropertyChanged(nameof(GpuDetailsText)); } }
    }

    public string GpuDetailsText => GpuIsSleeping ? "Power Savings (Sleep)" : $"{GpuUtilization}% Load • {GpuPowerWatts:F1}W";
    public string SensorCountText => $"{AllSensors.Count} Sensors Detected";

    public string BackendStatusText => FanHardwareManager.Instance.BackendStatus;

    public string StatusText
    {
        get => _statusText;
        set { if (_statusText != value) { _statusText = value; OnPropertyChanged(); } }
    }

    #endregion

    #region Methods & Actions

    public void SliderDragStarted(int fanIndex)
    {
        if (fanIndex == 0) _cpuDragging = true;
        else _gpuDragging = true;
    }

    public void SliderDragCompleted(int fanIndex)
    {
        ActiveMode = "Custom";
        if (fanIndex == 0)
        {
            _cpuDragging = false;
            ApplyCpuSpeed();
        }
        else
        {
            _gpuDragging = false;
            ApplyGpuSpeed();
        }
    }

    public void SetMode(string mode)
    {
        if (mode != "Turbo") _lastNonTurboMode = mode;
        ActiveMode = mode;
        TurboActive = mode == "Turbo";

        switch (mode)
        {
            case "Auto":
                _cpuFanAuto = true;
                _gpuFanAuto = true;
                OnPropertyChanged(nameof(CpuFanAuto));
                OnPropertyChanged(nameof(GpuFanAuto));
                FanHardwareManager.Instance.SetCpuFan(true, 0);
                FanHardwareManager.Instance.SetGpuFan(true, 0);
                StatusText = "BIOS Automatic Curve";
                break;

            case "Silent":
                _cpuFanAuto = false;
                _gpuFanAuto = false;
                _cpuFanSpeed = 30;
                _gpuFanSpeed = 30;
                OnPropertyChanged(nameof(CpuFanAuto));
                OnPropertyChanged(nameof(GpuFanAuto));
                OnPropertyChanged(nameof(CpuFanSpeed));
                OnPropertyChanged(nameof(GpuFanSpeed));
                ApplyCpuSpeed();
                ApplyGpuSpeed();
                StatusText = "Silent Mode (30%)";
                break;

            case "Balanced":
                _cpuFanAuto = false;
                _gpuFanAuto = false;
                _cpuFanSpeed = 60;
                _gpuFanSpeed = 60;
                OnPropertyChanged(nameof(CpuFanAuto));
                OnPropertyChanged(nameof(GpuFanAuto));
                OnPropertyChanged(nameof(CpuFanSpeed));
                OnPropertyChanged(nameof(GpuFanSpeed));
                ApplyCpuSpeed();
                ApplyGpuSpeed();
                StatusText = "Balanced Mode (60%)";
                break;

            case "Turbo":
                _cpuFanAuto = false;
                _gpuFanAuto = false;
                _cpuFanSpeed = 100;
                _gpuFanSpeed = 100;
                CoolBoost = true;
                OnPropertyChanged(nameof(CpuFanAuto));
                OnPropertyChanged(nameof(GpuFanAuto));
                OnPropertyChanged(nameof(CpuFanSpeed));
                OnPropertyChanged(nameof(GpuFanSpeed));
                ApplyCpuSpeed();
                ApplyGpuSpeed();
                StatusText = "MAX TURBO (100% + CoolBoost)";
                break;

            case "Curve":
                _cpuFanAuto = false;
                _gpuFanAuto = false;
                OnPropertyChanged(nameof(CpuFanAuto));
                OnPropertyChanged(nameof(GpuFanAuto));
                EvaluateCurveMode();
                StatusText = $"Smart Curve ({ActiveCurvePreset})";
                break;

            case "Custom":
                _cpuFanAuto = false;
                _gpuFanAuto = false;
                OnPropertyChanged(nameof(CpuFanAuto));
                OnPropertyChanged(nameof(GpuFanAuto));
                ApplyCpuSpeed();
                ApplyGpuSpeed();
                StatusText = "Manual Control";
                break;
        }

        SaveFanSettings();
    }

    public void ToggleTurbo()
    {
        if (ActiveMode == "Turbo")
        {
            SetMode(string.IsNullOrEmpty(_lastNonTurboMode) || _lastNonTurboMode == "Turbo" ? "Balanced" : _lastNonTurboMode);
        }
        else
        {
            if (ActiveMode != "Turbo") _lastNonTurboMode = ActiveMode;
            SetMode("Turbo");
        }
    }

    public void SetFixedSpeed(int fanIndex, int percent)
    {
        if (fanIndex == 0)
        {
            _cpuFanAuto = false;
            CpuFanSpeed = percent;
            OnPropertyChanged(nameof(CpuFanAuto));
        }
        else
        {
            _gpuFanAuto = false;
            GpuFanSpeed = percent;
            OnPropertyChanged(nameof(GpuFanAuto));
        }
        ActiveMode = "Custom";
        SaveFanSettings();
    }

    private void ApplyCpuSpeed()
    {
        FanHardwareManager.Instance.SetCpuFan(CpuFanAuto, CpuFanSpeed);
        SaveFanSettings();
    }

    private void ApplyGpuSpeed()
    {
        FanHardwareManager.Instance.SetGpuFan(GpuFanAuto, GpuFanSpeed);
        SaveFanSettings();
    }

    private void SaveFanSettings()
    {
        _settings.CpuManualSpeed = CpuFanSpeed;
        _settings.GpuManualSpeed = GpuFanSpeed;
        _settings.ActivePreset = ActiveMode;
        _settings.StartWithWindows = StartWithWindows;
        ConfigManager.Save(_settings);
    }

    private void UpdateActiveModeFromFans()
    {
        if (CpuFanAuto && GpuFanAuto) ActiveMode = "Auto";
        else if (ActiveMode != "Curve" && ActiveMode != "Turbo") ActiveMode = "Custom";
    }

    public void SetCurvePreset(string preset)
    {
        if (SelectedCurveTab == 0)
        {
            CpuCurvePreset = preset;
        }
        else
        {
            GpuCurvePreset = preset;
        }
        NotifyCurveProfileVisuals();
    }

    private void LoadCpuCurvePreset(string presetName)
    {
        IList<CurvePoint> source = presetName switch
        {
            "Silent" => FanCurveController.SilentCurve,
            "Balanced" => FanCurveController.BalancedCurve,
            "Gaming" => FanCurveController.GamingCurve,
            _ => _settings.CustomCpuCurvePoints.Count > 0 ? _settings.CustomCpuCurvePoints : FanCurveController.BalancedCurve
        };

        CpuCurvePoints.Clear();
        foreach (var p in source)
            CpuCurvePoints.Add(new CurvePoint(p.Temperature, p.FanSpeed));

        OnPropertyChanged(nameof(ActiveCurvePoints));
    }

    private void LoadGpuCurvePreset(string presetName)
    {
        IList<CurvePoint> source = presetName switch
        {
            "Silent" => FanCurveController.SilentCurve,
            "Balanced" => FanCurveController.BalancedCurve,
            "Gaming" => FanCurveController.GamingCurve,
            _ => _settings.CustomGpuCurvePoints.Count > 0 ? _settings.CustomGpuCurvePoints : FanCurveController.BalancedCurve
        };

        GpuCurvePoints.Clear();
        foreach (var p in source)
            GpuCurvePoints.Add(new CurvePoint(p.Temperature, p.FanSpeed));

        OnPropertyChanged(nameof(ActiveCurvePoints));
    }

    private void EvaluateCurveMode()
    {
        if (SyncFanCurves || LinkFans)
        {
            var targetSpeed = _cpuCurveController.EvaluateSpeed(PeakTemp, CpuCurvePoints);
            _cpuCurveTargetSpeed = targetSpeed;
            _gpuCurveTargetSpeed = targetSpeed;

            _cpuFanSpeed = targetSpeed;
            _gpuFanSpeed = targetSpeed;
            OnPropertyChanged(nameof(CpuFanSpeed));
            OnPropertyChanged(nameof(GpuFanSpeed));
            OnPropertyChanged(nameof(CpuCurveLiveText));
            OnPropertyChanged(nameof(GpuCurveLiveText));
            OnPropertyChanged(nameof(CurrentCurveLiveText));

            FanHardwareManager.Instance.SetCpuFan(false, targetSpeed);
            FanHardwareManager.Instance.SetGpuFan(false, targetSpeed);
            StatusText = $"Curve Synced: {targetSpeed}% (Peak {PeakTemp:F1}°C)";
        }
        else
        {
            var cpuTarget = _cpuCurveController.EvaluateSpeed(CpuTemp, CpuCurvePoints);
            var gpuTarget = _gpuCurveController.EvaluateSpeed(GpuTemp, GpuCurvePoints);

            _cpuCurveTargetSpeed = cpuTarget;
            _gpuCurveTargetSpeed = gpuTarget;

            _cpuFanSpeed = cpuTarget;
            _gpuFanSpeed = gpuTarget;
            OnPropertyChanged(nameof(CpuFanSpeed));
            OnPropertyChanged(nameof(GpuFanSpeed));
            OnPropertyChanged(nameof(CpuCurveLiveText));
            OnPropertyChanged(nameof(GpuCurveLiveText));
            OnPropertyChanged(nameof(CurrentCurveLiveText));

            FanHardwareManager.Instance.SetCpuFan(false, cpuTarget);
            FanHardwareManager.Instance.SetGpuFan(false, gpuTarget);
            StatusText = $"Curves: CPU {cpuTarget}% ({CpuTempText}) | GPU {gpuTarget}% ({GpuTempText})";
        }
    }

    private void OnPollTick(object? sender, EventArgs e)
    {
        PollHardware();
    }

    private void PollHardware()
    {
        var reading = FanHardwareManager.Instance.PollHardware();
        CpuTemp = reading.CpuTemp;
        GpuTemp = reading.GpuTemp;
        CpuFanRpm = reading.CpuRpm;
        GpuFanRpm = reading.GpuRpm;
        GpuName = reading.GpuName;
        GpuUtilization = reading.GpuUtilization;
        GpuPowerWatts = reading.GpuPowerWatts;
        GpuIsSleeping = reading.GpuIsSleeping;

        // Synchronize AllSensors list
        AllSensors.Clear();
        foreach (var s in reading.AllSensors)
            AllSensors.Add(s);
        OnPropertyChanged(nameof(SensorCountText));

        // Synchronize DetectedGpus list
        if (reading.DetectedGpus.Count > 0 && DetectedGpus.Count != reading.DetectedGpus.Count)
        {
            DetectedGpus.Clear();
            foreach (var g in reading.DetectedGpus)
                DetectedGpus.Add(g);

            if (_selectedGpu == null && DetectedGpus.Count > 0)
            {
                var active = GpuManager.Instance.ActiveGpu;
                _selectedGpu = DetectedGpus.FirstOrDefault(g => g.Id == active?.Id) ?? DetectedGpus[0];
                OnPropertyChanged(nameof(SelectedGpu));
            }
        }
        OnPropertyChanged(nameof(PlatformSummaryText));
        OnPropertyChanged(nameof(BackendStatusText));

        // Update MSI Afterburner Telemetry Series
        SeriesGpuTemp.AddPoint(reading.GpuTemp);
        SeriesGpuCoreClock.AddPoint(reading.GpuCoreClockMhz);
        SeriesGpuMemoryClock.AddPoint(reading.GpuMemoryClockMhz);
        SeriesGpuCoreLoad.AddPoint(reading.GpuUtilization);
        SeriesGpuPower.AddPoint(reading.GpuPowerWatts);
        SeriesGpuVramUsage.AddPoint(reading.GpuVramUsedMb);
        SeriesGpuFanRpm.AddPoint(reading.GpuRpm);
        SeriesCpuTemp.AddPoint(reading.CpuTemp);
        SeriesCpuPackageLoad.AddPoint(reading.CpuLoad);
        SeriesCpuPackagePower.AddPoint(reading.CpuPowerWatts);
        double ramPct = reading.RamTotalGb > 0 ? (reading.RamUsedGb / reading.RamTotalGb) * 100.0 : 0.0;
        SeriesRamUsage.AddPoint(ramPct);

        GpuClocksLiveText = $"{reading.GpuCoreClockMhz} MHz Core | {reading.GpuMemoryClockMhz} MHz Mem | VRAM: {reading.GpuVramUsedMb:F0}/{reading.GpuVramTotalMb:F0} MB";

        // Update RTSS OSD
        if (OsdEnabled)
        {
            var snap = new OsdTelemetrySnapshot
            {
                GpuName = reading.GpuName,
                GpuTemp = reading.GpuTemp,
                GpuLoad = reading.GpuUtilization,
                GpuCoreClockMhz = reading.GpuCoreClockMhz,
                GpuMemoryClockMhz = reading.GpuMemoryClockMhz,
                GpuPowerWatts = reading.GpuPowerWatts,
                GpuVramUsedMb = reading.GpuVramUsedMb,
                GpuVramTotalMb = reading.GpuVramTotalMb,
                GpuFanRpm = reading.GpuRpm,
                CpuName = CpuModelName,
                CpuTemp = reading.CpuTemp,
                CpuLoad = reading.CpuLoad,
                CpuPowerWatts = reading.CpuPowerWatts,
                CpuFanRpm = reading.CpuRpm,
                RamUsedGb = reading.RamUsedGb,
                RamTotalGb = reading.RamTotalGb,
                ActivePreset = ActiveMode
            };
            RtssOsdService.Instance.UpdateOsd(_settings.Osd, snap);
            RtssStatusText = RtssOsdService.Instance.IsRtssRunning
                ? "🟢 RTSS Connected (In-Game OSD Live)"
                : "⚪ RTSS Not Running (Launch RTSS to see in-game OSD)";
            OsdPreviewText = RtssOsdService.Instance.LastOsdPreview;
        }
        else
        {
            RtssStatusText = "Overlay Disabled";
        }

        // Feature A: Check Foreground Game/Application Watcher
        if (EnableAppProfiles)
        {
            ProcessWatcherService.Instance.CheckForegroundProcess(AppProfileRules);
        }

        // Feature D: Poll Battery Power Source
        BatteryMonitorService.Instance.PollPowerStatus();
        OnPropertyChanged(nameof(PowerSourceStatusText));

        // Overheat Protection Watchdog
        if (OverheatProtection && PeakTemp >= OverheatThreshold)
        {
            if (!IsOverheating)
            {
                IsOverheating = true;
                StatusText = $"OVERHEAT GUARD: Peak {PeakTemp:F1}°C >= {OverheatThreshold}°C (Max Fan)";
                FanHardwareManager.Instance.SetCpuFan(false, 100);
                FanHardwareManager.Instance.SetGpuFan(false, 100);
            }
        }
        else if (IsOverheating && PeakTemp < OverheatThreshold - 5)
        {
            // Reset after temperature cools down 5 degrees below threshold
            IsOverheating = false;
            SetMode(ActiveMode);
        }
        else if (ActiveMode == "Curve" && !_cpuDragging && !_gpuDragging)
        {
            EvaluateCurveMode();
        }
        else
        {
            // Keep live curve previews calculated for curve tabs
            _cpuCurveTargetSpeed = _cpuCurveController.EvaluateSpeed(CpuTemp, CpuCurvePoints);
            _gpuCurveTargetSpeed = _gpuCurveController.EvaluateSpeed(GpuTemp, GpuCurvePoints);
            OnPropertyChanged(nameof(CpuCurveLiveText));
            OnPropertyChanged(nameof(GpuCurveLiveText));
            OnPropertyChanged(nameof(CurrentCurveLiveText));
        }

        // Update Tray Tooltip
        var modeStr = ActiveMode == "Auto" ? "Auto" : $"{CpuFanSpeed}%/{GpuFanSpeed}%";
        var gpuDesc = GpuIsSleeping ? "Sleep" : $"{GpuTempText} ({GpuUtilization}%)";
        var tip = $"DynoFanControl: CPU {CpuTempText} ({CpuFanRpmText}) | GPU {gpuDesc} [{modeStr}]";
        TooltipUpdateRequested?.Invoke(tip);
    }

    private void ApplyInitialState()
    {
        if (ActiveMode != "Auto")
            SetMode(ActiveMode);
        else
            SetMode("Auto");

        if (CoolBoost)
            FanHardwareManager.Instance.SetCoolBoost(true);
    }

    public void ApplyGpuTuning()
    {
        var profile = new GpuTuningProfile
        {
            ProfileName = "Custom Tuning",
            CoreClockOffsetMhz = CoreClockOffset,
            MemoryClockOffsetMhz = MemoryClockOffset,
            PowerLimitPercent = PowerLimitPercent,
            TempLimitCelsius = TempLimitCelsius,
            VoltageOffsetMv = VoltageOffsetMv
        };
        GpuTuningManager.Instance.ApplyProfile(profile);
        _settings.CurrentTuningProfile = profile;
        ConfigManager.Save(_settings);
        TuningStatusText = GpuTuningManager.Instance.LastStatus;
    }

    public void ResetGpuTuningStock()
    {
        GpuTuningManager.Instance.ResetToStock();
        CoreClockOffset = 0;
        MemoryClockOffset = 0;
        PowerLimitPercent = 100;
        TempLimitCelsius = 83;
        VoltageOffsetMv = 0;
        _settings.CurrentTuningProfile = GpuTuningProfile.Stock;
        ConfigManager.Save(_settings);
        TuningStatusText = GpuTuningManager.Instance.LastStatus;
    }

    public void ApplyTuningPreset(string presetName)
    {
        var preset = presetName switch
        {
            "Stock" => GpuTuningProfile.Stock,
            "Undervolt" => GpuTuningProfile.Undervolt,
            "Balanced" => GpuTuningProfile.Balanced,
            "MaxOC" => GpuTuningProfile.MaxOverclock,
            "Eco" => GpuTuningProfile.Eco,
            _ => GpuTuningProfile.Stock
        };

        CoreClockOffset = preset.CoreClockOffsetMhz;
        MemoryClockOffset = preset.MemoryClockOffsetMhz;
        PowerLimitPercent = preset.PowerLimitPercent;
        TempLimitCelsius = preset.TempLimitCelsius;
        VoltageOffsetMv = preset.VoltageOffsetMv;

        GpuTuningManager.Instance.ApplyProfile(preset);
        _settings.CurrentTuningProfile = preset;
        ConfigManager.Save(_settings);
        TuningStatusText = $"Preset Applied: {preset.ProfileName}";
    }

    public void SaveTuningSlot(int slotIndex)
    {
        var profile = new GpuTuningProfile
        {
            ProfileName = $"Slot {slotIndex}",
            CoreClockOffsetMhz = CoreClockOffset,
            MemoryClockOffsetMhz = MemoryClockOffset,
            PowerLimitPercent = PowerLimitPercent,
            TempLimitCelsius = TempLimitCelsius,
            VoltageOffsetMv = VoltageOffsetMv
        };
        GpuTuningManager.Instance.SaveToSlot(slotIndex - 1, profile);
        ActiveTuningSlot = slotIndex;
        TuningStatusText = $"Tuning saved to Profile Slot [{slotIndex}]";
    }

    public void LoadTuningSlot(int slotIndex)
    {
        var profile = GpuTuningManager.Instance.GetSlot(slotIndex - 1);
        CoreClockOffset = profile.CoreClockOffsetMhz;
        MemoryClockOffset = profile.MemoryClockOffsetMhz;
        PowerLimitPercent = profile.PowerLimitPercent;
        TempLimitCelsius = profile.TempLimitCelsius;
        VoltageOffsetMv = profile.VoltageOffsetMv;

        GpuTuningManager.Instance.ApplyProfile(profile);
        _settings.CurrentTuningProfile = profile;
        ConfigManager.Save(_settings);
        ActiveTuningSlot = slotIndex;
        TuningStatusText = $"Loaded & Applied Profile Slot [{slotIndex}]";
    }

    #region Automation & Advanced Actions

    private void OnGameRuleActivated(AppProfileRule rule)
    {
        _preGameMode = ActiveMode;
        _preGameOsd = OsdEnabled;
        SetMode(rule.TargetMode);
        if (rule.TargetMode == "Curve" && !string.IsNullOrEmpty(rule.CurvePreset))
        {
            SetCurvePreset(rule.CurvePreset);
        }
        if (rule.EnableOsd)
        {
            OsdEnabled = true;
        }
        StatusText = $"🎮 Game Mode Engaged: {rule.DisplayName} ➔ {rule.TargetMode}";
        OnPropertyChanged(nameof(ActiveGameRuleText));
    }

    private void OnGameRuleDeactivated()
    {
        StatusText = $"Desktop Focus: Restoring {_preGameMode}";
        SetMode(_preGameMode);
        OsdEnabled = _preGameOsd;
        OnPropertyChanged(nameof(ActiveGameRuleText));
    }

    private void OnPowerStateChanged(bool isOnAc, int batteryPercent)
    {
        if (!EnableBatterySwitching) return;

        if (isOnAc)
        {
            SetMode(string.IsNullOrEmpty(AcMode) ? "Balanced" : AcMode);
            StatusText = $"⚡ AC Connected ({batteryPercent}%) ➔ {AcMode}";
        }
        else
        {
            SetMode(string.IsNullOrEmpty(BatteryMode) ? "Silent" : BatteryMode);
            StatusText = $"🔋 Running on Battery ({batteryPercent}%) ➔ {BatteryMode}";
        }
        OnPropertyChanged(nameof(PowerSourceStatusText));
    }

    public void AddCurrentAppRule(string targetMode)
    {
        string currentExe = ProcessWatcherService.GetActiveProcessName();
        if (string.IsNullOrEmpty(currentExe) || currentExe == "dynofancontrol" || currentExe == "fanctl")
        {
            StatusText = "Switch focus to your target game window first.";
            return;
        }

        var existing = AppProfileRules.FirstOrDefault(r => string.Equals(r.ProcessName, currentExe, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.TargetMode = targetMode;
            StatusText = $"Updated rule: {currentExe} ➔ {targetMode}";
        }
        else
        {
            var rule = new AppProfileRule(currentExe, currentExe.ToUpperInvariant(), targetMode, "Gaming", true);
            AppProfileRules.Add(rule);
            _settings.AppProfileRules.Add(rule);
            ConfigManager.Save(_settings);
            StatusText = $"Added game rule: {currentExe} ➔ {targetMode}";
        }
    }

    public void RemoveAppRule(AppProfileRule rule)
    {
        AppProfileRules.Remove(rule);
        _settings.AppProfileRules.Remove(rule);
        ConfigManager.Save(_settings);
        StatusText = $"Removed rule for {rule.DisplayName}";
    }

    public void ApplyCpuClamping()
    {
        CpuPowerTuningService.Instance.ApplyLimits(CpuMaxTempTarget, CpuPowerLimitWatts);
        OnPropertyChanged(nameof(CpuClampingStatusText));
        StatusText = CpuPowerTuningService.Instance.StatusMessage;
    }

    public void ResetCpuClamping()
    {
        CpuPowerTuningService.Instance.ResetLimits();
        OnPropertyChanged(nameof(CpuClampingStatusText));
        StatusText = "CPU thermal clamping disabled";
    }

    public async void StartFanCalibration()
    {
        if (IsCalibrating) return;
        OnPropertyChanged(nameof(IsCalibrating));
        await FanCalibrationService.Instance.StartCalibrationAsync(ActiveMode, CpuFanSpeed, GpuFanSpeed);
        OnPropertyChanged(nameof(IsCalibrating));
        OnPropertyChanged(nameof(CalibrationStatus));
    }

    public void CancelFanCalibration()
    {
        FanCalibrationService.Instance.Cancel();
        OnPropertyChanged(nameof(IsCalibrating));
        OnPropertyChanged(nameof(CalibrationStatus));
    }

    #endregion

    #endregion

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
