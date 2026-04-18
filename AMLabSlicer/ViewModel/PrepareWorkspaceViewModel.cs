using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using System.Numerics;
using System.Collections.ObjectModel;
using AMLabSlicer.Core.Parameters;
using AMLabSlicer.State;

namespace AMLabSlicer.ViewModel
{
    public partial class PrepareWorkspaceViewModel : ObservableObject
    {
        // 存放载入的 3D 模型
        [ObservableProperty]
        private Element3D? _loadedModel;

        // 存放主网格数据 (每 10mm 一根)
        [ObservableProperty]
        private Geometry3D? _majorGridGeometry;

        // 存放细网格数据 (每 1mm 一根)
        [ObservableProperty]
        private Geometry3D? _minorGridGeometry;

        // 视口模式状态机
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsObjectMode))]
        [NotifyPropertyChangedFor(nameof(IsFaceMode))]
        private ViewportMode _viewportMode = ViewportMode.ObjectMode;

        public bool IsObjectMode => _viewportMode == ViewportMode.ObjectMode;
        public bool IsFaceMode => _viewportMode == ViewportMode.FaceMode;

        [RelayCommand]
        private void ToggleViewportMode()
        {
            ViewportMode = ViewportMode == ViewportMode.ObjectMode
                ? ViewportMode.FaceMode
                : ViewportMode.ObjectMode;
        }

        // 左侧面板开关状态
        [ObservableProperty]
        private bool _isParameterPanelOpen = true;

        [RelayCommand]
        private void TogglePanel() => IsParameterPanelOpen = !IsParameterPanelOpen;

        // 面向大纲视图的模型节点树
        public ObservableCollection<OutlinerNodeViewModel> OutlinerItems { get; } = new ObservableCollection<OutlinerNodeViewModel>();

        // 可选切片算法集合
        public ObservableCollection<string> SlicingAlgorithms { get; } = new ObservableCollection<string>
        {
            "标准切片 (Standard)",
            "高性能切片 (High Performance)",
            "精细打磨 (High Detail)"
        };

        private string _selectedAlgorithm = "标准切片 (Standard)";
        public string SelectedAlgorithm
        {
            get => _selectedAlgorithm;
            set
            {
                if (SetProperty(ref _selectedAlgorithm, value))
                {
                    RebuildParametersForAlgorithm(value);
                }
            }
        }

        // 暴露给 UI 绑定的参数集合
        public ObservableCollection<SliceParameter> Parameters { get; } = new ObservableCollection<SliceParameter>();

        private readonly IParameterStore _parameterStore;
        
        public PreferencesViewModel AppPrefs { get; }

        public PrepareWorkspaceViewModel(IParameterStore parameterStore, PreferencesViewModel appPrefs)
        {
            _parameterStore = parameterStore;
            AppPrefs = appPrefs;

            // 初始化算法切换事件
            RebuildParametersForAlgorithm(_selectedAlgorithm);

            // 在工作区初始化时，立刻生成切片平台网格
            GeneratePlatformGrid();
        }

        private void RebuildParametersForAlgorithm(string algorithm)
        {
            // 缓存旧参数用于继承
            var oldParams = _parameterStore.GetAllParameters().ToDictionary(p => p.Key, p => p.Value);

            // 清理旧参数
            _parameterStore.ClearAll();

            // 辅助方法：判断并继承旧值（同名参数则优先用旧值覆盖指定的默认值）
            void Register(SliceParameter p)
            {
                if (oldParams.TryGetValue(p.Key, out var oldVal))
                {
                    p.Value = oldVal;
                }
                _parameterStore.RegisterParameter(p);
            }

            if (algorithm == "标准切片 (Standard)")
            {
                Register(new SliceParameter { Key = "LayerHeight", DisplayName = "层高", Category = "分层支持", ControlType = UIControlType.NumericBox, Value = 0.2, Unit = "mm", Description = "每层的打印厚度" });
                Register(new SliceParameter { Key = "InfillDensity", DisplayName = "填充密度", Category = "强度填充", ControlType = UIControlType.Slider, Value = 20.0, Unit = "%", MinValue = 0, MaxValue = 100, Description = "内部填充的密度百分比" });
                Register(new SliceParameter { Key = "PrintSpeed", DisplayName = "打印速度", Category = "速度热力", ControlType = UIControlType.Slider, Value = 60.0, Unit = "mm/s", MinValue = 10, MaxValue = 300, Description = "打印头移动速度" });
            }
            else if (algorithm == "高性能切片 (High Performance)")
            {
                Register(new SliceParameter { Key = "AccelSpeed", DisplayName = "加速度", Category = "速度热力", ControlType = UIControlType.NumericBox, Value = 3000.0, Unit = "mm/s²", Description = "最大加速度" });
                Register(new SliceParameter { Key = "Jerk", DisplayName = "Jerk (急动)", Category = "速度热力", ControlType = UIControlType.NumericBox, Value = 15.0, Unit = "mm/s", Description = "角速度改变补偿" });
                Register(new SliceParameter { Key = "FlowRate", DisplayName = "挤出流量", Category = "挤出成型", ControlType = UIControlType.Slider, Value = 105.0, Unit = "%", MinValue = 90, MaxValue = 150 });
            }
            else
            {
                Register(new SliceParameter { Key = "LayerHeight", DisplayName = "层高", Category = "分层支持", ControlType = UIControlType.NumericBox, Value = 0.08, Unit = "mm", Description = "极其精细的层高" });
                Register(new SliceParameter { Key = "SurfaceSmooth", DisplayName = "表面平滑", Category = "质量", ControlType = UIControlType.CheckBox, Value = true });
            }

            Parameters.Clear();
            foreach (var p in _parameterStore.GetAllParameters())
            {
                Parameters.Add(p);
            }
        }
        /// <summary>
        /// </summary>
        private void GeneratePlatformGrid()
        {
            var majorBuilder = new LineBuilder();
            var minorBuilder = new LineBuilder();

            // 设定平台尺寸 225
            int width = 225;
            int depth = 225;
            
            int halfWidth = width / 2;
            int halfDepth = depth / 2;

            // 1. 沿着 X 轴画线（平行于 Y 轴的线）
            for (int x = 0; x <= width; x++)
            {
                // 如果能被 10 整除，就是主线（粗线），否则是细线
                if (x % 10 == 0)
                {
                    majorBuilder.AddLine(new Vector3(x, 0, 0), new Vector3(x, depth, 0));
                }
                else
                {
                    minorBuilder.AddLine(new Vector3(x, 0, 0), new Vector3(x, depth, 0));
                }
            }

            // 2. 沿着 Y 轴画线（平行于 X 轴的线）
            for (int y = 0; y <= depth; y++)
            {
                if (y % 10 == 0)
                {
                    majorBuilder.AddLine(new Vector3(0, y, 0), new Vector3(width, y, 0));
                }
                else
                {
                    minorBuilder.AddLine(new Vector3(0, y, 0), new Vector3(width, y, 0));
                }
            }

            // 将打包好的线框数据转换为渲染引擎认识的 Geometry3D，并绑定给前台
            MajorGridGeometry = majorBuilder.ToLineGeometry3D();
            MinorGridGeometry = minorBuilder.ToLineGeometry3D();
        }
    }
}