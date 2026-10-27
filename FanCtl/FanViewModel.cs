using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using FanCtl.Hardware;
using FanCtl.Services;

namespace FanCtl;

public class FanViewModel : INotifyPropertyChanged
{
    private readonly DispatcherTimer _pollTimer;
    private readonly FanCurveController _curveController = new();
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

    private string _activeMode = "Auto"; // Auto, Silent, Balanced, Turbo, Curve, Custom
    private string _activeCurvePreset = "Balanced";
    private bool _overheatProtection = true;
    private int _overheatThreshold = 88;
    private bool _isOverheating;

    private bool _startWithWindows;
    private bool _startMinimized;
    private bool _closeToTray = true;

    private bool _isSettingsOpen;
    private bool _isCurveEditorOpen;
    private string _statusText = "Initializing...";

    private bool _cpuDragging;
    private bool _gpuDragging;

    public ObservableCollection<CurvePoint> CurvePoints { get; } = new();

    public event Action<string>? TooltipUpdateRequested;

    public FanViewModel()
    {
        // 1. Load persisted settings
        _settings = ConfigManager.Load();
        _linkFans = _settings.LinkFans;
        _coolBoost = _settings.CoolBoost;
        _overheatProtection = _settings.OverheatProtection;
        _overheatThreshold = _settings.OverheatThreshold;
        _startWithWindows = StartupManager.IsStartupEnabled();
        _startMinimized = _settings.StartMinimized;
        _closeToTray = _settings.CloseToTray;
        _activeMode = _settings.ActivePreset;
        _activeCurvePreset = _settings.ActiveCurvePreset;
        _cpuFanSpeed = _settings.CpuManualSpeed;
        _gpuFanSpeed = _settings.GpuManualSpeed;

        foreach (var p in _settings.CustomCurvePoints)
            CurvePoints.Add(new CurvePoint(p.Temperature, p.FanSpeed));

        // 2. Initialize hardware
        FanHardwareManager.Instance.Initialize();

        // 3. Setup hardware poll timer
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(1, _settings.PollIntervalSeconds)) };
        _pollTimer.Tick += OnPollTick;
        _pollTimer.Start();

        // 4. Initial poll & apply saved state
        PollHardware();
        ApplyInitialState();
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
                _settings.ActivePreset = value;
                ConfigManager.Save(_settings);
            }
        }
    }

    public string ActiveCurvePreset
    {
        get => _activeCurvePreset;
        set
        {
            if (_activeCurvePreset != value)
            {
                _activeCurvePreset = value;
                OnPropertyChanged();
                LoadCurvePreset(value);
                _settings.ActiveCurvePreset = value;
                ConfigManager.Save(_settings);
            }
        }
    }

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
        ActiveMode = "Custom";
    }

    public void SetMode(string mode)
    {
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
            SetMode("Auto");
        else
            SetMode("Turbo");
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
        ConfigManager.Save(_settings);
    }

    private void UpdateActiveModeFromFans()
    {
        if (CpuFanAuto && GpuFanAuto) ActiveMode = "Auto";
        else if (ActiveMode != "Curve" && ActiveMode != "Turbo") ActiveMode = "Custom";
    }

    private void LoadCurvePreset(string presetName)
    {
        IList<CurvePoint> source = presetName switch
        {
            "Silent" => FanCurveController.SilentCurve,
            "Balanced" => FanCurveController.BalancedCurve,
            "Gaming" => FanCurveController.GamingCurve,
            _ => _settings.CustomCurvePoints
        };

        CurvePoints.Clear();
        foreach (var p in source)
            CurvePoints.Add(new CurvePoint(p.Temperature, p.FanSpeed));

        if (ActiveMode == "Curve")
            EvaluateCurveMode();
    }

    private void EvaluateCurveMode()
    {
        var targetSpeed = _curveController.EvaluateSpeed(PeakTemp, CurvePoints);
        _cpuFanSpeed = targetSpeed;
        _gpuFanSpeed = targetSpeed;
        OnPropertyChanged(nameof(CpuFanSpeed));
        OnPropertyChanged(nameof(GpuFanSpeed));

        FanHardwareManager.Instance.SetCpuFan(false, targetSpeed);
        FanHardwareManager.Instance.SetGpuFan(false, targetSpeed);
        StatusText = $"Curve Active: {targetSpeed}% (Peak {PeakTemp:F1}°C)";
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

        // Update Tray Tooltip
        var modeStr = ActiveMode == "Auto" ? "Auto" : $"{CpuFanSpeed}%/{GpuFanSpeed}%";
        var tip = $"FanCtl: CPU {CpuTempText} ({CpuFanRpmText}) | GPU {GpuTempText} ({GpuFanRpmText}) [{modeStr}]";
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
