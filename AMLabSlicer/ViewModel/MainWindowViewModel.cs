using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Windows;
// 【引入下面这两个关键的 Helix 命名空间】
using HelixToolkit.Wpf.SharpDX;
using HelixToolkit.SharpDX.Assimp;
using HelixToolkit.SharpDX;

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
                var importer = new Importer();
                var scene = importer.Load(openFileDialog.FileName);

                if (scene != null && scene.Root != null)
                {
                    // 给模型刷上默认的材质
                    foreach (var node in scene.Root.Traverse())
                    {
                        // 【修复 CS0234 报错】：直接使用全路径指定 MeshNode 的位置
                        if (node is HelixToolkit.SharpDX.Model.Scene.MeshNode meshNode)
                        {
                            meshNode.Material = PhongMaterials.LightGray;
                        }
                    }

                    // 【核心逻辑】：把底层的 SceneNode 包装成 WPF 认识的 SceneNodeGroupModel3D
                    var groupModel = new SceneNodeGroupModel3D();
                    groupModel.AddNode(scene.Root);

                    if (CurrentWorkspace is PrepareWorkspaceViewModel prepVM)
                    {
                        // 把包装好的控件传给界面
                        prepVM.LoadedModel = groupModel;
                    }
                }
                else
                {
                    MessageBox.Show("模型加载失败或文件已损坏！", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}