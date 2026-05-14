using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Windows;
using System.Threading.Tasks;
using HelixToolkit.Wpf.SharpDX;
using HelixToolkit.SharpDX.Assimp;
using HelixToolkit.SharpDX;
using HelixToolkit.SharpDX.Model.Scene;
using AMLabSlicer.Views;
using AMLabSlicer.Occt;
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
            var stack = new Stack<SceneNode>();
            SceneNode? cur = node;
            while (cur != null) { stack.Push(cur); cur = cur.Parent; }

            var m = Matrix4x4.Identity;
            while (stack.Count > 0)
                m = m * stack.Pop().ModelMatrix;
            return m;
        }

        // ── Task 2: 异步模型加载 ─────────────────────────────
        // 使用 AsyncRelayCommand 确保文件对话框在主线程，IO/CPU 在后台线程

        [RelayCommand]
        private async Task LoadModelAsync()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "3D 模型文件 (*.stl;*.obj;*.3mf;*.step;*.stp)|*.stl;*.obj;*.3mf;*.step;*.stp|" +
                         "STEP 文件 (*.step;*.stp)|*.step;*.stp|" +
                         "网格文件 (*.stl;*.obj;*.3mf)|*.stl;*.obj;*.3mf|" +
                         "所有文件 (*.*)|*.*",
                Title = "选择要切片的 3D 模型",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() != true) return;
            if (CurrentWorkspace is not PrepareWorkspaceViewModel prepVM) return;

            // 首次导入时创建全局 GroupModel
            SceneNodeGroupModel3D? groupModel = prepVM.LoadedModel as SceneNodeGroupModel3D;
            bool isNewGroupModel = groupModel == null;
            if (isNewGroupModel)
                groupModel = new SceneNodeGroupModel3D();

            var existingNames = new HashSet<string>();
            string[] filePaths = openFileDialog.FileNames;

            foreach (var filePath in filePaths)
            {
                // ── 后台线程执行耗时 IO / CPU 工作 ───────────
                SceneNode? meshRootNode = null;
                string fp = filePath; // capture for lambda

                try
                {
                    meshRootNode = await Task.Run(() =>
                        OcctInteropService.IsStepFile(fp)
                            ? LoadStepFile(fp)
                            : LoadMeshFileViaAssimp(fp)
                    );
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"加载失败：{System.IO.Path.GetFileName(fp)}\n{ex.Message}",
                        "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    continue;
                }

                if (meshRootNode == null) continue;

                // ── 以下代码回到主线程执行（await 后自动切回）──

                // 自动命名
                string baseName  = System.IO.Path.GetFileNameWithoutExtension(filePath);
                string modelName = baseName;
                int suffix = 2;
                while (existingNames.Contains(modelName))
                    modelName = $"{baseName} ({suffix++})";
                existingNames.Add(modelName);

                // 计算几何包围盒（从顶点直接算）
                float minX = float.MaxValue, maxX = float.MinValue;
                float minY = float.MaxValue, maxY = float.MinValue;
                float minZ = float.MaxValue, maxZ = float.MinValue;

                foreach (var node in meshRootNode.Traverse())
                {
                    if (node is MeshNode mn && mn.Geometry?.Positions != null)
                    {
                        var worldM = GetWorldModelMatrix(mn);
                        foreach (var pos in mn.Geometry.Positions)
                        {
                            var wp = Vector3.Transform(pos, worldM);
                            if (wp.X < minX) minX = wp.X; if (wp.X > maxX) maxX = wp.X;
                            if (wp.Y < minY) minY = wp.Y; if (wp.Y > maxY) maxY = wp.Y;
                            if (wp.Z < minZ) minZ = wp.Z; if (wp.Z > maxZ) maxZ = wp.Z;
                        }
                    }
                }

                float cx = (minX == float.MaxValue) ? 0f : (minX + maxX) * 0.5f;
                float cy = (minY == float.MaxValue) ? 0f : (minY + maxY) * 0.5f;
                if (minZ == float.MaxValue) { minZ = 0f; maxZ = 0f; }
                float cz = (minZ + maxZ) * 0.5f;          // AABB 几何中心 Z
                float halfH = (maxZ - minZ) * 0.5f;        // 模型半高

                // ── Task 4: 初始摆放 —— 模型底面贴地，XY 居中在世界原点 ──
                // pivotNode 的 Translation = AABB 几何中心 (0, 0, cz_world)
                //   → TransformManipulator3D.Target = pivotNode 时，Gizmo 自动显示在几何中心
                // meshRootNode 局部坐标: 将几何中心移到 pivotNode 的局部原点
                //   → meshRootNode.LocalTranslation = (−cx, −cy, −cz)
                // 底面贴地条件: pivotNode.Z = halfH (= cz when minZ=0)
                var pivotNode = new GroupNode
                {
                    Name        = modelName,
                    // 将几何中心对齐世界 XY 中心，Z 移到 halfH 使底面贴 Z=0
                    ModelMatrix = Matrix4x4.CreateTranslation(0f, 0f, halfH)
                };
                // 将几何中心移到 pivotNode 的局部原点
                meshRootNode.ModelMatrix =
                    Matrix4x4.CreateTranslation(-cx, -cy, -cz) * meshRootNode.ModelMatrix;
                meshRootNode.Name = modelName + "_mesh";

                pivotNode.AddChildNode(meshRootNode);
                groupModel!.AddNode(pivotNode);

                // 同步大纲
                var outlinerNode = OutlinerNodeViewModel.BuildTree(pivotNode, modelName);
                prepVM.OutlinerItems.Add(outlinerNode);
            }

            // 首次导入完成后绑定到视图
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

        [RelayCommand]
        private void Undo() => GetActiveViewport()?.CommandDispatcher.Undo();

        [RelayCommand]
        private void Redo() => GetActiveViewport()?.CommandDispatcher.Redo();

        // ── STEP 文件加载 ────────────────────────────────────
        // 在后台线程中调用，不得触碰任何 UI 对象

        private static SceneNode? LoadStepFile(string filePath)
        {
            var occtService = new OcctInteropService();
            // 使用推荐的细分参数：0.1mm 线性偏差，0.5rad 角度偏差
            var meshGeometry = occtService.LoadStepModel(filePath, 0.1, 0.5);
            if (meshGeometry == null) return null;

            // 用 MeshGeometry3D 构造一个 MeshNode
            var meshNode = new MeshNode
            {
                Name     = System.IO.Path.GetFileNameWithoutExtension(filePath),
                Geometry = meshGeometry,
                Material = CreateDefaultMaterial(),
                CullMode = SharpDX.Direct3D11.CullMode.None,  // 双面渲染避免翻转时漏面
            };

            var rootGroup = new GroupNode { Name = meshNode.Name + "_root" };
            rootGroup.AddChildNode(meshNode);
            return rootGroup;
        }

        // ── Assimp 网格加载 ──────────────────────────────────

        private static SceneNode? LoadMeshFileViaAssimp(string filePath)
        {
            var importer = new Importer();
            importer.Configuration.AssimpPostProcessSteps =
                PostProcessSteps.JoinIdenticalVertices |
                PostProcessSteps.GenerateSmoothNormals |
                PostProcessSteps.CalculateTangentSpace;

            var scene = importer.Load(filePath);
            if (scene == null || scene.Root == null)
                return null;

            var mat = CreateDefaultMaterial();
            foreach (var node in scene.Root.Traverse())
                if (node is MeshNode mn) mn.Material = mat;

            return scene.Root;
        }

        // ── 材质工厂 ─────────────────────────────────────────

        private static HelixToolkit.SharpDX.Model.PhongMaterialCore CreateDefaultMaterial()
            => new HelixToolkit.SharpDX.Model.PhongMaterialCore
            {
                DiffuseColor      = new HelixToolkit.Maths.Color4(225f/255f, 225f/255f, 225f/255f, 1f),
                AmbientColor      = new HelixToolkit.Maths.Color4(220f/255f, 220f/255f, 220f/255f, 1f),
                SpecularColor     = new HelixToolkit.Maths.Color4(30f/255f,  30f/255f,  30f/255f,  1f),
                SpecularShininess = 5f,
            };

        // ── Viewport 辅助 ────────────────────────────────────

        private static ModelPreviewView? GetActiveViewport()
        {
            if (Application.Current.MainWindow is not Window w) return null;
            return FindVisualChild<ModelPreviewView>(w);
        }

        private static T? FindVisualChild<T>(System.Windows.DependencyObject parent)
            where T : System.Windows.DependencyObject
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
