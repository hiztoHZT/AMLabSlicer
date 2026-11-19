using AMLabSlicer.Views;

namespace AMLabSlicer.Amlab_Monitor;

public partial class Amlab_MonitorSettingsWindow : ThemedWindow
{
    internal string? SelectedModelId { get; private set; }

    public Amlab_MonitorSettingsWindow()
    {
        InitializeComponent();
        var viewModel = new Amlab_MonitorSettingsViewModel(() => Amlab_MonitorSettingsViewModel.PickWeightFile(this));
        viewModel.Saved += () =>
        {
            SelectedModelId = viewModel.Amlab_MonitorSelectedEntry?.Id;
            DialogResult = true;
        };
        DataContext = viewModel;
    }

}
