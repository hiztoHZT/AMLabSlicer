using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using AMLabSlicer.Services;

namespace AMLabSlicer.ViewModel;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private string? _projectPath;
    public string WindowTitle => ProjectPath == null ? "AMLab Slicer" : $"{Path.GetFileName(ProjectPath)} — AMLab Slicer";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveProjectCommand), nameof(SaveProjectAsCommand), nameof(OpenProjectCommand), nameof(LoadModelCommand), nameof(ImportDroppedModelsCommand))]
    private bool _isProjectBusy;
    private bool CanUseProject() => !IsProjectBusy && !IsImportingModels;

    [RelayCommand(CanExecute = nameof(CanUseProject))]
    private Task SaveProjectAsync() => SaveProjectCoreAsync(false);
    [RelayCommand(CanExecute = nameof(CanUseProject))]
    private Task SaveProjectAsAsync() => SaveProjectCoreAsync(true);
    private async Task SaveProjectCoreAsync(bool saveAs)
    {
        var path = ProjectPath;
        if (saveAs || path == null)
        {
            var dialog = new SaveFileDialog { Title = "保存工程", Filter = "AMLab 工程 (*.amlab)|*.amlab", DefaultExt = ".amlab", AddExtension = true,
                FileName = path == null ? "未命名工程.amlab" : Path.GetFileName(path) };
            path = UserDialogService.SelectSaveDestination(dialog, Application.Current.MainWindow);
            if (path == null) return;
        }
        IsProjectBusy = true;
        try
        {
            var document = PrepareWorkspace.CaptureProject();
            await Task.Run(() => ProjectFile.Save(path, document));
            ProjectPath = path;
            _projectSaveSucceeded = true;
        }
        catch (Exception ex) { _dialogs.ShowMessage("保存工程失败：" + ex.Message, "保存工程"); }
        finally { IsProjectBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanUseProject))]
    private async Task OpenProjectAsync()
    {
        var dialog = new OpenFileDialog { Title = "打开工程", Filter = "AMLab 工程 (*.amlab)|*.amlab" };
        if (dialog.ShowDialog(Application.Current.MainWindow) != true) return;
        // Always prompt: even an empty scene can contain unsaved printer/algorithm edits.
        var answer = AMLabSlicer.Views.MessageDialogWindow.ShowChoice(Application.Current.MainWindow,
            "打开工程将替换当前模型和参数。是否先保存当前工程？", "打开工程", "保存", "不保存");
        if (answer == MessageBoxResult.Cancel) return;
        if (answer == MessageBoxResult.Yes)
        {
            // Clear success marker so cancelling Save As or a failed write cannot discard the scene.
            _projectSaveSucceeded = false;
            await SaveProjectCoreAsync(false);
            if (!_projectSaveSucceeded) return;
        }
        IsProjectBusy = true;
        try
        {
            var document = await Task.Run(() => ProjectFile.Load(dialog.FileName));
            PrepareWorkspace.RestoreProject(document);
            CurrentWorkspace = PrepareWorkspace;
            ProjectPath = dialog.FileName;
        }
        catch (Exception ex) { _dialogs.ShowMessage("打开工程失败：" + ex.Message, "打开工程"); }
        finally { IsProjectBusy = false; }
    }
    private bool _projectSaveSucceeded;
}
