using System.Windows.Controls;
using AMLabSlicer.ViewModel;

namespace AMLabSlicer.Views
{
    public partial class PrepareWorkspaceView : UserControl
    {
        public PrepareWorkspaceView()
        {
            InitializeComponent();
            // DataContext 在父级绑定后才可用，延迟到 Loaded 时传递 AppPrefs
            Loaded += (_, _) =>
            {
                if (DataContext is PrepareWorkspaceViewModel vm)
                    ModelPreview.SetPreferences(vm.AppPrefs);
            };
        }
    }
}