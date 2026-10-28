using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DynoFanControl.Services;
using DynoFanControl.Services.Automation;
using DynoFanControl.ViewModels;

namespace DynoFanControl;

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
        // Setup Window Message Hook for Single-Instance Activation
        try
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            var source = System.Windows.Interop.HwndSource.FromHwnd(helper.EnsureHandle());
            source?.AddHook(WndProc);
        }
        catch { }

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

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == App.WM_SHOWME)
        {
            RestoreWindow();
            handled = true;
        }
        return IntPtr.Zero;
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
        Topmost = true;
        Topmost = false;
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

    private void CurveTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string s && int.TryParse(s, out var tabIndex))
        {
            _vm.SelectedCurveTab = tabIndex;
        }
    }

    private void SensorsToggle_Click(object sender, RoutedEventArgs e)
    {
        _vm.IsSensorsDrawerOpen = !_vm.IsSensorsDrawerOpen;
    }

    private void CurveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string profile)
            _vm.SetCurvePreset(profile);
    }

    private void SettingsToggle_Click(object sender, RoutedEventArgs e)
    {
        _vm.IsSettingsOpen = !_vm.IsSettingsOpen;
    }

    private void TuningToggle_Click(object sender, RoutedEventArgs e)
    {
        _vm.IsTuningOpen = !_vm.IsTuningOpen;
    }

    private void MonitorToggle_Click(object sender, RoutedEventArgs e)
    {
        _vm.IsMonitorOpen = !_vm.IsMonitorOpen;
    }

    private void OsdToggle_Click(object sender, RoutedEventArgs e)
    {
        _vm.IsOsdOpen = !_vm.IsOsdOpen;
    }

    private void TuningPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string preset)
            _vm.ApplyTuningPreset(preset);
    }

    private void TuningApply_Click(object sender, RoutedEventArgs e)
    {
        _vm.ApplyGpuTuning();
    }

    private void TuningReset_Click(object sender, RoutedEventArgs e)
    {
        _vm.ResetGpuTuningStock();
    }

    private void TuningSlot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string s && int.TryParse(s, out var slot))
            _vm.LoadTuningSlot(slot);
    }

    private void TuningSaveSlot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string s && int.TryParse(s, out var slot))
            _vm.SaveTuningSlot(slot);
    }

    private void GameProfilesToggle_Click(object sender, RoutedEventArgs e)
    {
        _vm.IsGameProfilesOpen = !_vm.IsGameProfilesOpen;
    }

    private void CalibrationToggle_Click(object sender, RoutedEventArgs e)
    {
        _vm.IsCalibrationOpen = !_vm.IsCalibrationOpen;
    }

    private void CpuTuningToggle_Click(object sender, RoutedEventArgs e)
    {
        _vm.IsCpuTuningOpen = !_vm.IsCpuTuningOpen;
    }

    private void AddCurrentApp_Click(object sender, RoutedEventArgs e)
    {
        _vm.AddCurrentAppRule("Turbo");
    }

    private void DeleteAppRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.DataContext is AppProfileRule rule)
            _vm.RemoveAppRule(rule);
    }

    private void StartCalibration_Click(object sender, RoutedEventArgs e)
    {
        _vm.StartFanCalibration();
    }

    private void CancelCalibration_Click(object sender, RoutedEventArgs e)
    {
        _vm.CancelFanCalibration();
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
