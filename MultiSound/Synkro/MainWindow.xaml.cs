using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Synkro.Core;
using Synkro.ViewModels;

namespace Synkro;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void VolDown_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is ChannelViewModel vm)
            vm.Volume = Math.Max(0, vm.Volume - 0.05f);
    }

    private void VolUp_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is ChannelViewModel vm)
            vm.Volume = Math.Min(AudioOutputChannel.MaxVolume, vm.Volume + 0.05f);
    }

    private void FineTuneDown_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is ChannelViewModel vm)
            vm.FineTuneMs = Math.Max(-300, vm.FineTuneMs - 5);
    }

    private void FineTuneUp_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is ChannelViewModel vm)
            vm.FineTuneMs = Math.Min(300, vm.FineTuneMs + 5);
    }

    private void RemoveDevice_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is ChannelViewModel slot && DataContext is MainViewModel vm)
            vm.RemoveDevice(slot);
    }

}

public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

