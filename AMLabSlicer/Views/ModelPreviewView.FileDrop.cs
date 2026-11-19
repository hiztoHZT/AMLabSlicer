using System.Windows;
using AMLabSlicer.Services;
using AMLabSlicer.ViewModel;

namespace AMLabSlicer.Views;

public partial class ModelPreviewView
{
    private void ModelFiles_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Effects = DragDropEffects.None;
        if ((e.AllowedEffects & DragDropEffects.Copy) != 0 &&
            e.Data.GetData(DataFormats.FileDrop) is string[] paths &&
            paths.Any(ModelImportService.HasSupportedExtension) &&
            Window.GetWindow(this)?.DataContext is MainWindowViewModel main &&
            main.ImportDroppedModelsCommand.CanExecute(paths))
            e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void ModelFiles_Drop(object sender, DragEventArgs e)
    {
        ModelFiles_DragOver(sender, e);
        if (!e.Handled || e.Effects != DragDropEffects.Copy) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths &&
            Window.GetWindow(this)?.DataContext is MainWindowViewModel main)
            await main.ImportDroppedModelsCommand.ExecuteAsync(paths);
    }
}
