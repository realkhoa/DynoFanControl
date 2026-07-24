using System.Windows;
using System.Windows.Input;

namespace FanCtl;

public partial class MainWindow : Window
{
    private readonly FanViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new FanViewModel();
        DataContext = _vm;
    }

    private void Toggle0_Click(object sender, RoutedEventArgs e)
    {
        _vm.Fan0Auto = !_vm.Fan0Auto;
    }

    private void Toggle1_Click(object sender, RoutedEventArgs e)
    {
        _vm.Fan1Auto = !_vm.Fan1Auto;
    }

    private void Turbo_Click(object sender, RoutedEventArgs e)
    {
        _vm.Turbo();
    }

    private void Slider0_Down(object sender, MouseButtonEventArgs e)
    {
        _vm.SliderDragStarted(0);
    }

    private void Slider0_Up(object sender, MouseButtonEventArgs e)
    {
        _vm.SliderDragCompleted(0);
    }

    private void Slider1_Down(object sender, MouseButtonEventArgs e)
    {
        _vm.SliderDragStarted(1);
    }

    private void Slider1_Up(object sender, MouseButtonEventArgs e)
    {
        _vm.SliderDragCompleted(1);
    }
}
