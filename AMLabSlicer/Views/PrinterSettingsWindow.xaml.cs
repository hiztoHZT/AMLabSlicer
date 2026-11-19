using System.Windows;
using AMLabSlicer.ViewModel;

namespace AMLabSlicer.Views;

public partial class PrinterSettingsWindow : ThemedWindow
{
    private void ShowSaveError(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PrinterSettingsViewModel.Status) && sender is PrinterSettingsViewModel vm && vm.Status.StartsWith("未保存"))
            MessageDialogWindow.ShowMessage(this, vm.Status, "保存失败", warning: true);
    }
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is PrinterSettingsViewModel vm && vm.HasUnsavedChanges)
        {
            var result = MessageDialogWindow.ShowChoice(this, "是否保存当前更改？", "打印机设置", "保存", "不保存");
            if (result == MessageBoxResult.Cancel || (result == MessageBoxResult.Yes && !vm.TrySave())) e.Cancel = true;
        }
        base.OnClosing(e);
    }
    public PrinterSettingsWindow(PrinterSettingsViewModel viewModel) { InitializeComponent(); DataContext = viewModel;
        viewModel.PropertyChanged += ShowSaveError;
        Closed += (_, _) => viewModel.PropertyChanged -= ShowSaveError; }
}
