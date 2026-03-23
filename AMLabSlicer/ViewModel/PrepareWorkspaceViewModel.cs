using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
// 引入 WPF 3D 控件的命名空间
using HelixToolkit.Wpf.SharpDX;

namespace AMLabSlicer.ViewModel
{
    public partial class PrepareWorkspaceViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableObject? _currentParameterPanel;

        [ObservableProperty]
        private bool _isParameterPanelOpen = true;

        [ObservableProperty]
        private Element3D? _loadedModel;

        [RelayCommand]
        private void TogglePanel()
        {
            IsParameterPanelOpen = !IsParameterPanelOpen;
        }
    }
}