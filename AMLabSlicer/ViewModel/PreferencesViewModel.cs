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
    }
}
