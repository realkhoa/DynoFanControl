using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace FanCtl;

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b) return !b;
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b) return !b;
        return false;
    }
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool b = value is true;
        if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase))
            b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility.Visible;
    }
}

public class BoolToAutoManualConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isAuto)
            return isAuto ? "AUTO" : "MANUAL";
        return "AUTO";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class StringEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var v = value?.ToString() ?? "";
        var p = parameter?.ToString() ?? "";
        return string.Equals(v, p, StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class TempToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush CoolBrush = new(Color.FromRgb(0, 229, 255)); // Cyan
    private static readonly SolidColorBrush NormalBrush = new(Color.FromRgb(76, 175, 80)); // Green
    private static readonly SolidColorBrush WarmBrush = new(Color.FromRgb(255, 167, 38)); // Amber
    private static readonly SolidColorBrush HotBrush = new(Color.FromRgb(239, 83, 80)); // Crimson Red

    static TempToBrushConverter()
    {
        CoolBrush.Freeze();
        NormalBrush.Freeze();
        WarmBrush.Freeze();
        HotBrush.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double temp = 0;
        if (value is double d) temp = d;
        else if (value is float f) temp = f;
        else if (value != null && double.TryParse(value.ToString(), out var parsed)) temp = parsed;

        if (temp <= 48) return CoolBrush;
        if (temp <= 62) return NormalBrush;
        if (temp <= 76) return WarmBrush;
        return HotBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
