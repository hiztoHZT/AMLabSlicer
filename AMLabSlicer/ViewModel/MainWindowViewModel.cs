using AMLabSlicer.Services;
using AMLabSlicer.Amlab_Monitor;
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
        [NotifyPropertyChangedFor(nameof(IsPrepareWorkspaceActive), nameof(IsAmlab_MonitorWorkspaceActive))]
        [NotifyCanExecuteChangedFor(nameof(LoadModelCommand), nameof(ImportDroppedModelsCommand), nameof(UndoCommand), nameof(RedoCommand))]
        private ObservableObject _currentWorkspace;

        public PrepareWorkspaceViewModel PrepareWorkspace { get; }
        private readonly Amlab_MonitorViewModel _amlab_MonitorWorkspace;

        public bool IsPrepareWorkspaceActive => ReferenceEquals(CurrentWorkspace, PrepareWorkspace);
        public bool IsAmlab_MonitorWorkspaceActive => ReferenceEquals(CurrentWorkspace, _amlab_MonitorWorkspace);

        private readonly IUserDialogService _dialogs;
        private readonly IModelImportService _modelImporter;

        public MainWindowViewModel(PrepareWorkspaceViewModel prepVM, IUserDialogService dialogs, IModelImportService modelImporter, PluginManager? plugins = null,
            Yolo11EngineProcess? yolo11 = null)
        {
            _dialogs = dialogs;
            _modelImporter = modelImporter;
            _amlab_MonitorWorkspace = yolo11 is null ? new Amlab_MonitorViewModel() : new Amlab_MonitorViewModel(yolo11);
            PrepareWorkspace = prepVM;
            _currentWorkspace = prepVM;
            if (plugins is not null)
            {
                plugins.ModelImportHandler = ImportPluginModelAsync;
                plugins.ReadSliceParameters = () => IsPrepareWorkspaceActive && !IsProjectBusy
                    ? prepVM.ReadAgentSliceParameters() : null;
                plugins.ApplySliceParameters = (snapshot, changes) =>
                {
                    if (!IsPrepareWorkspaceActive || IsProjectBusy || prepVM.StartSlicingCommand.IsRunning)
                        throw new InvalidOperationException("当前无法修改切片参数。");
                    prepVM.ApplyAgentSliceParameters(snapshot, changes);
                };
            }
        }

        [RelayCommand]
        private void ShowPrepareWorkspace() => CurrentWorkspace = PrepareWorkspace;

        [RelayCommand]
        private void ShowAmlab_MonitorWorkspace() => CurrentWorkspace = _amlab_MonitorWorkspace;

        private AMLabSlicer.Views.DeviceControlWindow? deviceWindow;
        [RelayCommand]
        private void OpenDevice()
        {
            if (deviceWindow != null) { deviceWindow.Activate(); return; }
            deviceWindow = new AMLabSlicer.Views.DeviceControlWindow { Owner = System.Windows.Application.Current.MainWindow };
            deviceWindow.Closed += (_, _) => deviceWindow = null;
            deviceWindow.Show();
        }

        private readonly System.Threading.SemaphoreSlim _pluginImportGate = new(1);
        private async Task ImportPluginModelAsync(string path, CancellationToken token)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher
                ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;
            if (!dispatcher.CheckAccess())
            {
                await dispatcher.InvokeAsync(() => ImportPluginModelAsync(path, token)).Task.Unwrap();
                return;
            }
            path = System.IO.Path.GetFullPath(path);
            if (!System.IO.File.Exists(path)) throw new System.IO.FileNotFoundException("模型文件不存在", path);
            if (!new[] { ".glb", ".gltf", ".stl", ".obj", ".ply", ".3mf" }.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant()))
                throw new InvalidOperationException("不支持的插件模型格式");
            await _pluginImportGate.WaitAsync(token);
            SceneNode? root = null;
            try
            {
                var prep = PrepareWorkspace;
                root = await _modelImporter.LoadAsync(path) ?? throw new InvalidOperationException("模型为空");
                token.ThrowIfCancellationRequested();
                var name = System.IO.Path.GetFileNameWithoutExtension(path);
                var baseName = name;
                var suffix = 2;
                while (prep.OutlinerItems.Any(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase))) name = $"{baseName} ({suffix++})";
                root.Name = name;
                var tree = OutlinerNodeViewModel.BuildTree(root, name);
                var group = prep.LoadedModel as SceneNodeGroupModel3D ?? new SceneNodeGroupModel3D();
                var node = root;
                bool firstImport = true;
                prep.History.ExecuteCommand(new PluginImportCommand(
                    () => { group.AddNode(node); prep.OutlinerItems.Add(tree); prep.LoadedModel = group;
                        if (firstImport) { prep.ArrangeImportedModel(node); firstImport = false; } },
                    () => { group.RemoveNode(node); prep.OutlinerItems.Remove(tree); }));
                root = null; // Host/history now owns the scene node.
            }
            finally { root?.Dispose(); _pluginImportGate.Release(); }
        }

        private sealed class PluginImportCommand(Action execute, Action undo) : AMLabSlicer.Core.Commands.ICommandAction
        {
            public string Name => "导入插件模型";
            public void Execute() => execute();
            public void Undo() => undo();
        }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(LoadModelCommand), nameof(ImportDroppedModelsCommand), nameof(OpenProjectCommand), nameof(SaveProjectCommand), nameof(SaveProjectAsCommand))]
        private bool _isImportingModels;

        private bool CanImportModels() => IsPrepareWorkspaceActive && !IsImportingModels && !IsProjectBusy;

        [RelayCommand(CanExecute = nameof(CanImportModels))]
        private Task LoadModelAsync() => ImportModelsAsync(_dialogs.SelectModels());

        [RelayCommand(CanExecute = nameof(CanImportModels))]
        private Task ImportDroppedModelsAsync(string[]? paths) =>
            ImportModelsAsync(paths?.Where(ModelImportService.HasSupportedExtension).ToArray() ?? []);

        private async Task ImportModelsAsync(string[] paths)
        {
            if (!CanImportModels() || paths.Length == 0) return;
            IsImportingModels = true;
            try
            {
                foreach (var path in paths)
                {
                    SceneNode? node;
                    try { node = await _modelImporter.LoadAsync(path); }
                    catch (Exception ex)
                    {
                        _dialogs.ShowMessage($"加载失败：{System.IO.Path.GetFileName(path)}\n{ex.Message}", "错误");
                        continue;
                    }
                    if (node == null) continue;

                    var baseName = System.IO.Path.GetFileNameWithoutExtension(path);
                    var name = baseName;
                    int suffix = 2;
                    while (PrepareWorkspace.OutlinerItems.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
                        name = $"{baseName} ({suffix++})";
                    node.Name = name;

                    var group = PrepareWorkspace.LoadedModel as SceneNodeGroupModel3D ?? new SceneNodeGroupModel3D();
                    group.AddNode(node);
                    PrepareWorkspace.OutlinerItems.Add(OutlinerNodeViewModel.BuildTree(node, name));
                    PrepareWorkspace.LoadedModel = group;
                    PrepareWorkspace.ArrangeImportedModel(node);
                }
            }
            finally { IsImportingModels = false; }
        }

        [RelayCommand]
        private void OpenPreferences() => _dialogs.OpenPreferences();

        [RelayCommand]
        private void OpenPrinters() => _dialogs.OpenPrinters();

        [RelayCommand]
        private void OpenAmlab_MonitorSettings()
        {
            var (saved, selectedModelId) = _dialogs.OpenAmlab_MonitorSettings();
            if (saved)
                _amlab_MonitorWorkspace.RequestModelCatalogRefresh(selectedModelId);
        }
        [RelayCommand] private void OpenExtensions() => _dialogs.OpenExtensions("amlab.agent");
        [RelayCommand] private void OpenExtensionManagement() => _dialogs.OpenExtensions("插件管理");

        [RelayCommand(CanExecute = nameof(IsPrepareWorkspaceActive))]
        private void Undo()
        {
            if (IsPrepareWorkspaceActive) PrepareWorkspace.History.Undo();
        }

        [RelayCommand(CanExecute = nameof(IsPrepareWorkspaceActive))]
        private void Redo()
        {
            if (IsPrepareWorkspaceActive) PrepareWorkspace.History.Redo();
        }
    }
}
