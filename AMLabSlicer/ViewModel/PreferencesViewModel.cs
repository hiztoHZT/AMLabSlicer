using CommunityToolkit.Mvvm.ComponentModel;
using HelixToolkit.Wpf.SharpDX;
using HelixToolkit.SharpDX;

namespace AMLabSlicer.ViewModel
{
    public partial class PreferencesViewModel : ObservableObject
    {
        private bool _enableFXAA = true; // 后处理消除锯齿
        public bool EnableFXAA
        {
            get => _enableFXAA;
            set
            {
                SetProperty(ref _enableFXAA, value);
                OnPropertyChanged(nameof(FXAALevel));
            }
        }

        public FXAALevel FXAALevel => _enableFXAA ? FXAALevel.High : FXAALevel.None;

        [ObservableProperty]
        private bool _enableSSAO = false; // 屏幕空间环境光遮蔽 (较耗性能，默认可选)

        [ObservableProperty]
        private bool _showCoordinateSystem = true; // 显示左下角坐标轴

        [ObservableProperty]
        private bool _showViewCube = true; // 显示右上角视角控制立方体

        [ObservableProperty]
        private bool _useOrthographic = false; // 使用正交视角

        // ── 操作确认首选项 ──
        [ObservableProperty]
        private bool _enableDeleteConfirm = true;

        [ObservableProperty]
        private bool _enableArrangeConfirm = true;

        [ObservableProperty]
        private bool _enableSplitConfirm = true;

        [ObservableProperty]
        private bool _splitUndoable = false; // 拆分操作是否可撤销（默认关，占用内存较大）

        [ObservableProperty]
        private int _undoStackDepth = 25; // 撤销栈深度
    }
}
