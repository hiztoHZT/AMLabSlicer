using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Windows;
using HelixToolkit.Wpf.SharpDX;
using HelixToolkit.SharpDX.Assimp;
using HelixToolkit.SharpDX;
using AMLabSlicer.Views;
using SharpAssimp;

namespace AMLabSlicer.ViewModel
{
    public partial class MainWindowViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableObject? _currentWorkspace;

        public MainWindowViewModel()
        {
            CurrentWorkspace = new PrepareWorkspaceViewModel();
        }

        [RelayCommand]
        private void LoadModel()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "STL 模型文件 (*.stl)|*.stl|所有文件 (*.*)|*.*",
                Title = "选择要切片的 3D 模型"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                // 实例化 importer
                var importer = new Importer();

                // 使用 SharpAssimp 的枚举来开启自动平滑和切线计算
                importer.Configuration.AssimpPostProcessSteps =
                    SharpAssimp.PostProcessSteps.JoinIdenticalVertices |
                    SharpAssimp.PostProcessSteps.GenerateSmoothNormals |
                    SharpAssimp.PostProcessSteps.CalculateTangentSpace;

                // 加载模型
                var scene = importer.Load(openFileDialog.FileName);

                if (scene != null && scene.Root != null)
                {
                    // 调配专业的“切片机哑光材质”
                    var slicerMaterial = new HelixToolkit.SharpDX.Model.PhongMaterialCore()
                    {
                        DiffuseColor = new HelixToolkit.Maths.Color4(225f / 255f, 225f / 255f, 225f / 255f, 1f),
                        AmbientColor = new HelixToolkit.Maths.Color4(220f / 255f, 220f / 255f, 220f / 255f, 1f),
                        SpecularColor = new HelixToolkit.Maths.Color4(30f / 255f, 30f / 255f, 30f / 255f, 1f),
                        SpecularShininess = 5f
                    };

                    //将材质刷给模型
                    foreach (var node in scene.Root.Traverse())
                    {
                        if (node is HelixToolkit.SharpDX.Model.Scene.MeshNode meshNode)
                        {
                            meshNode.Material = slicerMaterial;
                        }
                    }

                    //把底层的 SceneNode 包装成 WPF 认识的 SceneNodeGroupModel3D
                    var groupModel = new SceneNodeGroupModel3D();
                    groupModel.AddNode(scene.Root);

                    //传递给界面显示
                    if (CurrentWorkspace is PrepareWorkspaceViewModel prepVM)
                    {
                        prepVM.LoadedModel = groupModel;
                    }
                }
                else
                {
                    MessageBox.Show("模型加载失败或文件已损坏！", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        private void OpenPreferences()
        {
            var prefWindow = new PreferencesWindow();
            prefWindow.Owner = Application.Current.MainWindow;
            prefWindow.ShowDialog();
        }
    }
}