using System.Text.Json;
using System.Collections.ObjectModel;
using AMLabSlicer.Core.Printers;
using AMLabSlicer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AMLabSlicer.ViewModel;

public sealed class PrinterSettingRow : ObservableObject
{
    private readonly Dictionary<string, string> values;
    public PrinterSettingDefinition Definition { get; }
    public string Label => Definition.Label;
    public string Unit => Definition.Unit;
    public string Description => Definition.Description;
    public bool IsEnabled => !Definition.ReadOnly;
    public bool IsBoolean => Definition.Type == "bool";
    public bool IsChoice => Definition.Options.Length > 0;
    public bool IsText => !IsBoolean && !IsChoice;
    public bool IsMultiline => Definition.Multiline;
    public string[] Options => Definition.Options;
    public string Value
    {
        get => values.GetValueOrDefault(Definition.Key, Definition.Default);
        set { values[Definition.Key] = value; OnPropertyChanged(); OnPropertyChanged(nameof(BooleanValue)); OnPropertyChanged(nameof(Error)); }
    }
    public bool BooleanValue { get => bool.TryParse(Value, out var flag) && flag; set => Value = value ? "true" : "false"; }
    public string Error => PrinterCatalog.ValidateValue(Definition, Value) ?? "";
    public PrinterSettingRow(PrinterSettingDefinition definition, Dictionary<string, string> values) { Definition = definition; this.values = values; }
}

public partial class PrinterSettingsViewModel : ObservableObject
{
    private readonly PrinterProfileStore store;
    private readonly PrinterLibrary draft;
    private string savedState = "";
    private string CaptureState() { foreach (var printer in Printers) printer.SynchronizeDerivedValues(); return JsonSerializer.Serialize(new PrinterLibrary { Printers = Printers.ToList(), ActivePrinterId = SelectedPrinter?.Id ?? "" }); }
    public bool HasUnsavedChanges => CaptureState() != savedState;
    public bool TrySave() { Save(); return !HasUnsavedChanges; }
    public ObservableCollection<PrinterProfile> Printers { get; }
    public ObservableCollection<PrinterSettingRow> Rows { get; } = new();
    public ObservableCollection<PrinterExtruder> Extruders { get; } = new();
    public ObservableCollection<PrinterAxis> Axes { get; } = new();
    public string[] Sections { get; } = ["机器参数", "挤出机", "多轴与运动学", "运动轴"];
    [ObservableProperty] private PrinterProfile? selectedPrinter;
    [ObservableProperty] private PrinterExtruder? selectedExtruder;
    [ObservableProperty] private PrinterAxis? selectedAxis;
    [ObservableProperty] private string selectedSection = "机器参数";
    [ObservableProperty] private string search = "";
    [ObservableProperty] private bool onlyApplicableParameters = true;
    private string currentAlgorithmId = "";
    public bool CanFilterParameters => PrinterParameterUsage.IsKnown(currentAlgorithmId);
    public bool HasNoVisibleRows => Rows.Count == 0;
    public void SetCurrentMethod(string algorithmId)
    {
        currentAlgorithmId = algorithmId;
        OnPropertyChanged(nameof(CanFilterParameters));
        RefreshRows();
    }
    [ObservableProperty] private string status = "修改暂存在此窗口；点击“保存并使用”后生效，关闭则放弃未保存修改。";
    public bool HasPrinter => SelectedPrinter != null;
    public bool IsExtruderSection => SelectedSection == "挤出机" && HasPrinter;
    public bool IsAxisSection => SelectedSection == "运动轴" && HasPrinter;
    public string Location => store.Location;
    public string PrinterName
    {
        get => SelectedPrinter?.Name ?? "";
        set { if (SelectedPrinter == null) return; SelectedPrinter.Name = value; OnPropertyChanged(); }
    }
    public string ActiveExtruderLabel => SelectedPrinter == null ? "" : $"切片使用挤出机 {SelectedPrinter.ActiveExtruder + 1} 的喷嘴及耗材直径";
    public PrinterSettingsViewModel(PrinterProfileStore store)
    {
        this.store = store; draft = store.Snapshot(); Printers = new(draft.Printers);
        if (store.LoadWarning.Length > 0) Status = store.LoadWarning;
        SelectedPrinter = Printers.FirstOrDefault(p => p.Id == draft.ActivePrinterId) ?? Printers.FirstOrDefault();
        savedState = CaptureState();
    }
    partial void OnSelectedPrinterChanged(PrinterProfile? value)
    {
        Extruders.Clear(); Axes.Clear();
        if (value != null)
        {
            foreach (var item in value.Extruders) Extruders.Add(item);
            foreach (var item in value.Axes) Axes.Add(item);
        }
        SelectedExtruder = Extruders.ElementAtOrDefault(value?.ActiveExtruder ?? 0); SelectedAxis = Axes.FirstOrDefault();
        OnPropertyChanged(nameof(PrinterName)); OnPropertyChanged(nameof(HasPrinter)); OnPropertyChanged(nameof(ActiveExtruderLabel));
        RefreshRows();
    }
    partial void OnSelectedExtruderChanged(PrinterExtruder? value) => RefreshRows();
    partial void OnSelectedAxisChanged(PrinterAxis? value) => RefreshRows();
    partial void OnSelectedSectionChanged(string value) { Search = ""; RefreshRows(); }
    partial void OnSearchChanged(string value) => RefreshRows();
    partial void OnOnlyApplicableParametersChanged(bool value) => RefreshRows();
    private void RefreshRows()
    {
        OnPropertyChanged(nameof(IsExtruderSection)); OnPropertyChanged(nameof(IsAxisSection));
        Rows.Clear(); if (SelectedPrinter == null) return;
        SelectedPrinter.SynchronizeDerivedValues();
        var scope = SelectedSection switch { "挤出机" => "extruder", "多轴与运动学" => "kinematics", "运动轴" => "axis", _ => "machine" };
        var values = scope switch { "extruder" => SelectedExtruder?.Settings, "kinematics" => SelectedPrinter.Kinematics, "axis" => SelectedAxis?.Settings, _ => SelectedPrinter.Machine };
        if (values == null) return;
        var groups = new[] { "基础与成型空间", "喷头与热管理", "挤出机与换头", "运动与固件参数", "指令与启停", "材料兼容默认值" };
        foreach (var definition in PrinterCatalog.Definitions.Where(d => d.Scope == scope &&
            (!OnlyApplicableParameters || !CanFilterParameters || PrinterParameterUsage.Applies(currentAlgorithmId, scope, d.Key, SelectedAxis?.Name)) &&
            (string.IsNullOrWhiteSpace(Search) || (d.Key + d.Label + d.Group + d.Description).Contains(Search, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(d => Array.IndexOf(groups, d.Group)))
            Rows.Add(new PrinterSettingRow(definition, values));
        OnPropertyChanged(nameof(HasNoVisibleRows));
    }
    [RelayCommand] private void NewPrinter()
    {
        var printer = PrinterProfile.Create("新打印机 " + (Printers.Count + 1));
        Printers.Add(printer); SelectedPrinter = printer; Status = "已新建，填写参数后点击“保存并使用”。";
    }
    [RelayCommand] private void CopyPrinter()
    {
        if (SelectedPrinter == null) return;
        var copy = SelectedPrinter.Clone(); copy.Id = Guid.NewGuid().ToString("N"); copy.Name += " 副本"; copy.Revision = 1;
        foreach (var extruder in copy.Extruders) extruder.Id = Guid.NewGuid().ToString("N");
        foreach (var axis in copy.Axes) axis.Id = Guid.NewGuid().ToString("N");
        Printers.Add(copy); SelectedPrinter = copy; Status = "已复制为独立配置，保存后生效。";
    }
    [RelayCommand] private void DeletePrinter()
    {
        if (SelectedPrinter == null) return;
        var name = SelectedPrinter.Name; Printers.Remove(SelectedPrinter); SelectedPrinter = Printers.FirstOrDefault();
        Status = $"已从草稿移除“{name}”；保存后删除，关闭窗口可放弃本次删除。";
    }
    [RelayCommand] private void AddExtruder()
    {
        if (SelectedPrinter == null) return;
        var extruder = PrinterExtruder.Create(Extruders.Count); SelectedPrinter.Extruders.Add(extruder); Extruders.Add(extruder); SelectedExtruder = extruder;
    }
    [RelayCommand] private void DeleteExtruder()
    {
        if (SelectedPrinter == null || SelectedExtruder == null) return;
        if (Extruders.Count <= 1) { Status = "至少保留一台挤出机。"; return; }
        SelectedPrinter.Extruders.Remove(SelectedExtruder); Extruders.Remove(SelectedExtruder);
        SelectedPrinter.ActiveExtruder = 0; SelectedExtruder = Extruders.First(); OnPropertyChanged(nameof(ActiveExtruderLabel));
    }
    [RelayCommand] private void UseExtruder()
    {
        if (SelectedPrinter == null || SelectedExtruder == null) return;
        SelectedPrinter.ActiveExtruder = Extruders.IndexOf(SelectedExtruder); SelectedPrinter.SynchronizeDerivedValues(); OnPropertyChanged(nameof(ActiveExtruderLabel));
    }
    [RelayCommand] private void AddAxis()
    {
        if (SelectedPrinter == null) return;
        var name = new[] { "A", "B", "C", "U", "V", "W" }.FirstOrDefault(n => !Axes.Any(a => a.Name == n)) ?? "J" + (Axes.Count + 1);
        var axis = PrinterAxis.Create(name); SelectedPrinter.Axes.Add(axis); Axes.Add(axis);
        SelectedPrinter.Kinematics["is_multi_axis"] = "true"; SelectedAxis = axis;
        Status = "已添加旋转轴并启用多轴；请填写真实运动链、轴方向、旋转中心及限位。";
    }
    [RelayCommand] private void DeleteAxis()
    {
        if (SelectedPrinter == null || SelectedAxis == null) return;
        if (Axes.Count <= 3) { Status = "至少保留三根运动轴。"; return; }
        SelectedPrinter.Axes.Remove(SelectedAxis); Axes.Remove(SelectedAxis); SelectedAxis = Axes.First();
        if (Axes.Count == 3) SelectedPrinter.Kinematics["is_multi_axis"] = "false";
    }
    [RelayCommand] private void Save()
    {
        try
        {
            foreach (var printer in Printers) { printer.SynchronizeDerivedValues(); PrinterValidation.ThrowIfInvalid(printer); }
            draft.Printers = Printers.ToList(); draft.ActivePrinterId = SelectedPrinter?.Id ?? "";
            foreach (var printer in draft.Printers) printer.Revision++;
            store.Save(draft); Status = SelectedPrinter == null ? "已保存；当前没有打印机，切片已禁用。" : $"已保存，当前打印机：{SelectedPrinter.Name}。后续切片使用此配置。";
            // Refresh display names after a rename, without losing the edited profile objects.
            var selected = SelectedPrinter; var saved = Printers.ToArray(); Printers.Clear();
            foreach (var item in saved) Printers.Add(item); SelectedPrinter = selected;
            savedState = CaptureState();
        }
        catch (Exception ex) { Status = "未保存：" + ex.Message; }
    }
}
