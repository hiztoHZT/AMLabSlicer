using System.Windows.Controls;
using System.Windows.Input;

namespace AMLabSlicer.Views
{
    public partial class OutlinerPanelView : UserControl
    {
        public OutlinerPanelView()
        {
            InitializeComponent();
        }

        private void RenameBox_LostFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.DataContext is AMLabSlicer.ViewModel.OutlinerNodeViewModel vm)
            {
                vm.CommitRename();
            }
        }

        private void RenameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is TextBox tb && tb.DataContext is AMLabSlicer.ViewModel.OutlinerNodeViewModel vm)
            {
                if (e.Key == Key.Enter)
                {
                    vm.CommitRename();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    vm.CancelRename();
                    e.Handled = true;
                }
            }
        }
    }
}
