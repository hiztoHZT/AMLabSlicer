using AMLabSlicer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using System.Numerics;
using System.Collections.ObjectModel;
using System.Globalization;
using AMLabSlicer.Core.Parameters;
using AMLabSlicer.Grpc;
using System.Threading.Tasks;
using System.Linq;
using System;
using System.IO;
using HelixToolkit.SharpDX.Model.Scene;

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

        private void CacheCurrentParameterValues()
        {
            if (string.IsNullOrEmpty(_activeParameterAlgorithmId))
                return;

            _parameterValuesByAlgorithm[_activeParameterAlgorithmId] =
                _parameterStore.GetAllParameters().ToDictionary(p => p.Key, p => p.Value);
        }

        private static string GroupStateKey(string algorithmId, string category, string subcategory)
            => $"{algorithmId}|{category}|{subcategory}";

        private void CacheSubcategoryExpandedStates()
        {
            foreach (var category in ParameterCategories)
            {
                foreach (var subcategory in category.Subcategories)
                {
                    _subcategoryExpandedStates[GroupStateKey(_activeParameterAlgorithmId, category.Name, subcategory.Name)] = subcategory.IsExpanded;
                }
            }
        }

        private void RebuildParameterGroups(IEnumerable<SliceParameter> orderedParameters, string algorithmId)
        {
            CacheSubcategoryExpandedStates();

            var selectedCategoryName = SelectedParameterCategory?.Name;
            ParameterCategories.Clear();

            foreach (var parameter in orderedParameters)
            {
                var categoryName = string.IsNullOrWhiteSpace(parameter.Category) ? "未分类" : parameter.Category;
                var subcategoryName = string.IsNullOrWhiteSpace(parameter.Subcategory) ? "常规" : parameter.Subcategory;
                var category = ParameterCategories.FirstOrDefault(group => group.Name == categoryName);

                if (category == null)
                {
                    category = new ParameterCategoryGroup(categoryName);
                    ParameterCategories.Add(category);
                }

                var expandedKey = GroupStateKey(algorithmId, categoryName, subcategoryName);
                var isExpanded = !_subcategoryExpandedStates.TryGetValue(expandedKey, out var savedExpanded) || savedExpanded;
                category.GetOrAddSubcategory(subcategoryName, isExpanded).Parameters.Add(parameter);
            }

            SelectedParameterCategory =
                ParameterCategories.FirstOrDefault(group => group.Name == selectedCategoryName)
                ?? ParameterCategories.FirstOrDefault();
        }

        [RelayCommand]
        private void SelectParameterCategory(ParameterCategoryGroup? category)
        {
            if (category != null)
                SelectedParameterCategory = category;
        }

        // 左侧面板开关状态
        [ObservableProperty]
        private bool _isParameterPanelOpen = true;

        [RelayCommand]
        private void TogglePanel() => IsParameterPanelOpen = !IsParameterPanelOpen;

        [ObservableProperty] private bool _isAgentPanelOpen;
        [RelayCommand] private void ToggleAgentPanel() => IsAgentPanelOpen = !IsAgentPanelOpen;
        public ObservableCollection<WorkspacePanelEntry> ExtensionPanels { get; }
        [ObservableProperty] private WorkspacePanelEntry? _selectedExtensionPanel;
        [RelayCommand] private void SelectExtensionPanel(WorkspacePanelEntry entry)
        {
            if (!ExtensionPanels.Contains(entry)) return;
            foreach (var panel in ExtensionPanels) panel.IsSelected = ReferenceEquals(panel, entry);
            SelectedExtensionPanel = entry; IsAgentPanelOpen = true;
        }
        [RelayCommand] private void OpenExtensionManagement() => _dialogs.OpenExtensions("插件管理");

        // 面向大纲视图的模型节点树
        public ObservableCollection<OutlinerNodeViewModel> OutlinerItems { get; } = new ObservableCollection<OutlinerNodeViewModel>();

        // 可选切片算法集合
        public ObservableCollection<AlgorithmInfo> SlicingAlgorithms { get; } = new ObservableCollection<AlgorithmInfo>();

        private AlgorithmInfo? _selectedAlgorithm;
        public AlgorithmInfo? SelectedAlgorithm
        {
            get => _selectedAlgorithm;
            set
            {
                if (SetProperty(ref _selectedAlgorithm, value))
                {
                    if (!string.IsNullOrEmpty(value?.AlgorithmId))
                    {
                        _ = RebuildParametersForAlgorithmAsync(value.AlgorithmId);
                    }
                }
            }
        }

        // 暴露给 UI 绑定的参数集合
        public ObservableCollection<SliceParameter> Parameters { get; } = new ObservableCollection<SliceParameter>();

        public ObservableCollection<ParameterCategoryGroup> ParameterCategories { get; } = new ObservableCollection<ParameterCategoryGroup>();

        [ObservableProperty]
        private ParameterCategoryGroup? _selectedParameterCategory;

        partial void OnSelectedParameterCategoryChanged(ParameterCategoryGroup? oldValue, ParameterCategoryGroup? newValue)
        {
            if (oldValue != null)
                oldValue.IsSelected = false;

            if (newValue != null)
                newValue.IsSelected = true;
        }

        private readonly IParameterStore _parameterStore;
        private readonly Dictionary<string, Dictionary<string, object?>> _parameterValuesByAlgorithm = new();
        private readonly Dictionary<string, bool> _subcategoryExpandedStates = new();
        private string _activeParameterAlgorithmId = "";
        
        private readonly ISlicingService _slicingService;
        private readonly ISliceRequestFactory _sliceRequestFactory;
        private readonly IUserDialogService _dialogs;
        private int _parameterRequestVersion;
        public AMLabSlicer.Core.Commands.CommandManager History { get; } = new();
        public PreferencesViewModel AppPrefs { get; }

        public PrepareWorkspaceViewModel(IParameterStore parameterStore, PreferencesViewModel appPrefs,
            ISlicingService slicingService, ISliceRequestFactory sliceRequestFactory, IUserDialogService dialogs, PluginManager? plugins = null)
        {
            _parameterStore = parameterStore;
            AppPrefs = appPrefs;
            ExtensionPanels = plugins?.Panels ?? new();
            SelectedExtensionPanel = ExtensionPanels.FirstOrDefault();
            if (SelectedExtensionPanel is not null) SelectedExtensionPanel.IsSelected = true;
            ExtensionPanels.CollectionChanged += (_, _) =>
            {
                if (SelectedExtensionPanel is null || !ExtensionPanels.Contains(SelectedExtensionPanel))
                {
                    SelectedExtensionPanel = ExtensionPanels.FirstOrDefault();
                    if (SelectedExtensionPanel is not null) SelectedExtensionPanel.IsSelected = true;
                }
            };
            IsAgentPanelOpen = appPrefs.OpenDeveloperPanelOnStartup;
            _slicingService = slicingService;
            _sliceRequestFactory = sliceRequestFactory;
            _dialogs = dialogs;


            // 在工作区初始化时，立刻生成切片平台网格
            GeneratePlatformGrid();
        }

        [RelayCommand]
        private async Task InitializeAsync()
        {
            try
            {
                var response = await _slicingService.GetAlgorithmsAsync();
                SlicingAlgorithms.Clear();
                foreach (var alg in response.Algorithms)
                {
                    SlicingAlgorithms.Add(alg);
                }

                if (SlicingAlgorithms.Count > 0)
                {
                    SelectedAlgorithm = SlicingAlgorithms[0];
                }
            }
            catch (Exception ex)
            {
                _dialogs.ShowMessage("无法连接到后端引擎 (amlabslicer.engine)。\n请确保后端服务已在 http://localhost:50051 运行。\n" + ex.Message, "连接失败");
            }
        }

        private async Task RebuildParametersForAlgorithmAsync(string algorithm)
        {
            var requestVersion = ++_parameterRequestVersion;

            try
            {
                CacheCurrentParameterValues();
                var response = await _slicingService.GetParametersAsync(algorithm);
                if (requestVersion != _parameterRequestVersion || SelectedAlgorithm?.AlgorithmId != algorithm)
                    return;

                // 缓存旧参数用于继承
                _parameterValuesByAlgorithm.TryGetValue(algorithm, out var previousValues);

                _parameterStore.ClearAll();

                foreach (var pDef in response.Parameters)
                {
                    var param = new SliceParameter
                    {
                        Key = pDef.Key,
                        DisplayName = pDef.DisplayName,
                        Category = pDef.Category,
                        Subcategory = pDef.Subcategory,
                        Order = pDef.Order,
                        ControlType = (UIControlType)pDef.ControlType,
                        Unit = pDef.Unit,
                        Description = pDef.Description,
                        MinValue = pDef.MinValue,
                        MaxValue = pDef.MaxValue,
                        Step = pDef.Step,
                        IsAdvanced = pDef.IsAdvanced,
                        VisibleIf = pDef.VisibleIf,
                        EnabledIf = pDef.EnabledIf
                    };

                    if (param.ControlType == UIControlType.ComboBox)
                    {
                        param.Options = pDef.Options.ToList();
                    }

                    if (previousValues != null && previousValues.TryGetValue(pDef.Key, out var oldVal))
                    {
                        param.Value = oldVal;
                    }
                    else
                    {
                        if (param.ControlType == UIControlType.CheckBox)
                        {
                            param.Value = bool.TryParse(pDef.DefaultValue, out bool b) && b;
                        }
                        else if (param.ControlType == UIControlType.NumericBox || param.ControlType == UIControlType.Slider)
                        {
                            if (double.TryParse(pDef.DefaultValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) param.Value = d;
                        }
                        else
                        {
                            param.Value = pDef.DefaultValue;
                            if (param.ControlType == UIControlType.ComboBox)
                            {
                                param.Options = pDef.Options.ToList();
                            }
                        }
                    }

                    _parameterStore.RegisterParameter(param);
                }

                var orderedParameters = response.Parameters
                    .Select(pDef => _parameterStore.GetParameterRaw(pDef.Key))
                    .Where(p => p != null)
                    .Cast<SliceParameter>()
                    .ToList();

                Parameters.Clear();
                foreach (var p in orderedParameters)
                {
                    Parameters.Add(p);
                }

                RebuildParameterGroups(orderedParameters, algorithm);

                _activeParameterAlgorithmId = algorithm;
            }
            catch (Exception ex)
            {
                if (requestVersion == _parameterRequestVersion)
                    _dialogs.ShowMessage("获取参数列表失败: " + ex.Message);
            }
        }

        [RelayCommand]
        private void AutoArrange()
        {
            if (LoadedModel is not SceneNodeGroupModel3D model) return;
            try { History.ExecuteCommand(SceneArrangement.CreateCommand(model.GroupNode.Items)); }
            catch (InvalidOperationException ex) { _dialogs.ShowMessage(ex.Message, "自动摆放"); }
        }

        [RelayCommand]
        private async Task StartSlicingAsync()
        {
            if (SelectedAlgorithm == null || SelectedAlgorithm.AlgorithmId != _activeParameterAlgorithmId)
            {
                _dialogs.ShowMessage("请等待所选算法参数加载完成后再切片。");
                return;
            }

            try
            {
                var req = _sliceRequestFactory.Create(SelectedAlgorithm.AlgorithmId,
                    _parameterStore.GetAllParameters(),
                    OutlinerItems.Where(item => item.Node != null)
                        .Select(item => new SceneObject(item.Name, item.Node!)));

                if (req.Objects.Count == 0)
                {
                    _dialogs.ShowMessage("请先导入可切片的模型。");
                    return;
                }

                await foreach (var response in _slicingService.SliceAsync(req))
                {
                    if (response.MsgCase != SliceServerMessage.MsgOneofCase.Result) continue;
                    if (!response.Result.Success)
                    {
                        _dialogs.ShowMessage($"切片失败: {response.Result.Message}", "错误");
                        continue;
                    }
                    var artifact = response.Result.Artifacts.FirstOrDefault(a => a.Kind == "gcode");
                    if (artifact == null || artifact.Data.Length == 0)
                    {
                        _dialogs.ShowMessage("切片成功，但后端没有返回 G-code artifact。", "结果缺失");
                        continue;
                    }
                    var path = _dialogs.SelectGCodeDestination();
                    if (path == null) continue;
                    await File.WriteAllBytesAsync(path, artifact.Data.ToByteArray());
                    _dialogs.ShowMessage($"文件已保存至：\n{path}", "切片完成");
                }
            }
            catch (Exception ex)
            {
                _dialogs.ShowMessage("发送切片请求失败: " + ex.Message);
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
