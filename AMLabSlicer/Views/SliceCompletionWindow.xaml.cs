using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using AMLabSlicer.ViewModel;
namespace AMLabSlicer.Views;
public partial class SliceCompletionWindow : ThemedWindow
{
 private readonly SliceCompletionViewModel vm;
 public SliceCompletionWindow(SliceCompletionViewModel model){InitializeComponent();DataContext=vm=model;}
 private void KeyChanged(object sender,RoutedEventArgs e)=>vm.ApiKey=((PasswordBox)sender).Password;
 protected override void OnClosing(CancelEventArgs e){if(vm.IsBusy)e.Cancel=true;base.OnClosing(e);}
}
