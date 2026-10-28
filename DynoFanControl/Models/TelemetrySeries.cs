using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DynoFanControl.Models;

/// <summary>
/// High-performance rolling telemetry series for MSI Afterburner-style real-time graphs.
/// Tracks minimum, current, and maximum metrics, and automatically generates SVG/WPF
/// polyline and polygon coordinate strings for ultra-smooth UI rendering.
/// </summary>
public sealed class TelemetrySeries : INotifyPropertyChanged
{
    private readonly double[] _buffer;
    private int _count;
    private int _head;
    private readonly double _chartWidth;
    private readonly double _chartHeight;
    private readonly double _fixedMin;
    private readonly double _fixedMax;
    private readonly bool _autoScale;

    private double _latest;
    private double _min;
    private double _max;
    private string _polylinePoints = "";
    private string _polygonPoints = "";

    public string Title { get; }
    public string Unit { get; }
    public string ColorHex { get; }

    public double Latest
    {
        get => _latest;
        private set { _latest = value; OnPropertyChanged(); OnPropertyChanged(nameof(FormattedValue)); }
    }

    public double Min
    {
        get => _min;
        private set { _min = value; OnPropertyChanged(); OnPropertyChanged(nameof(FormattedMin)); }
    }

    public double Max
    {
        get => _max;
        private set { _max = value; OnPropertyChanged(); OnPropertyChanged(nameof(FormattedMax)); }
    }

    public string FormattedValue => $"{Latest:F0}{Unit}";
    public string FormattedMin => $"Min: {Min:F0}{Unit}";
    public string FormattedMax => $"Max: {Max:F0}{Unit}";

    public string PolylinePoints
    {
        get => _polylinePoints;
        private set { _polylinePoints = value; OnPropertyChanged(); }
    }

    public string PolygonPoints
    {
        get => _polygonPoints;
        private set { _polygonPoints = value; OnPropertyChanged(); }
    }

    public TelemetrySeries(string title, string unit, string colorHex, int capacity = 60, double width = 240, double height = 65, double min = 0, double max = 100, bool autoScale = true)
    {
        Title = title;
        Unit = unit;
        ColorHex = colorHex;
        _buffer = new double[capacity];
        _chartWidth = width;
        _chartHeight = height;
        _fixedMin = min;
        _fixedMax = max;
        _autoScale = autoScale;
        _min = double.MaxValue;
        _max = double.MinValue;
    }

    public void AddPoint(double value)
    {
        _buffer[_head] = value;
        _head = (_head + 1) % _buffer.Length;
        if (_count < _buffer.Length) _count++;

        Latest = value;

        // Compute Min & Max
        double curMin = double.MaxValue;
        double curMax = double.MinValue;

        for (int i = 0; i < _count; i++)
        {
            double v = _buffer[i];
            if (v < curMin) curMin = v;
            if (v > curMax) curMax = v;
        }

        Min = curMin;
        Max = curMax;

        UpdatePoints();
    }

    private void UpdatePoints()
    {
        if (_count < 2)
        {
            PolylinePoints = $"0,{_chartHeight} {_chartWidth},{_chartHeight}";
            PolygonPoints = $"0,{_chartHeight} {_chartWidth},{_chartHeight} {_chartWidth},{_chartHeight} 0,{_chartHeight}";
            return;
        }

        double plotMin = _autoScale ? Math.Min(_fixedMin, Min) : _fixedMin;
        double plotMax = _autoScale ? Math.Max(_fixedMax, Max) : _fixedMax;
        if (Math.Abs(plotMax - plotMin) < 0.001) plotMax = plotMin + 1.0;

        double stepX = _chartWidth / (_buffer.Length - 1);
        int startIndex = (_count == _buffer.Length) ? _head : 0;
        int pointsCount = _count;

        var lineSb = new System.Text.StringBuilder();
        var polySb = new System.Text.StringBuilder();

        // Start polygon from bottom-left
        double firstX = (_buffer.Length - pointsCount) * stepX;
        polySb.Append($"{firstX:F1},{_chartHeight:F1} ");

        for (int i = 0; i < pointsCount; i++)
        {
            int bufIdx = (startIndex + i) % _buffer.Length;
            double val = _buffer[bufIdx];

            // Normalize Y: plotMax -> 0, plotMin -> _chartHeight
            double norm = (val - plotMin) / (plotMax - plotMin);
            double y = _chartHeight - (norm * (_chartHeight - 8.0)) - 4.0; // 4px padding
            double x = ((_buffer.Length - pointsCount) + i) * stepX;

            lineSb.Append($"{x:F1},{y:F1} ");
            polySb.Append($"{x:F1},{y:F1} ");
        }

        double lastX = _chartWidth;
        polySb.Append($"{lastX:F1},{_chartHeight:F1}");

        PolylinePoints = lineSb.ToString().TrimEnd();
        PolygonPoints = polySb.ToString().TrimEnd();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
