using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using AMLabSlicer.Plugin.Wpf.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace AMLabSlicer.Amlab_Monitor;

internal sealed partial class Amlab_MonitorSettingsViewModel : ObservableObject
{
    private readonly Func<string?> _pickWeight;
    private readonly string? _snapshot;
    private readonly bool _loaded;

    internal Amlab_MonitorSettingsViewModel(Func<string?> pickWeight)
    {
        _pickWeight = pickWeight;
        try
        {
            var config = Amlab_MonitorModelStore.Load();
            _snapshot = config.Snapshot;
            Amlab_MonitorEntries = new(config.Entries);
            Amlab_MonitorSelectedEntry = Amlab_MonitorEntries.FirstOrDefault();
            _loaded = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            Amlab_MonitorStatus = string.Format(Text("无法读取视觉模型配置：{0}"), error.Message);
        }
    }

    public ObservableCollection<Amlab_MonitorModelEntry> Amlab_MonitorEntries { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Amlab_MonitorHasSelection))]
    [NotifyCanExecuteChangedFor(nameof(Amlab_MonitorChangeWeightCommand), nameof(Amlab_MonitorRemoveModelCommand))]
    private Amlab_MonitorModelEntry? _amlab_MonitorSelectedEntry;

    [ObservableProperty] private string _amlab_MonitorStatus = "";
    public bool Amlab_MonitorHasSelection => Amlab_MonitorSelectedEntry is not null;

    internal event Action? Saved;

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Amlab_MonitorAddModel() => TryAddModel();

    internal string? AddAndSave() => TryAddModel() is { } entry && TrySave() ? entry.Id : null;

    private Amlab_MonitorModelEntry? TryAddModel()
    {
        if (!_loaded || PickWeight() is not { } path)
            return null;
        var entry = new Amlab_MonitorModelEntry(Guid.NewGuid().ToString("N"), Path.GetFileNameWithoutExtension(path), path);
        Amlab_MonitorEntries.Add(entry);
        Amlab_MonitorSelectedEntry = entry;
        return entry;
    }

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void Amlab_MonitorChangeWeight()
    {
        if (Amlab_MonitorSelectedEntry is { } entry && PickWeight(entry) is { } path)
            entry.WeightPath = path;
    }

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void Amlab_MonitorRemoveModel()
    {
        if (Amlab_MonitorSelectedEntry is not { } entry)
            return;
        var index = Amlab_MonitorEntries.IndexOf(entry);
        Amlab_MonitorEntries.RemoveAt(index);
        Amlab_MonitorSelectedEntry = Amlab_MonitorEntries.Count == 0 ? null : Amlab_MonitorEntries[Math.Min(index, Amlab_MonitorEntries.Count - 1)];
        Amlab_MonitorStatus = "";
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Amlab_MonitorSave()
    {
        if (TrySave())
            Saved?.Invoke();
    }

    private bool TrySave()
    {
        if (Amlab_MonitorEntries.Any(entry => string.IsNullOrWhiteSpace(entry.Name)))
        {
            Amlab_MonitorStatus = Text("请填写每个视觉模型的名称");
            return false;
        }

        var entries = Amlab_MonitorEntries.Select(entry =>
            new Amlab_MonitorModelEntry(entry.Id, entry.Name.Trim(), entry.WeightPath)).ToArray();
        try
        {
            if (!Amlab_MonitorModelStore.TrySave(_snapshot, entries))
            {
                Amlab_MonitorStatus = Text("视觉模型配置已在其他窗口修改，请重新打开");
                return false;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Amlab_MonitorStatus = string.Format(Text("无法保存视觉模型配置：{0}"), error.Message);
            return false;
        }
        return true;
    }

    private string? PickWeight(Amlab_MonitorModelEntry? current = null)
    {
        var path = _pickWeight();
        if (path is null)
            return null;
        if (!Amlab_MonitorModelStore.IsWeightPath(path) || !File.Exists(path))
        {
            Amlab_MonitorStatus = Text("请选择存在的 .pt 权重文件");
            return null;
        }
        if (Amlab_MonitorEntries.Any(entry => !ReferenceEquals(entry, current) &&
            string.Equals(entry.WeightPath, path, StringComparison.OrdinalIgnoreCase)))
        {
            Amlab_MonitorStatus = Text("该权重文件已添加");
            return null;
        }
        Amlab_MonitorStatus = "";
        return path;
    }

    internal static string? PickWeightFile(Window owner)
    {
        var dialog = new OpenFileDialog
        {
            Title = Text("添加权重…"),
            Filter = "PyTorch (*.pt)|*.pt",
            CheckFileExists = true
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    private bool CanEdit() => _loaded;
    private bool CanEditSelection() => CanEdit() && Amlab_MonitorHasSelection;
    private static string Text(string key) => UiText.Current.Translate(key);
}
