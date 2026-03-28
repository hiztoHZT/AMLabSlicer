using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using System.Numerics; // 必须引入这个，用于 Vector3 三维坐标
using System.Collections.ObjectModel;
using AMLabSlicer.Core.Parameters;

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

        // 暴露给 UI 绑定的参数集合
        public ObservableCollection<SliceParameter> Parameters { get; }

        private readonly IParameterStore _parameterStore;
        
        public PreferencesViewModel AppPrefs { get; }

        public PrepareWorkspaceViewModel(IParameterStore parameterStore, PreferencesViewModel appPrefs)
        {
            _parameterStore = parameterStore;
            AppPrefs = appPrefs;

            // 演示参数注册 —— 未来由切片算法自动注册，UI 模板自动匹配
            _parameterStore.RegisterParameter(new SliceParameter { Key = "LayerHeight", DisplayName = "层高", Category = "基础", ParameterType = typeof(double), Value = 0.2, Unit = "mm", Description = "每层的打印厚度" });
            _parameterStore.RegisterParameter(new SliceParameter { Key = "InfillDensity", DisplayName = "填充密度", Category = "基础", ParameterType = typeof(double), Value = 20.0, Unit = "%", MinValue = 0, MaxValue = 100, Description = "内部填充的密度百分比" });
            _parameterStore.RegisterParameter(new SliceParameter { Key = "PrintSpeed", DisplayName = "打印速度", Category = "速度", ParameterType = typeof(double), Value = 60.0, Unit = "mm/s", MinValue = 10, MaxValue = 300, Description = "打印头移动速度" });
            _parameterStore.RegisterParameter(new SliceParameter { Key = "PrintTemp", DisplayName = "喷嘴温度", Category = "温度", ParameterType = typeof(double), Value = 210.0, Unit = "°C", Description = "喷嘴加热温度" });
            _parameterStore.RegisterParameter(new SliceParameter { Key = "BedTemp", DisplayName = "热床温度", Category = "温度", ParameterType = typeof(double), Value = 60.0, Unit = "°C", Description = "打印热床温度" });
            _parameterStore.RegisterParameter(new SliceParameter { Key = "GenerateSupport", DisplayName = "生成支撑", Category = "支撑", ParameterType = typeof(bool), Value = true, Description = "是否自动生成支撑结构" });
            _parameterStore.RegisterParameter(new SliceParameter { Key = "SupportType", DisplayName = "支撑类型", Category = "支撑", ParameterType = typeof(string), Value = "树状", Options = new object[] { "普通", "树状", "线性" }, Description = "支撑结构的生成方式" });

            Parameters = new ObservableCollection<SliceParameter>(_parameterStore.GetAllParameters());

            // 在工作区初始化时，立刻生成切片平台网格
            GeneratePlatformGrid();
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

            // 1. 沿着 X 轴画线（平行于 Y 轴的线）以原点 0,0 居中对称
            for (int x = -halfWidth; x <= halfWidth; x++)
            {
                // 如果能被 10 整除，就是主线（粗线），否则是细线
                if (x % 10 == 0)
                {
                    majorBuilder.AddLine(new Vector3(x, -halfDepth, 0), new Vector3(x, halfDepth, 0));
                }
                else
                {
                    minorBuilder.AddLine(new Vector3(x, -halfDepth, 0), new Vector3(x, halfDepth, 0));
                }
            }

            // 2. 沿着 Y 轴画线（平行于 X 轴的线）以原点 0,0 居中对称
            for (int y = -halfDepth; y <= halfDepth; y++)
            {
                if (y % 10 == 0)
                {
                    majorBuilder.AddLine(new Vector3(-halfWidth, y, 0), new Vector3(halfWidth, y, 0));
                }
                else
                {
                    minorBuilder.AddLine(new Vector3(-halfWidth, y, 0), new Vector3(halfWidth, y, 0));
                }
            }

            // 将打包好的线框数据转换为渲染引擎认识的 Geometry3D，并绑定给前台
            MajorGridGeometry = majorBuilder.ToLineGeometry3D();
            MinorGridGeometry = minorBuilder.ToLineGeometry3D();
        }
    }
}