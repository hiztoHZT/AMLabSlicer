using System.Windows;
using AMLabSlicer.Views;
using Microsoft.Win32;
using AMLabSlicer.Plugin.Wpf.Localization;

namespace AMLabSlicer.Services;

public sealed class UserDialogService(Func<PreferencesWindow> createPreferences, Func<ExtensionsWindow> createExtensions) : IUserDialogService
{
    public string[] SelectModels()
    {
        var dialog = new OpenFileDialog
        {
            Title = UiText.Current.Translate("选择要切片的 3D 模型"),
            Filter = UiText.Current.Language == "en" ? "3D Models (*.stl;*.obj;*.3mf;*.step;*.stp)|*.stl;*.obj;*.3mf;*.step;*.stp|All Files (*.*)|*.*" : "3D 模型文件 (*.stl;*.obj;*.3mf;*.step;*.stp)|*.stl;*.obj;*.3mf;*.step;*.stp|所有文件 (*.*)|*.*",
            Multiselect = true
        };
        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FileNames : [];
    }

    public string? SelectGCodeDestination()
    {
        var dialog = new SaveFileDialog
        {
            Title = UiText.Current.Translate("保存切片 G-Code 文件"),
            Filter = "G-Code Files (*.gcode)|*.gcode|All Files (*.*)|*.*",
            DefaultExt = ".gcode",
            FileName = "slice_output.gcode"
        };
        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FileName : null;
    }

    public void ShowMessage(string message, string title = "提示")
    {
        MessageDialogWindow.ShowMessage(Application.Current.MainWindow, message, title);
    }

    public void OpenPreferences()
    {
        var window = createPreferences();
        window.Owner = Application.Current.MainWindow;
        window.ShowDialog();
    }
    public void OpenExtensions(string section = "插件管理")
    {
        var window = createExtensions();
        if (window.DataContext is AMLabSlicer.ViewModel.ExtensionsViewModel vm) { vm.RefreshCommand.Execute(null); vm.SelectSection(section); }
        window.Owner = Application.Current.MainWindow;
        window.ShowDialog();
    }
    public string? SelectPluginPackage()
    {
        var dialog = new OpenFileDialog { Title = UiText.Current.Translate("安装插件包"), Filter = UiText.Current.Language == "en" ? "Plugin Package (*.zip)|*.zip" : "插件包 (*.zip)|*.zip" };
        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FileName : null;
    }
}
