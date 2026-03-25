using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using System.Numerics; // 必须引入这个，用于 Vector3 三维坐标

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

        public PrepareWorkspaceViewModel()
        {
            // 在工作区初始化时，立刻生成切片平台网格
            GeneratePlatformGrid();
        }

        /// <summary>
        /// 生成 255 x 255 的 3D 打印平台网格
        /// </summary>
        private void GeneratePlatformGrid()
        {
            var majorBuilder = new LineBuilder();
            var minorBuilder = new LineBuilder();

            // 设定平台尺寸
            int width = 255;
            int depth = 255;

            // 假设机床原点 (0,0,0) 在左前角，平台向 X 和 Y 的正方向延伸
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