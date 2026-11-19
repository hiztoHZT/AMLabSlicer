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

        private AMLabSlicer.ViewModel.OutlinerNodeViewModel[] _menuTargets = [];
        private void ObjectMenu_Opened(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is not ContextMenu menu ||
                menu.DataContext is not AMLabSlicer.ViewModel.OutlinerNodeViewModel target ||
                DataContext is not AMLabSlicer.ViewModel.PrepareWorkspaceViewModel workspace) return;
            _dragItem = null;
            _menuTargets = target.IsSelected ? workspace.SelectedItems.ToArray() : [target];
            if (!target.IsSelected) workspace.SelectObjects(_menuTargets);
            bool editable = _menuTargets.All(x => x.IsEffectivelyVisible && !x.DescendantsAndSelf().Any(y => y.IsEffectivelyLocked));
            foreach (var item in menu.Items.OfType<MenuItem>())
                item.IsEnabled = (item.Tag as string) switch
                {
                    "rename" => _menuTargets.Length == 1,
                    "group" => editable && _menuTargets.Length > 1 && _menuTargets.All(x => x.Parent == target.Parent),
                    "ungroup" => editable && _menuTargets.Length == 1 && target.IsGroup,
                    "delete" => editable,
                    "hide" => _menuTargets.Any(x => x.CanToggleVisibility && x.IsVisible),
                    "show" => _menuTargets.Any(x => x.CanToggleVisibility && !x.IsVisible),
                    "lock" => _menuTargets.Any(x => x.CanToggleLock && !x.IsLocked),
                    "unlock" => _menuTargets.Any(x => x.CanToggleLock && x.IsLocked),
                    _ => true
                };
        }

        private void ObjectMenu_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem ||
                DataContext is not AMLabSlicer.ViewModel.PrepareWorkspaceViewModel workspace) return;
            var targets = _menuTargets.Where(workspace.AllOutlinerItems.Contains).ToArray();
            switch (menuItem.Tag as string)
            {
                case "showAll":
                    workspace.ShowAllObjects();
                    break;
                case "unlockAll":
                    workspace.UnlockAllObjects();
                    break;
                case "group":
                    workspace.SelectObjects(targets);
                    workspace.GroupSelectionCommand.Execute(null);
                    break;
                case "ungroup":
                    workspace.SelectObjects(targets);
                    workspace.UngroupSelectionCommand.Execute(null);
                    break;
                case "hide":
                case "show":
                    foreach (var target in targets.Where(x => x.CanToggleVisibility))
                        target.IsVisible = (string)menuItem.Tag == "show";
                    break;
                case "lock":
                case "unlock":
                    foreach (var target in targets.Where(x => x.CanToggleLock))
                        target.IsLocked = (string)menuItem.Tag == "lock";
                    break;
                case "delete":
                    workspace.SelectObjects(targets);
                    System.Windows.DependencyObject? parent = this;
                    while (parent != null && parent is not PrepareWorkspaceView)
                        parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
                    if (parent is PrepareWorkspaceView view)
                        ((ModelPreviewView)view.FindName("ModelPreview")).ConfirmAndDelete();
                    break;
            }
            e.Handled = true;
        }
        private System.Windows.Point _dragStart;
        private AMLabSlicer.ViewModel.OutlinerNodeViewModel? _dragItem;
        private const string ObjectDragFormat = "AMLabSlicer.OutlinerObject";
        private void Outliner_MouseUp(object sender, MouseButtonEventArgs e) => _dragItem = null;
        private void Outliner_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragItem == null || e.LeftButton != MouseButtonState.Pressed) return;
            var delta = e.GetPosition(this) - _dragStart;
            if (Math.Abs(delta.X) < System.Windows.SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < System.Windows.SystemParameters.MinimumVerticalDragDistance) return;
            var item = _dragItem; _dragItem = null;
            var data = new System.Windows.DataObject(ObjectDragFormat, item);
            System.Windows.DragDrop.DoDragDrop(this, data, System.Windows.DragDropEffects.Move);
            e.Handled = true;
        }
        private void ObjectRow_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            e.Effects = System.Windows.DragDropEffects.None;
            if (e.Data.GetData(ObjectDragFormat) is AMLabSlicer.ViewModel.OutlinerNodeViewModel source && sender is System.Windows.FrameworkElement row && row.DataContext is AMLabSlicer.ViewModel.OutlinerNodeViewModel target && source != target && source.Parent == target.Parent)
                e.Effects = System.Windows.DragDropEffects.Move;
            if (sender is Border border)
            {
                border.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                border.BorderThickness = e.Effects == System.Windows.DragDropEffects.Move
                    ? e.GetPosition(border).Y < border.ActualHeight / 2 ? new System.Windows.Thickness(0,1,0,0) : new System.Windows.Thickness(0,0,0,1)
                    : new System.Windows.Thickness(0);
            }
            e.Handled = true;
        }
        private void ObjectRow_DragLeave(object sender, System.Windows.DragEventArgs e)
        {
            if (sender is Border border) border.ClearValue(Border.BorderThicknessProperty);
            e.Handled = true;
        }
        private void ObjectRow_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (sender is Border border) border.ClearValue(Border.BorderThicknessProperty);
            if (DataContext is AMLabSlicer.ViewModel.PrepareWorkspaceViewModel workspace && e.Data.GetData(ObjectDragFormat) is AMLabSlicer.ViewModel.OutlinerNodeViewModel source && sender is System.Windows.FrameworkElement row && row.DataContext is AMLabSlicer.ViewModel.OutlinerNodeViewModel target)
                workspace.ReorderObject(source, target, e.GetPosition(row).Y >= row.ActualHeight / 2);
            e.Handled = true;
        }
        private void NameLabel_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2 || sender is not System.Windows.FrameworkElement { DataContext: AMLabSlicer.ViewModel.OutlinerNodeViewModel item }) return;
            _dragItem = null; item.RenameCommand.Execute(null); e.Handled = true;
        }
        private void RenameBox_IsVisibleChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
        {
            if (sender is TextBox box && box.IsVisible)
                box.Dispatcher.BeginInvoke(new Action(() => { if (box.IsVisible) { box.Focus(); box.SelectAll(); } }));
        }
        private AMLabSlicer.ViewModel.OutlinerNodeViewModel? HoveredItem()
        {
            var hit = InputHitTest(Mouse.GetPosition(this)) as System.Windows.DependencyObject;
            while (hit != null)
            {
                if (hit is System.Windows.FrameworkElement element && element.DataContext is AMLabSlicer.ViewModel.OutlinerNodeViewModel item) return item;
                hit = System.Windows.Media.VisualTreeHelper.GetParent(hit);
            }
            return null;
        }
        private void ObjectRow_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is TextBox || e.OriginalSource is System.Windows.Controls.Primitives.ButtonBase) return;
            var source = e.OriginalSource as System.Windows.DependencyObject;
            while (source != null && source != sender)
            {
                if (source is System.Windows.Controls.Primitives.ButtonBase || source is TextBox) return;
                source = System.Windows.Media.VisualTreeHelper.GetParent(source);
            }
            if (DataContext is AMLabSlicer.ViewModel.PrepareWorkspaceViewModel workspace && sender is System.Windows.FrameworkElement row && row.DataContext is AMLabSlicer.ViewModel.OutlinerNodeViewModel item)
            { Focus(); workspace.SelectObject(item.Node, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)); _dragStart = e.GetPosition(this); _dragItem = item; e.Handled = true; }
        }
        private void Outliner_KeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.FocusedElement is TextBox) return;
            if (DataContext is not AMLabSlicer.ViewModel.PrepareWorkspaceViewModel workspace) return;
            var modifiers = Keyboard.Modifiers;
            if (e.Key == Key.H && modifiers == ModifierKeys.None) { workspace.HideSelectedObjects(); e.Handled = true; return; }
            if (e.Key == Key.L && modifiers == ModifierKeys.None) { workspace.LockSelectedObjects(); e.Handled = true; return; }
            if (e.Key == Key.H && modifiers == ModifierKeys.Shift) { workspace.ShowAllObjects(); e.Handled = true; return; }
            if (e.Key == Key.L && modifiers == ModifierKeys.Shift) { workspace.UnlockAllObjects(); e.Handled = true; return; }
            if (e.Key == Key.G && modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) { workspace.UngroupSelectionCommand.Execute(null); e.Handled = true; return; }
            if (e.Key == Key.A && modifiers == ModifierKeys.Control) { workspace.SelectAllObjects(); e.Handled = true; }
            else if (e.Key == Key.A && modifiers == ModifierKeys.Shift && HoveredItem() is { } item) { workspace.SelectObject(item.Node, true); e.Handled = true; }
            else if (e.Key == Key.G && modifiers == ModifierKeys.Shift) { workspace.GroupSelectionCommand.Execute(null); e.Handled = true; }
            else if (e.Key == Key.D && modifiers == ModifierKeys.Shift) { workspace.InvertObjectSelection(); e.Handled = true; }
            else if (e.Key == Key.Escape) { workspace.SelectObjects([]); e.Handled = true; }
            else if (e.Key == Key.F2 && workspace.SelectedItems.Count == 1 && workspace.ActiveSelection is { } active) { active.RenameCommand.Execute(null); e.Handled = true; }
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
