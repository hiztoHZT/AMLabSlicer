using AMLabSlicer.ViewModel;
namespace AMLabSlicer.Views;
public partial class ExtensionsWindow : ThemedWindow
{
    public ExtensionsWindow(ExtensionsViewModel viewModel)
    {
        InitializeComponent(); DataContext = viewModel;
        Closed += (_, _) => { PluginSettingsContent.Content = null; viewModel.SelectedSettingsPage = null; };
    }
}
