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

        [ObservableProperty]
        private string _name;

        // 重命名支持
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNotRenaming))]
        private bool _isRenaming = false;

        [ObservableProperty]
        private string _editingName = string.Empty;

        public bool IsNotRenaming => !_isRenaming;

        [ObservableProperty]
        private bool _isSelected;

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
                    foreach (var child in Children)
                        child.IsVisible = value;
                }
            }
        }

        public ObservableCollection<OutlinerNodeViewModel> Children { get; } = new();

        // 面片组数据
        public System.Collections.Generic.List<int>? FaceIndices { get; set; }
        public bool IsFaceGroup => FaceIndices != null;

        public SceneNode Node => _node;

        public OutlinerNodeViewModel(SceneNode node, string defaultName)
        {
            _node = node;
            _name = string.IsNullOrWhiteSpace(node.Name) ? defaultName : node.Name;
            _editingName = _name;
        }

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
            IsVisible = !IsVisible;
        }

        /// <summary>
        /// 从 GroupNode 递归构建大纲树
        /// </summary>
        public static OutlinerNodeViewModel BuildTree(SceneNode rootNode, string name = "模型对象")
        {
            var vm = new OutlinerNodeViewModel(rootNode, name);
            if (rootNode is GroupNode group)
            {
                int index = 1;
                foreach (var child in group.Items)
                {
                    if (child is MeshNode || child is GroupNode)
                    {
                        vm.Children.Add(BuildTree(child, child.Name ?? $"子部件 {index++}"));
                    }
                }
            }
            return vm;
        }
    }

    // ─── 简单内联 Converter（避免额外文件）──────────────

    /// <summary>节点类型图标：面片组显示◆，普通网格显示○</summary>
    public class OutlinerIconConverter : IValueConverter
    {
        public static readonly OutlinerIconConverter Instance = new();
        public object Convert(object v, Type t, object p, CultureInfo c)
            => v is bool b && b ? "◆" : "○";
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotSupportedException();
    }

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
