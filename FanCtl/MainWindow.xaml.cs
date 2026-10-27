using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FanCtl.Services;
using FanCtl.ViewModels;

namespace FanCtl;

public partial class MainWindow : Window
{
    private readonly FanViewModel _vm;
    private TrayIconManager? _tray;
    private bool _isExplicitExit;

    public MainWindow()
    {
        InitializeComponent();
        try
        {
            var iconUri = new Uri("pack://application:,,,/app.ico", UriKind.RelativeOrAbsolute);
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(iconUri);
        }
        catch
        {
            // Ignore icon loading error
        }
        _vm = new FanViewModel();
        DataContext = _vm;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Setup System Tray
        try
        {
            if (FindResource("TrayMenu") is ContextMenu menu)
            {
                menu.DataContext = _vm;
                _tray = new TrayIconManager(this, menu);
                _tray.OnOpenRequested += RestoreWindow;
                _tray.Initialize();

                _vm.TooltipUpdateRequested += text => _tray.UpdateTooltip(text);
            }
        }
        catch
        {
            // Ignore tray setup error
        }

        // Only start minimized if explicitly passed via command line argument (--minimized)
        if (App.StartMinimized)
        {
            Hide();
            WindowState = WindowState.Minimized;
        }
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (!_isExplicitExit && _vm.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _tray?.Dispose();
    }

    public void RestoreWindow()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        Focus();
    }

    #region UI Event Handlers

    private void Mode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string mode)
            _vm.SetMode(mode);
    }

    private void Turbo_Click(object sender, RoutedEventArgs e)
    {
        _vm.ToggleTurbo();
    }

    private void CpuAutoToggle_Click(object sender, RoutedEventArgs e)
    {
        _vm.CpuFanAuto = !_vm.CpuFanAuto;
    }

    private void GpuAutoToggle_Click(object sender, RoutedEventArgs e)
    {
        _vm.GpuFanAuto = !_vm.GpuFanAuto;
    }

    private void CpuSlider_Down(object sender, MouseButtonEventArgs e)
    {
        _vm.SliderDragStarted(0);
    }

    private void CpuSlider_Up(object sender, MouseButtonEventArgs e)
    {
        _vm.SliderDragCompleted(0);
    }

    private void GpuSlider_Down(object sender, MouseButtonEventArgs e)
    {
        _vm.SliderDragStarted(1);
    }

    private void GpuSlider_Up(object sender, MouseButtonEventArgs e)
    {
        _vm.SliderDragCompleted(1);
    }

    private void CpuChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string s && int.TryParse(s, out var speed))
            _vm.SetFixedSpeed(0, speed);
    }

    private void GpuChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string s && int.TryParse(s, out var speed))
            _vm.SetFixedSpeed(1, speed);
    }

    private void CurveToggle_Click(object sender, RoutedEventArgs e)
    {
        _vm.IsCurveEditorOpen = !_vm.IsCurveEditorOpen;
    }

    private void SensorsToggle_Click(object sender, RoutedEventArgs e)
    {
        _vm.IsSensorsDrawerOpen = !_vm.IsSensorsDrawerOpen;
    }

    private void CurveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string profile)
            _vm.ActiveCurvePreset = profile;
    }

    private void SettingsToggle_Click(object sender, RoutedEventArgs e)
    {
        _vm.IsSettingsOpen = !_vm.IsSettingsOpen;
    }

    private void TrayShow_Click(object sender, RoutedEventArgs e)
    {
        RestoreWindow();
    }

    private void TrayMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && item.Tag is string mode)
            _vm.SetMode(mode);
    }

    private void TrayExit_Click(object sender, RoutedEventArgs e)
    {
        _isExplicitExit = true;
        _tray?.Dispose();
        Close();
        Application.Current.Shutdown();
    }

    #endregion
}
