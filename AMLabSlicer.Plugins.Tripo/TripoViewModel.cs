using System.IO;
using AMLabSlicer.Plugin.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AMLabSlicer.Plugins.Tripo;

public sealed class TripoSettings
{
    public string KeyVariable { get; set; } = "TRIPO_API_KEY";
    public string Model { get; set; } = "v3.1-20260211";
}

public partial class TripoViewModel : ObservableObject, IDisposable
{
    private readonly IPluginContext _context;
    private readonly TripoService _service;
    private readonly CancellationTokenSource _lifetime = new();
    [ObservableProperty] private string _keyVariable;
    [ObservableProperty] private string _model;
    [ObservableProperty] private string _prompt = "";
    [ObservableProperty] private string _imageUrl = "";
    [ObservableProperty] private bool _autoImport;
    [ObservableProperty] private string _status = "就绪；生成会消耗 Tripo API 积分";
    [ObservableProperty] private string _localPath = "";
    [ObservableProperty] private string _saveStatus = "设置自动保存";
    private bool _stopped;
    public TripoViewModel(IPluginContext context, TripoService service)
    {
        _context = context; _service = service;
        var settings = context.Settings.Load<TripoSettings>();
        _keyVariable = settings.KeyVariable; _model = settings.Model;
        GenerateCommand.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(GenerateCommand.IsRunning)) ImportCommand.NotifyCanExecuteChanged(); };
        ImportCommand.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ImportCommand.IsRunning)) GenerateCommand.NotifyCanExecuteChanged(); };
    }
    partial void OnKeyVariableChanged(string value) => Save();
    partial void OnModelChanged(string value) => Save();
    private void Save()
    {
        try { _context.Settings.Save(new TripoSettings { KeyVariable = KeyVariable, Model = Model }); SaveStatus = "插件设置已保存"; }
        catch (Exception ex) { SaveStatus = "保存失败：" + ex.Message; }
    }
    [RelayCommand] private void Settings() => _context.OpenSettings();
    private bool CanGenerate() => !_stopped && !ImportCommand.IsRunning;
    [RelayCommand(CanExecute = nameof(CanGenerate), IncludeCancelCommand = true)]
    private async Task GenerateAsync(CancellationToken cancellationToken)
    {
        ImportCommand.NotifyCanExecuteChanged();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var importAfter = AutoImport;
        try
        {
            var key = Environment.GetEnvironmentVariable(KeyVariable.Trim());
            if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("请先在插件设置中指定有效的 API Key 环境变量");
            if (string.IsNullOrWhiteSpace(Model)) throw new InvalidOperationException("请填写模型版本");
            Status = "正在提交生成任务";
            LocalPath = await _service.GenerateAsync(key, Model.Trim(), Prompt.Trim(), ImageUrl.Trim(),
                Path.Combine(Path.GetDirectoryName(_context.Settings.Location)!, "models"),
                new Progress<string>(s => { if (!_stopped) Status = s; }), linked.Token);
            Status = "生成完成，可以导入到视窗";
            if (importAfter) await ImportCoreAsync(linked.Token);
        }
        catch (OperationCanceledException) { Status = "已停止等待；云端任务可能继续计费"; }
        catch (Exception ex) { Status = "操作失败：" + ex.Message; }
        finally { ImportCommand.NotifyCanExecuteChanged(); }
    }
    partial void OnLocalPathChanged(string value) => ImportCommand.NotifyCanExecuteChanged();
    private bool CanImport() => !_stopped && !GenerateCommand.IsRunning && File.Exists(LocalPath);
    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        GenerateCommand.NotifyCanExecuteChanged();
        try { await ImportCoreAsync(_lifetime.Token); }
        catch (OperationCanceledException) { Status = "已取消导入"; }
        catch (Exception ex) { Status = "导入失败，模型文件已保留：" + ex.Message; }
        finally { GenerateCommand.NotifyCanExecuteChanged(); }
    }
    private async Task ImportCoreAsync(CancellationToken token)
    {
        if (_context is not IModelImportContext host) throw new InvalidOperationException("当前宿主不支持模型导入，请升级宿主");
        await host.ImportModelAsync(LocalPath, token);
        Status = "已导入到视窗；请确认模型尺寸与可打印性";
    }
    public void Dispose() { _stopped = true; _lifetime.Cancel(); GenerateCommand.Cancel(); GenerateCommand.NotifyCanExecuteChanged(); ImportCommand.NotifyCanExecuteChanged(); }
}
