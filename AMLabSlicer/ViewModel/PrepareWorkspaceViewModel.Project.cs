using AMLabSlicer.Services;
using AMLabSlicer.Grpc;
using HelixToolkit.Wpf.SharpDX;

namespace AMLabSlicer.ViewModel;

public partial class PrepareWorkspaceViewModel
{
    public ProjectDocument CaptureProject()
    {
        if (_parametersLoading || SelectedAlgorithm == null || SelectedAlgorithm.AlgorithmId != _activeParameterAlgorithmId)
            throw new InvalidOperationException("请等待切片算法参数加载完成后再保存。");
        var outline = AllOutlinerItems.ToDictionary(item => item.Node);
        var document = ProjectFile.Clone(new ProjectDocument
        {
            Printer = _printers?.GetActiveSnapshot() ?? throw new InvalidOperationException("请先配置打印机。"),
            AlgorithmId = SelectedAlgorithm.AlgorithmId,
            AlgorithmName = SelectedAlgorithm.DisplayName,
            Parameters = _parameterStore.GetAllParameters().ToList(),
            Models = new()
        });
        document.Models = OutlinerItems.Select(item => ProjectFile.Capture(item.Node, outline)).ToList();
        return document;
    }

    public void RestoreProject(ProjectDocument document)
    {
        // Prepare geometry before changing the current workspace or printer library.
        var restored = document.Models.Select(ProjectFile.Restore).ToArray();
        var group = new SceneNodeGroupModel3D();
        foreach (var item in restored) group.AddNode(item.Node);
        if (_printers is not PrinterProfileStore printers)
            throw new InvalidOperationException("当前打印机存储不支持打开工程。");
        var library = printers.Snapshot();
        var printer = document.Printer.Clone();
        // Preserve existing user profiles; reuse an identical snapshot when possible.
        var existing = library.Printers.FirstOrDefault(p => System.Text.Json.JsonSerializer.Serialize(p) == System.Text.Json.JsonSerializer.Serialize(printer));
        if (existing == null)
        {
            if (library.Printers.Any(p => p.Id == printer.Id)) printer.Id = Guid.NewGuid().ToString("N");
            library.Printers.Add(printer);
        }
        library.ActivePrinterId = existing?.Id ?? printer.Id;
        printers.Save(library);
        ++_parameterRequestVersion; // Discard parameter reload triggered by printer change.
        CacheCurrentParameterValues();
        var algorithm = SlicingAlgorithms.FirstOrDefault(a => a.AlgorithmId == document.AlgorithmId);
        if (algorithm == null)
        {
            algorithm = new AlgorithmInfo { AlgorithmId = document.AlgorithmId, DisplayName = document.AlgorithmName };
            SlicingAlgorithms.Add(algorithm);
        }
        _selectedAlgorithm = algorithm;
        OnPropertyChanged(nameof(SelectedAlgorithm));
        _activeParameterAlgorithmId = document.AlgorithmId;
        _presetScope = library.ActivePrinterId + "|" + document.AlgorithmId;
        var presetName = "工程 " + DateTime.Now.ToString("yyyyMMdd HHmmss") + " " + Guid.NewGuid().ToString("N")[..6];
        _slicePresets.Create(_presetScope, presetName, document.Parameters.ToDictionary(p => p.Key,
            p => Convert.ToString(p.Value, System.Globalization.CultureInfo.InvariantCulture) ?? ""));
        var presets = _slicePresets.Group(_presetScope);
        _parameterContext = _presetScope + "#" + presets.SelectedId;
        _parametersLoading = false;
        _loadingPresets = true;
        ParameterPresets.Clear();
        foreach (var preset in presets.Presets) ParameterPresets.Add(preset);
        SelectedParameterPreset = ParameterPresets.Single(p => p.Id == presets.SelectedId);
        _loadingPresets = false;
        _presetScope = library.ActivePrinterId + "|" + document.AlgorithmId;
        _parameterStore.ClearAll();
        Parameters.Clear();
        foreach (var parameter in document.Parameters)
        {
            // A physical pose confirmation is session-specific, never trust it from a file.
            if (parameter.Key == "atom.fixed_pose_confirmed") parameter.Value = false;
            var context = _parameterContext;
            parameter.PropertyChanged += (_, change) =>
            {
                if (change.PropertyName != nameof(parameter.Value)) return;
                try { _slicePresets.Save(context, parameter.Key, parameter.Value); }
                catch (Exception ex) { _dialogs.ShowMessage("切片工艺保存失败：" + ex.Message); }
            };
            _parameterStore.RegisterParameter(parameter);
            Parameters.Add(parameter);
        }
        RebuildParameterGroups(Parameters, document.AlgorithmId);
        SelectObjects(Array.Empty<OutlinerNodeViewModel>());
        History.Clear();
        OutlinerItems.Clear();
        LoadedModel = group;
        foreach (var item in restored)
            OutlinerItems.Add(item.Outline ?? OutlinerNodeViewModel.BuildTree(item.Node));
    }
}
