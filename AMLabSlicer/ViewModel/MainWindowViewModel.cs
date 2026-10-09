using AMLabSlicer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;
using HelixToolkit.Wpf.SharpDX;
using HelixToolkit.SharpDX;
using HelixToolkit.SharpDX.Model.Scene;
using System.Collections.Generic;
using System.Numerics;

namespace AMLabSlicer.ViewModel
{
    public partial class MainWindowViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableObject? _currentWorkspace;

        private readonly IUserDialogService _dialogs;
        private readonly IModelImportService _modelImporter;

        public MainWindowViewModel(PrepareWorkspaceViewModel prepVM, IUserDialogService dialogs, IModelImportService modelImporter)
        {
            _dialogs = dialogs;
            _modelImporter = modelImporter;
            CurrentWorkspace = prepVM;
        }

        // ── Task 2: 异步模型加载 ─────────────────────────────
        // 使用 AsyncRelayCommand 确保文件对话框在主线程，IO/CPU 在后台线程

        [RelayCommand]
        private async Task LoadModelAsync()
        {
            var filePaths = _dialogs.SelectModels();
            if (filePaths.Length == 0) return;
            if (CurrentWorkspace is not PrepareWorkspaceViewModel prepVM) return;

            // 首次导入时创建全局 GroupModel
            SceneNodeGroupModel3D? groupModel = prepVM.LoadedModel as SceneNodeGroupModel3D;
            bool isNewGroupModel = groupModel == null;
            if (isNewGroupModel)
                groupModel = new SceneNodeGroupModel3D();

            var existingNames = new HashSet<string>(prepVM.OutlinerItems.Select(item => item.Name), StringComparer.OrdinalIgnoreCase);

            foreach (var filePath in filePaths)
            {
                // ── 后台线程执行耗时 IO / CPU 工作 ───────────
                SceneNode? meshRootNode = null;
                string fp = filePath; // capture for lambda

                try
                {
                    meshRootNode = await _modelImporter.LoadAsync(fp);
                }
                catch (Exception ex)
                {
                    _dialogs.ShowMessage($"加载失败：{System.IO.Path.GetFileName(fp)}\n{ex.Message}", "错误");
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

                meshRootNode.Name = modelName;
                var pivotNode = meshRootNode;
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
        private void OpenPreferences() => _dialogs.OpenPreferences();

        [RelayCommand]
        private void Undo() => (CurrentWorkspace as PrepareWorkspaceViewModel)?.History.Undo();

        [RelayCommand]
        private void Redo() => (CurrentWorkspace as PrepareWorkspaceViewModel)?.History.Redo();
    }
}
