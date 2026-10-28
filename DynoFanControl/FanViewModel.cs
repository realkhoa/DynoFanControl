using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DynoFanControl.Hardware;
using DynoFanControl.Hardware.Drivers;
using DynoFanControl.Hardware.Gpu;
using DynoFanControl.Services;

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
    private string _statusText = "Initializing...";

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

    public bool IsAutoActive => ActiveMode == "Auto";
    public bool IsSilentActive => ActiveMode == "Silent";
    public bool IsBalancedActive => ActiveMode == "Balanced";
    public bool IsCurveActive => ActiveMode == "Curve";
    public bool IsCustomActive => ActiveMode == "Custom";
    public bool IsTurboActive => TurboActive || ActiveMode == "Turbo";

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

    #endregion

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
