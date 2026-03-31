using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Windows;
using HelixToolkit.Wpf.SharpDX;
using HelixToolkit.SharpDX.Assimp;
using HelixToolkit.SharpDX;
using HelixToolkit.SharpDX.Model.Scene;
using AMLabSlicer.Views;
using SharpAssimp;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Numerics;

namespace AMLabSlicer.ViewModel
{
    public partial class MainWindowViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableObject? _currentWorkspace;

        public MainWindowViewModel(PrepareWorkspaceViewModel prepVM)
        {
            CurrentWorkspace = prepVM;
        }

        private static Matrix4x4 GetWorldModelMatrix(SceneNode node)
        {
            // node.ModelMatrix 为相对父节点的局部矩阵；
            // 这里把父链逐层相乘，得到相对 scene.Root 的矩阵。
            var stack = new Stack<SceneNode>();
            SceneNode? cur = node;
            while (cur != null)
            {
                stack.Push(cur);
                cur = cur.Parent;
            }

            var m = Matrix4x4.Identity;
            while (stack.Count > 0)
                m = m * stack.Pop().ModelMatrix;
            return m;
        }

        [RelayCommand]
        private void LoadModel()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "3D 模型文件 (*.stl;*.obj;*.3mf)|*.stl;*.obj;*.3mf|所有文件 (*.*)|*.*",
                Title = "选择要切片的 3D 模型",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() != true) return;

            if (CurrentWorkspace is not PrepareWorkspaceViewModel prepVM) return;

            // 首次导入时创建全局 GroupModel（若已存在则复用）
            SceneNodeGroupModel3D? groupModel = prepVM.LoadedModel as SceneNodeGroupModel3D;
            bool isNewGroupModel = groupModel == null;
            if (isNewGroupModel)
                groupModel = new SceneNodeGroupModel3D();

            // 统计已存在的同名模型，用于自动编号
            var existingNames = new System.Collections.Generic.HashSet<string>();
            foreach (var filePath in openFileDialog.FileNames)
            {
                var importer = new Importer();
                importer.Configuration.AssimpPostProcessSteps =
                    SharpAssimp.PostProcessSteps.JoinIdenticalVertices |
                    SharpAssimp.PostProcessSteps.GenerateSmoothNormals |
                    SharpAssimp.PostProcessSteps.CalculateTangentSpace;

                var scene = importer.Load(filePath);
                if (scene == null || scene.Root == null)
                {
                    MessageBox.Show($"模型加载失败：{System.IO.Path.GetFileName(filePath)}",
                        "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    continue;
                }

                // 应用材质
                var mat = new HelixToolkit.SharpDX.Model.PhongMaterialCore()
                {
                    DiffuseColor  = new HelixToolkit.Maths.Color4(225f/255f, 225f/255f, 225f/255f, 1f),
                    AmbientColor  = new HelixToolkit.Maths.Color4(220f/255f, 220f/255f, 220f/255f, 1f),
                    SpecularColor = new HelixToolkit.Maths.Color4(30f/255f,  30f/255f,  30f/255f,  1f),
                    SpecularShininess = 5f
                };
                foreach (var node in scene.Root.Traverse())
                    if (node is HelixToolkit.SharpDX.Model.Scene.MeshNode mn2) mn2.Material = mat;

                // 自动命名
                string baseName  = System.IO.Path.GetFileNameWithoutExtension(filePath);
                string modelName = baseName;
                int suffix = 2;
                while (existingNames.Contains(modelName))
                    modelName = $"{baseName} ({suffix++})";
                existingNames.Add(modelName);

                // ── 计算几何包围盒（从顶点直接算，不依赖渲染管线）──
                // 注意：必须考虑 MeshNode 的父链变换，否则 pivot 中心会偏移。
                float minX = float.MaxValue, maxX = float.MinValue;
                float minY = float.MaxValue, maxY = float.MinValue;
                float minZ = float.MaxValue, maxZ = float.MinValue;
                foreach (var node in scene.Root.Traverse())
                {
                    if (node is HelixToolkit.SharpDX.Model.Scene.MeshNode mn3
                        && mn3.Geometry?.Positions != null)
                    {
                        var worldM = GetWorldModelMatrix(mn3);
                        foreach (var pos in mn3.Geometry.Positions)
                        {
                            // 将顶点变换到 scene.Root 空间（考虑父链矩阵）
                            var wp = Vector3.Transform(pos, worldM);
                            if (wp.X < minX) minX = wp.X;  if (wp.X > maxX) maxX = wp.X;
                            if (wp.Y < minY) minY = wp.Y;  if (wp.Y > maxY) maxY = wp.Y;
                            if (wp.Z < minZ) minZ = wp.Z;
                            if (wp.Z > maxZ) maxZ = wp.Z;
                        }
                    }
                }
                float cx = (minX == float.MaxValue) ? 0f : (minX + maxX) * 0.5f;
                float cy = (minY == float.MaxValue) ? 0f : (minY + maxY) * 0.5f;
                if (minZ == float.MaxValue)
                {
                    minZ = 0f;
                    maxZ = 0f;
                }
                float cz = (minZ + maxZ) * 0.5f;

                // ── 双层枢轴包装 ──
                // 外层 pivot：放在“几何中心”（经过贴地后）的 World 位置 → Gizmo 跟随此节点
                // 内层 offset：将 scene.Root 偏移使其局部原点 = 几何中心
                //   world_vertex = pivot.ModelMatrix * offset.ModelMatrix * local_vertex
                //                = Trans(cx,cy,cz-minZ) * Trans(-cx,-cy,-cz) * (vx,vy,vz)
                //                = (vx, vy, vz - minZ)   ← 正确：底面贴地，且局部原点=几何中心
                var pivotNode = new GroupNode
                {
                    Name        = modelName,
                    ModelMatrix = System.Numerics.Matrix4x4.CreateTranslation(cx, cy, cz - minZ)
                };
                // 对 scene.Root 施加偏移（使几何中心成为局部原点）
                // 这里用“预乘”，确保平移发生在 scene.Root 局部坐标系中，
                // 避免 scene.Root 原有 ModelMatrix 不为 Identity 时导致中心不对齐。
                scene.Root.ModelMatrix =
                    System.Numerics.Matrix4x4.CreateTranslation(-cx, -cy, -cz) *
                    scene.Root.ModelMatrix;
                scene.Root.Name = modelName + "_mesh";

                pivotNode.AddChildNode(scene.Root);
                groupModel!.AddNode(pivotNode);

                // 同步大纲（以 pivotNode 为根）
                var outlinerNode = OutlinerNodeViewModel.BuildTree(pivotNode, modelName);
                prepVM.OutlinerItems.Add(outlinerNode);
            }

            // 首次导入：节点全部就绪后再绑定到视图
            if (isNewGroupModel)
                prepVM.LoadedModel = groupModel;
        }

        [RelayCommand]
        private void OpenPreferences()
        {
            var prefWindow = App.AppHost!.Services.GetRequiredService<PreferencesWindow>();
            prefWindow.Owner = Application.Current.MainWindow;
            prefWindow.ShowDialog();
        }

        /// <summary>
        /// 通过 PrepareWorkspaceViewModel 转发到视图的 CommandDispatcher。
        /// 视图在 DataContextChanged 时将自身 CommandDispatcher 反注册到 VM（弱引用避免泄漏）。
        /// 简单起见，此处通过查找视觉树获取 ModelPreviewView。
        /// </summary>
        [RelayCommand]
        private void Undo()
        {
            GetActiveViewport()?.CommandDispatcher.Undo();
        }

        [RelayCommand]
        private void Redo()
        {
            GetActiveViewport()?.CommandDispatcher.Redo();
        }

        private static AMLabSlicer.Views.ModelPreviewView? GetActiveViewport()
        {
            if (Application.Current.MainWindow is not Window w) return null;
            return FindVisualChild<AMLabSlicer.Views.ModelPreviewView>(w);
        }

        private static T? FindVisualChild<T>(System.Windows.DependencyObject parent) where T : System.Windows.DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }
    }
}
