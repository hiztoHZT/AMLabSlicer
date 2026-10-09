using System.Windows;
using AMLabSlicer.Views;
using Microsoft.Win32;

namespace AMLabSlicer.Services;

public sealed class UserDialogService(Func<PreferencesWindow> createPreferences) : IUserDialogService
{
    public string[] SelectModels()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要切片的 3D 模型",
            Filter = "3D 模型文件 (*.stl;*.obj;*.3mf;*.step;*.stp)|*.stl;*.obj;*.3mf;*.step;*.stp|所有文件 (*.*)|*.*",
            Multiselect = true
        };
        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FileNames : [];
    }

    public string? SelectGCodeDestination()
    {
        var dialog = new SaveFileDialog
        {
            Title = "保存切片 G-Code 文件",
            Filter = "G-Code Files (*.gcode)|*.gcode|All Files (*.*)|*.*",
            DefaultExt = ".gcode",
            FileName = "slice_output.gcode"
        };
        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FileName : null;
    }

    public void ShowMessage(string message, string title = "提示")
    {
        var owner = Application.Current.MainWindow;
        if (owner is { IsVisible: true })
            MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        else
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void OpenPreferences()
    {
        var window = createPreferences();
        window.Owner = Application.Current.MainWindow;
        window.ShowDialog();
    }
}
