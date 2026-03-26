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

            // 这里可以初始化一些默认参数供测试
            _parameterStore.RegisterParameter(new SliceParameter { Key = "LayerHeight", DisplayName = "层高 (mm)", Category = "基础", ParameterType = typeof(double), Value = 0.2 });
            _parameterStore.RegisterParameter(new SliceParameter { Key = "InfillDensity", DisplayName = "填充密度 (%)", Category = "基础", ParameterType = typeof(double), Value = 20.0 });
            _parameterStore.RegisterParameter(new SliceParameter { Key = "GenerateSupport", DisplayName = "生成支撑", Category = "支撑", ParameterType = typeof(bool), Value = true });

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