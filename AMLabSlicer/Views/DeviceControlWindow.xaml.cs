using System.Windows;
using System.Windows.Controls;
using AMLabSlicer.ViewModel;
namespace AMLabSlicer.Views;
public partial class DeviceControlWindow : ThemedWindow
{
    public DeviceControlWindow() { InitializeComponent(); DataContext = new DeviceControlViewModel(); Closed += (_, _) => ((DeviceControlViewModel)DataContext).Dispose(); }
    private void KeyChanged(object sender, RoutedEventArgs e) { if (DataContext is DeviceControlViewModel vm) vm.ApiKey = ((PasswordBox)sender).Password; }
}
