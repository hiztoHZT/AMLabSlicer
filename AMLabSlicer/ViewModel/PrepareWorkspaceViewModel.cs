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
        [ObservableProperty] private Geometry3D? _buildVolumeTopGeometry;
        [ObservableProperty] private Geometry3D? _buildVolumeSidesGeometry;
        [ObservableProperty] private Geometry3D? _originXAxisGeometry;
        [ObservableProperty] private Geometry3D? _originYAxisGeometry;
        [ObservableProperty] private Geometry3D? _originZAxisGeometry;
        public AMLabSlicer.Core.Printers.PrinterProfile? BuildVolumeProfile { get; private set; }

        private readonly SlicePresetStore _slicePresets = new();
        private string _parameterContext = "";

        private void CacheCurrentParameterValues()
        {
            if (string.IsNullOrEmpty(_activeParameterAlgorithmId))
                return;

            _parameterValuesByAlgorithm[_parameterContext] =
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
        private bool _parametersLoading;
        public AMLabSlicer.Core.Commands.CommandManager History { get; } = new();
        public PreferencesViewModel AppPrefs { get; }
        private readonly AMLabSlicer.Core.Printers.IPrinterProfileProvider? _printers;
        public string PrinterSummary
        {
            get
            {
                try { var p = _printers?.GetActiveSnapshot(); return p == null ? "设置打印机" : $"打印机：{p.Name}"; }
                catch { return "未选择有效打印机"; }
            }
        }
        [RelayCommand] private void OpenPrinterSettings() => _dialogs.OpenPrinters();

        public PrepareWorkspaceViewModel(IParameterStore parameterStore, PreferencesViewModel appPrefs,
            ISlicingService slicingService, ISliceRequestFactory sliceRequestFactory, IUserDialogService dialogs, PluginManager? plugins = null,
            AMLabSlicer.Core.Printers.IPrinterProfileProvider? printers = null)
        {
            InitializeSelection();
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
            _printers = printers;
            if (_printers != null) _printers.Changed += (_, _) =>
            {
                OnPropertyChanged(nameof(PrinterSummary));
                GeneratePlatformGrid();
                if (SelectedAlgorithm != null) _ = RebuildParametersForAlgorithmAsync(SelectedAlgorithm.AlgorithmId);
            };


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
            _parametersLoading = true;

            try
            {
                CacheCurrentParameterValues();
                var response = await _slicingService.GetParametersAsync(algorithm);
                if (_printers != null)
                {
                    var machineRows = response.Parameters.Where(p => AMLabSlicer.Core.Printers.PrinterCatalog.IsMachineParameter(p.Key)).ToArray();
                    foreach (var row in machineRows) response.Parameters.Remove(row);
                }
                if (requestVersion != _parameterRequestVersion || SelectedAlgorithm?.AlgorithmId != algorithm)
                    return;

                // 缓存旧参数用于继承
                var scope = (_printers?.GetActiveSnapshot().Id ?? "default") + "|" + algorithm;
                var group = _slicePresets.Group(scope);
                _loadingPresets = true;
                ParameterPresets.Clear();
                foreach (var preset in group.Presets) ParameterPresets.Add(preset);
                SelectedParameterPreset = ParameterPresets.Single(p => p.Id == group.SelectedId);
                _presetScope = scope;
                _loadingPresets = false;
                var context = scope + "#" + group.SelectedId;
                _parameterValuesByAlgorithm.TryGetValue(context, out var previousValues);
                var savedValues = _slicePresets.Read(context);

                _parameterStore.ClearAll();

                foreach (var pDef in response.Parameters)
                {
                    var initialValue = savedValues.TryGetValue(pDef.Key, out var saved) ? saved : pDef.DefaultValue;
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
                            param.Value = bool.TryParse(initialValue, out bool b) && b;
                        }
                        else if (param.ControlType == UIControlType.NumericBox || param.ControlType == UIControlType.Slider)
                        {
                            if (double.TryParse(initialValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) param.Value = d;
                        }
                        else
                        {
                            param.Value = initialValue;
                            if (param.ControlType == UIControlType.ComboBox)
                            {
                                param.Options = pDef.Options.ToList();
                            }
                        }
                    }

                    if (param.Key == "atom.fixed_pose_confirmed") param.Value = false;
                    param.PropertyChanged += (_, change) =>
                    {
                        if (change.PropertyName != nameof(SliceParameter.Value)) return;
                        try { _slicePresets.Save(context, param.Key, param.Value); }
                        catch (Exception ex) { _dialogs.ShowMessage("切片工艺保存失败：" + ex.Message); }
                    };
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
                _parameterContext = context;
            }
            catch (Exception ex)
            {
                if (requestVersion == _parameterRequestVersion)
                    _dialogs.ShowMessage("获取参数列表失败: " + ex.Message);
            }
            finally { if (requestVersion == _parameterRequestVersion) _parametersLoading = false; }
        }

        [RelayCommand]
        private void AutoArrange()
        {
            if (LoadedModel is not SceneNodeGroupModel3D model) return;
            var fixedNodes = model.GroupNode.Items.Where(node => OutlinerItems.Any(item => item.Node == node && item.DescendantsAndSelf().Any(member => member.IsEffectivelyLocked))).ToArray();
            try { History.ExecuteCommand(SceneArrangement.CreateCommand(model.GroupNode.Items.Except(fixedNodes), _printers?.GetActiveSnapshot(), fixedNodes)); }
            catch (InvalidOperationException ex) { _dialogs.ShowMessage(ex.Message, "自动摆放"); }
        }

        public void ArrangeImportedModel(SceneNode node)
        {
            var existing = (LoadedModel as SceneNodeGroupModel3D)?.GroupNode.Items.Where(item => item != node).ToArray() ?? [];
            try { SceneArrangement.CreateCommand([node], _printers?.GetActiveSnapshot(), existing).Execute(); }
            catch (InvalidOperationException ex)
            {
                _dialogs.ShowMessage($"模型已导入，但未能自动排列：{ex.Message}\n已有模型位置未改变，请手动调整新模型。", "导入模型");
            }
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
                    AllOutlinerItems.Where(item => !item.IsGroup && item.Children.Count == 0)
                        .Select(item => new SceneObject(item.Name, item.Node!)));

                if (req.Objects.Count == 0)
                {
                    _dialogs.ShowMessage("请先导入可切片的模型。");
                    return;
                }

                var result = await _dialogs.SliceWithProgressAsync(_slicingService, req, SelectedAlgorithm.DisplayName);
                if (result == null) return;
                var artifact = result.Artifacts.FirstOrDefault(a => a.Kind == "gcode")
                    ?? result.Artifacts.FirstOrDefault(a => a.Kind == "toolpath.xyzab.json")
                    ?? result.Artifacts.FirstOrDefault(a => a.Kind == "toolpath.ply");
                if (artifact == null || artifact.Data.Length == 0)
                {
                    _dialogs.ShowMessage("切片成功，但后端没有返回可保存的路径文件。", "结果缺失");
                    return;
                }
                _dialogs.ShowSliceCompletion(artifact, req.Printer?.Name ?? "未选择打印机", result.Message);
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
            var topBuilder = new LineBuilder();
            var sidesBuilder = new LineBuilder();
            BuildVolumeProfile = null;

            // 设定平台尺寸 225
            double width = 225, depth = 225, height = 225;
            bool elliptic = false;
            bool centered = false;
            try
            {
                if (_printers?.GetActiveSnapshot() is { } printer)
                {
                    width = printer.Number("machine_width"); depth = printer.Number("machine_depth");
                    BuildVolumeProfile = printer;
                    height = printer.Number("machine_height");
                    elliptic = printer.Machine.GetValueOrDefault("machine_shape") == "elliptic";
                    centered = printer.Machine.GetValueOrDefault("machine_center_is_zero") == "true";
                }
            }
            catch (InvalidOperationException) { }
            float originX = centered ? -(float)width / 2 : 0, originY = centered ? -(float)depth / 2 : 0;
            // Bound viewport line count independently of the machine dimensions.
            var spacing = Math.Max(1, (int)Math.Ceiling(Math.Max(width, depth) / 1000));



            var cx = originX + width / 2;
            var cy = originY + depth / 2;
            for (int x = 0; x <= width; x += spacing)
            {
                double half = elliptic ? depth / 2 * Math.Sqrt(Math.Max(0, 1 - Math.Pow((x - width / 2) / (width / 2), 2))) : depth / 2;
                (x % 10 == 0 ? majorBuilder : minorBuilder).AddLine(new Vector3(x + originX, (float)(cy - half), 0), new Vector3(x + originX, (float)(cy + half), 0));
            }
            for (int y = 0; y <= depth; y += spacing)
            {
                double half = elliptic ? width / 2 * Math.Sqrt(Math.Max(0, 1 - Math.Pow((y - depth / 2) / (depth / 2), 2))) : width / 2;
                (y % 10 == 0 ? majorBuilder : minorBuilder).AddLine(new Vector3((float)(cx - half), y + originY, 0), new Vector3((float)(cx + half), y + originY, 0));
            }
            var outline = new List<Vector3>();
            if (elliptic)
            {
                for (int i = 0; i < 128; i++)
                {
                    double angle = i * Math.PI * 2 / 128;
                    outline.Add(new Vector3((float)(cx + width / 2 * Math.Cos(angle)), (float)(cy + depth / 2 * Math.Sin(angle)), 0));
                }
            }
            else
                outline.AddRange(new[] { new Vector3(originX, originY, 0), new Vector3(originX + (float)width, originY, 0), new Vector3(originX + (float)width, originY + (float)depth, 0), new Vector3(originX, originY + (float)depth, 0) });
            var up = new Vector3(0, 0, (float)height);
            for (int i = 0; i < outline.Count; i++)
            {
                var a = outline[i]; var b = outline[(i + 1) % outline.Count];
                majorBuilder.AddLine(a, b);
                topBuilder.AddLine(a + up, b + up);
                if (!elliptic || i % 32 == 0) sidesBuilder.AddLine(a, a + up);
            }

            // 将打包好的线框数据转换为渲染引擎认识的 Geometry3D，并绑定给前台
            var axisLength = (float)Math.Min(30, Math.Min(width, Math.Min(depth, height)) * 0.12);
            static Geometry3D BuildAxis(Vector3 endpoint)
            {
                var builder = new LineBuilder();
                builder.AddLine(Vector3.Zero, endpoint);
                return builder.ToLineGeometry3D();
            }

            OriginXAxisGeometry = BuildAxis(Vector3.UnitX * axisLength);
            OriginYAxisGeometry = BuildAxis(Vector3.UnitY * axisLength);
            OriginZAxisGeometry = BuildAxis(Vector3.UnitZ * axisLength);
            MajorGridGeometry = majorBuilder.ToLineGeometry3D();
            MinorGridGeometry = minorBuilder.ToLineGeometry3D();
            BuildVolumeTopGeometry = topBuilder.ToLineGeometry3D();
            BuildVolumeSidesGeometry = sidesBuilder.ToLineGeometry3D();
            OnPropertyChanged(nameof(BuildVolumeProfile));
        }
    }
}
