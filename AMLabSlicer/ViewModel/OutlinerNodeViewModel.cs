using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixToolkit.SharpDX.Model.Scene;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AMLabSlicer.ViewModel
{
    /// <summary>
    /// 面向左侧面板大纲视图（Outliner TreeView）的节点包裹器
    /// </summary>
    public partial class OutlinerNodeViewModel : ObservableObject
    {
        private readonly SceneNode _node;
        private readonly HashSet<OutlinerNodeViewModel> _knownChildren = new();

        [ObservableProperty]
        private string _name;

        // 重命名支持
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNotRenaming))]
        private bool _isRenaming = false;

        [ObservableProperty]
        private string _editingName = string.Empty;

        public bool IsNotRenaming => !IsRenaming;

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private bool _isLocked;

        partial void OnIsLockedChanged(bool value) => RefreshEffectiveState();
        partial void OnIsSelectedChanged(bool value) { if (value && (IsEffectivelyLocked || !IsEffectivelyVisible)) IsSelected = false; }

        [RelayCommand]
        private void ToggleLock() { if (CanToggleLock) IsLocked = !IsLocked; }

        [ObservableProperty]
        private bool _isExpanded = true;

        public bool IsVisible
        {
            get => _node.Visible;
            set
            {
                if (_node.Visible != value)
                {
                    _node.Visible = value;
                    OnPropertyChanged();
                    RefreshEffectiveState();
                }
            }
        }

        public ObservableCollection<OutlinerNodeViewModel> Children { get; } = new();

        public SceneNode Node => _node;
        private OutlinerNodeViewModel? _parent;
        public OutlinerNodeViewModel? Parent
        {
            get => _parent;
            internal set { if (SetProperty(ref _parent, value)) RefreshEffectiveState(); }
        }
        public bool IsGroup { get; }
        public bool IsEffectivelyLocked => IsLocked || Parent?.IsEffectivelyLocked == true;
        public bool IsEffectivelyVisible => IsVisible && (Parent?.IsEffectivelyVisible ?? true);
        public bool CanToggleLock => Parent?.IsEffectivelyLocked != true;
        public bool CanToggleVisibility => Parent?.IsEffectivelyVisible != false;
        public IEnumerable<OutlinerNodeViewModel> DescendantsAndSelf()
        {
            yield return this;
            foreach (var child in Children) foreach (var node in child.DescendantsAndSelf()) yield return node;
        }
        public bool IsAncestorOf(OutlinerNodeViewModel other)
        {
            for (var parent = other.Parent; parent != null; parent = parent.Parent) if (parent == this) return true;
            return false;
        }
        private void RefreshEffectiveState()
        {
            if (IsEffectivelyLocked || !IsEffectivelyVisible) IsSelected = false;
            OnPropertyChanged(nameof(IsEffectivelyLocked)); OnPropertyChanged(nameof(IsEffectivelyVisible));
            OnPropertyChanged(nameof(CanToggleLock)); OnPropertyChanged(nameof(CanToggleVisibility));
            foreach (var child in Children) child.RefreshEffectiveState();
        }
        partial void OnNameChanged(string value) => _node.Name = value;

        public OutlinerNodeViewModel(SceneNode node, string defaultName, bool isGroup = false)
        {
            _node = node;
            IsGroup = isGroup;
            Children.CollectionChanged += (_, args) =>
            {
                if (args.OldItems != null) foreach (OutlinerNodeViewModel old in args.OldItems) if (old.Parent == this) old.Parent = null;
                // Reset events have no old items; keep a snapshot to detach removed children as well.
                foreach (var old in _knownChildren.Where(x => !Children.Contains(x)).ToArray()) if (old.Parent == this) old.Parent = null;
                _knownChildren.Clear();
                foreach (var child in Children) { child.Parent = this; _knownChildren.Add(child); }
            };
            _name = string.IsNullOrWhiteSpace(node.Name) ? defaultName : node.Name;
            _editingName = _name;
        }

        [RelayCommand]
        private void ToggleExpanded() => IsExpanded = !IsExpanded;

        [RelayCommand]
        private void Rename()
        {
            EditingName = Name;
            IsRenaming = true;
        }

        public void CommitRename()
        {
            if (!string.IsNullOrWhiteSpace(EditingName))
                Name = EditingName;
            IsRenaming = false;
        }

        public void CancelRename()
        {
            EditingName = Name;
            IsRenaming = false;
        }

        [RelayCommand]
        private void ToggleVisibility()
        {
            if (CanToggleVisibility) IsVisible = !IsVisible;
        }

        /// <summary>
        /// 构建模型对象大纲节点
        /// </summary>
        public static OutlinerNodeViewModel BuildTree(SceneNode rootNode, string name = "模型对象")
        {
            var vm = new OutlinerNodeViewModel(rootNode, name);
            return vm;
        }
    }

    // ─── 简单内联 Converter（避免额外文件）──────────────

    /// <summary>bool → Visibility（True→Visible）</summary>
    public class BoolToVisConverter : IValueConverter
    {
        public static readonly BoolToVisConverter Instance = new();
        public object Convert(object v, Type t, object p, CultureInfo c)
            => v is bool b && b ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotSupportedException();
    }

    /// <summary>bool → Visibility（True→Collapsed，反向）</summary>
    public class InverseBoolToVisConverter : IValueConverter
    {
        public static readonly InverseBoolToVisConverter Instance = new();
        public object Convert(object v, Type t, object p, CultureInfo c)
            => v is bool b && b ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotSupportedException();
    }
}
