using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Threading;

namespace FanCtl;

public class FanViewModel : INotifyPropertyChanged
{
    private readonly DispatcherTimer _timer;
    private int _fan0Speed, _fan1Speed;
    private bool _fan0Auto = true, _fan1Auto = true;
    private bool _slider0Dragging, _slider1Dragging;
    private string _statusText = "";
    private bool _turboMode;

    public ObservableCollection<TempItem> Temps { get; } = new();

    public FanViewModel()
    {
        if (!EcProbe.IsAvailable) EcProbe.EnsureDriver();
        if (EcProbe.IsAvailable) StatusText = "INIT: EC+WMI";
        else StatusText = "INIT: WMI only";

        RefreshAll();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => UpdateTemps();
        _timer.Start();
    }

    public int Fan0Speed
    {
        get => _fan0Speed;
        set { if (_fan0Speed != value) { _fan0Speed = value; OnPropertyChanged(); if (!_slider0Dragging) DoSet(0, value); } }
    }
    public bool Fan0Auto
    {
        get => _fan0Auto;
        set { if (_fan0Auto != value) { _fan0Auto = value; OnPropertyChanged(); if (value) DoSet(0, 0); } }
    }
    public int Fan1Speed
    {
        get => _fan1Speed;
        set { if (_fan1Speed != value) { _fan1Speed = value; OnPropertyChanged(); if (!_slider1Dragging) DoSet(1, value); } }
    }
    public bool Fan1Auto
    {
        get => _fan1Auto;
        set { if (_fan1Auto != value) { _fan1Auto = value; OnPropertyChanged(); if (value) DoSet(1, 0); } }
    }
    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }
    public string SensorCountText => $"sensors: {Temps.Count}";

    public void SliderDragStarted(int i) { if (i == 0) _slider0Dragging = true; else _slider1Dragging = true; }
    public void SliderDragCompleted(int i)
    {
        if (i == 0) { _slider0Dragging = false; DoSet(0, _fan0Speed); }
        else { _slider1Dragging = false; DoSet(1, _fan1Speed); }
    }

    public void Turbo()
    {
        _turboMode = !_turboMode;
        if (_turboMode)
        {
            _fan0Auto = false; _fan1Auto = false;
            _fan0Speed = 100; _fan1Speed = 100;
            OnPropertyChanged(nameof(Fan0Auto)); OnPropertyChanged(nameof(Fan0Speed));
            OnPropertyChanged(nameof(Fan1Auto)); OnPropertyChanged(nameof(Fan1Speed));
            AcerWmi.SetFanSpeed(0, 100); AcerWmi.SetFanSpeed(1, 100);
            StatusText = "TURBO";
        }
        else
        {
            _fan0Auto = true; _fan1Auto = true;
            _fan0Speed = 50; _fan1Speed = 50;
            OnPropertyChanged(nameof(Fan0Auto)); OnPropertyChanged(nameof(Fan0Speed));
            OnPropertyChanged(nameof(Fan1Auto)); OnPropertyChanged(nameof(Fan1Speed));
            AcerWmi.SetFanSpeed(0, 0); AcerWmi.SetFanSpeed(1, 0);
            StatusText = "TURBO off";
        }
    }

    private void DoSet(int i, int s)
    {
        try { AcerWmi.SetFanSpeed(i, (uint)s); }
        catch { }
    }

    private void RefreshAll()
    {
        try
        {
            var f0 = AcerWmi.GetFanInfo(0); var f1 = AcerWmi.GetFanInfo(1);
            _fan0Auto = f0.IsAuto; _fan0Speed = f0.IsAuto ? 50 : (int)f0.Speed;
            _fan1Auto = f1.IsAuto; _fan1Speed = f1.IsAuto ? 50 : (int)f1.Speed;
            OnPropertyChanged(nameof(Fan0Speed)); OnPropertyChanged(nameof(Fan0Auto));
            OnPropertyChanged(nameof(Fan1Speed)); OnPropertyChanged(nameof(Fan1Auto));
            UpdateTemps();
            StatusText = $"Ready ({(EcProbe.IsAvailable ? "EC+WMI" : "WMI")})";
        }
        catch (Exception ex) { StatusText = $"Init: {ex.Message}"; }
    }

    private void UpdateTemps()
    {
        try
        {
            var readings = AcerWmi.GetLabelledTemps();
            Temps.Clear();
            foreach (var r in readings)
                Temps.Add(new TempItem { Label = r.Label, Value = $"{r.Value:F1}°C" });
        }
        catch { }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public class TempItem
{
    public string Label { get; init; } = "";
    public string Value { get; init; } = "";
}
