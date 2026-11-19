using System.Windows;
using AMLabSlicer.Amlab_Monitor;
using AMLabSlicer.Views;
using Microsoft.Win32;
using AMLabSlicer.Plugin.Wpf.Localization;

namespace AMLabSlicer.Services;

public sealed class UserDialogService(Func<PreferencesWindow> createPreferences, Func<ExtensionsWindow> createExtensions,
    Func<PrinterSettingsWindow>? createPrinters = null) : IUserDialogService
{
    public Task<AMLabSlicer.Grpc.SliceResult?> SliceWithProgressAsync(ISlicingService service, AMLabSlicer.Grpc.SliceRequest request, string methodName)
    {
        using var vm = new AMLabSlicer.ViewModel.SliceProgressViewModel(methodName);
        var window = new SliceProgressWindow(vm, service, request) { Owner = Application.Current.MainWindow };
        window.ShowDialog();
        if (vm.WasCancelled) return Task.FromResult<AMLabSlicer.Grpc.SliceResult?>(null);
        if (vm.Error != null) throw new InvalidOperationException(vm.Error);
        return Task.FromResult(vm.Result);
    }
    public void ShowSliceCompletion(AMLabSlicer.Grpc.Artifact artifact, string printerName, string summary)
    {
        var vm = new AMLabSlicer.ViewModel.SliceCompletionViewModel(artifact, printerName, summary, this);
        new SliceCompletionWindow(vm) { Owner = Application.Current.MainWindow }.ShowDialog();
    }
    public string? PromptPresetName(string title,string name)
    {
        var dialog=new PresetNameWindow(title,"参数配置名称",name) { Owner=Application.Current.MainWindow };
        return dialog.ShowDialog()==true ? dialog.Result : null;
    }
    public bool ConfirmPresetDelete(string name)
    {
        var dialog=new PresetNameWindow("删除参数配置",$"删除“{name}”？此操作不可撤销。",null) { Owner=Application.Current.MainWindow };
        return dialog.ShowDialog()==true;
    }
    public string[] SelectModels()
    {
        var pattern = ModelImportService.ModelFilePattern;
        var dialog = new OpenFileDialog
        {
            Title = UiText.Current.Translate("选择要切片的 3D 模型"),
            Filter = UiText.Current.Language == "en" ? $"3D Models ({pattern})|{pattern}|All Files (*.*)|*.*" : $"3D 模型文件 ({pattern})|{pattern}|所有文件 (*.*)|*.*",
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
        return SelectSaveDestination(dialog, Application.Current.MainWindow);
    }

    public string? SelectPosePreviewDestination(string fileName)
    {
        var dialog = new SaveFileDialog { Title = "保存 XYZAB 位姿预览", Filter = "JSON (*.json)|*.json",
            DefaultExt = ".json", FileName = fileName };
        return SelectSaveDestination(dialog, Application.Current.MainWindow);
    }

    public string? SelectPlyDestination()
    {
        var dialog = new SaveFileDialog { Title = "保存 AtomSlicer 路径与法向", Filter = "PLY (*.ply)|*.ply",
            DefaultExt = ".ply", FileName = "toolpath.ply" };
        return SelectSaveDestination(dialog, Application.Current.MainWindow);
    }

    internal static string? SelectSaveDestination(SaveFileDialog dialog, Window owner)
    {
        dialog.OverwritePrompt = false;
        if (dialog.ShowDialog(owner) != true) return null;
        var path = dialog.FileName;
        if (!System.IO.File.Exists(path)) return path;

        var answer = MessageDialogWindow.ShowChoice(owner,
            $"“{System.IO.Path.GetFileName(path)}”已存在。是否覆盖？", "确认覆盖", "覆盖");
        return answer == MessageBoxResult.Yes ? path : null;
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
    public (bool Saved, string? SelectedModelId) OpenAmlab_MonitorSettings()
    {
        var window = new Amlab_MonitorSettingsWindow { Owner = Application.Current.MainWindow };
        return (window.ShowDialog() == true, window.SelectedModelId);
    }

    public void OpenPrinters()
    {
        if (createPrinters == null) return;
        var window = createPrinters(); window.Owner = Application.Current.MainWindow;
        if (window.DataContext is AMLabSlicer.ViewModel.PrinterSettingsViewModel settings &&
            Application.Current.MainWindow.DataContext is AMLabSlicer.ViewModel.MainWindowViewModel main &&
            main.PrepareWorkspace.SelectedAlgorithm is { } method)
            settings.SetCurrentMethod(method.AlgorithmId);
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
