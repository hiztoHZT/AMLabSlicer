using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using AMLabSlicer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace AMLabSlicer.ViewModel;

public partial class DeviceControlViewModel : ObservableObject, IDisposable
{
    private MoonrakerClient? client;
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool busy;
    private bool disposed;
    [ObservableProperty] private string address = MoonrakerClient.DefaultAddress;
    [ObservableProperty] private string status = "未连接";
    [ObservableProperty] private string detail = "";
    [ObservableProperty] private string? selectedFile;
    public string ApiKey { private get; set; } = "";
    public ObservableCollection<string> Files { get; } = new();
    private string state = "";
    private bool ready;
    public bool CanOperate => client != null && ready && !busy && !disposed;
    public bool CanStart => CanOperate && state is "standby" or "complete" or "cancelled" && !string.IsNullOrWhiteSpace(SelectedFile);
    public bool CanPause => CanOperate && state == "printing";
    public bool CanResume => CanOperate && state == "paused";
    public bool CanCancel => CanOperate && state is "printing" or "paused";
    public bool CanConnect => !busy && !disposed;
    public DeviceControlViewModel()
    {
        var settings = MoonrakerConnectionSettings.Load();
        address = settings.Address;
        timer.Interval = TimeSpan.FromSeconds(settings.RefreshSeconds);
        timer.Tick += Tick;
    }
    private async void Tick(object? sender, EventArgs e) { if (!busy) await Run(RefreshStatus); }
    partial void OnSelectedFileChanged(string? value) => UpdateCommands();
    private void UpdateCommands()
    {
        OnPropertyChanged(nameof(CanOperate)); OnPropertyChanged(nameof(CanConnect));
        ConnectCommand.NotifyCanExecuteChanged(); UploadCommand.NotifyCanExecuteChanged(); RefreshCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged(); PauseCommand.NotifyCanExecuteChanged(); ResumeCommand.NotifyCanExecuteChanged(); CancelCommand.NotifyCanExecuteChanged();
    }
    private async Task Run(Func<Task> operation)
    {
        if (busy || disposed) return;
        busy = true; UpdateCommands();
        try { await operation(); }
        catch (OperationCanceledException) { if (!disposed) { ready = false; Status = "请求超时或已取消；请刷新确认设备状态。"; } }
        catch (Exception ex) { ready = false; Status = ex.Message; }
        finally { busy = false; if (!disposed) UpdateCommands(); }
    }
    [RelayCommand(CanExecute = nameof(CanConnect))]
    private Task Connect() => Run(async () =>
    {
        timer.Stop(); client?.Dispose(); client = null; ready = false; Files.Clear(); SelectedFile = null;
        client = new MoonrakerClient(Address, ApiKey);
        var settings = MoonrakerConnectionSettings.Load() with { Address = client.Endpoint.AbsoluteUri };
        Address = settings.Address;
        timer.Interval = TimeSpan.FromSeconds(settings.RefreshSeconds);
        try { settings.Save(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning("Moonraker settings: {0}", error.Message);
        }
        await RefreshStatus(); await RefreshFiles(); timer.Start();
    });
    private async Task RefreshStatus()
    {
        var result = await client!.StatusAsync(lifetime.Token);
        var s = result.GetProperty("status");
        ready = s.GetProperty("webhooks").GetProperty("state").GetString() == "ready";
        var print = s.GetProperty("print_stats"); state = print.GetProperty("state").GetString() ?? "";
        string Temperature(string name) => s.TryGetProperty(name, out var t)
            ? $"{t.GetProperty("temperature").GetDouble():F1} / {t.GetProperty("target").GetDouble():F0} ℃" : "不可用";
        var progress = s.TryGetProperty("virtual_sdcard", out var sd) ? sd.GetProperty("progress").GetDouble() : 0;
        Status = $"{client.Endpoint}  |  Klipper {(ready ? "ready" : "未就绪")}  |  {state}  |  {progress:P0}";
        Detail = $"喷嘴：{Temperature("extruder")}    热床：{Temperature("heater_bed")}\n当前文件：{print.GetProperty("filename").GetString()}\n更新：{DateTime.Now:HH:mm:ss}";
    }
    private async Task RefreshFiles()
    {
        var files = await client!.FilesAsync(lifetime.Token); var selection = SelectedFile;
        Files.Clear(); foreach (var item in files.EnumerateArray()) Files.Add(item.GetProperty("path").GetString()!);
        SelectedFile = Files.Contains(selection ?? "") ? selection : null;
    }
    [RelayCommand(CanExecute = nameof(CanOperate))] private Task Refresh() => Run(async () => { await RefreshStatus(); await RefreshFiles(); });
    [RelayCommand(CanExecute = nameof(CanOperate))] private async Task Upload()
    {
        var dialog = new OpenFileDialog { Filter = "G-code|*.gcode;*.gco;*.g", CheckFileExists = true };
        if (dialog.ShowDialog() != true) return;
        await Run(async () => { var uploaded = await client!.UploadAsync(dialog.FileName, lifetime.Token); await RefreshFiles(); SelectedFile = uploaded; Status = "上传完成，尚未开始打印：" + uploaded; });
    }
    [RelayCommand(CanExecute = nameof(CanStart))] private async Task Start()
    {
        var file = SelectedFile;
        if (MessageBox.Show($"设备：{client!.Endpoint}\n文件：{file}\n\n确认该 G-code 已适配此机器并开始打印？设备将执行文件中的运动和加热指令。", "开始打印", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await Run(async () => { await RefreshStatus(); if (!ready || state is not ("standby" or "complete" or "cancelled")) throw new InvalidOperationException("设备当前不可开始新任务。"); await client!.StartAsync(file!, lifetime.Token); await RefreshStatus(); });
    }
    private Task Job(string action, string expected) => Run(async () =>
    {
        await RefreshStatus();
        if (!ready || (action == "cancel" ? state is not ("printing" or "paused") : state != expected)) throw new InvalidOperationException("设备状态已改变，请刷新后重试。");
        await client!.JobAsync(action, lifetime.Token); await RefreshStatus();
    });
    [RelayCommand(CanExecute = nameof(CanPause))] private Task Pause() => Job("pause", "printing");
    [RelayCommand(CanExecute = nameof(CanResume))] private Task Resume() => Job("resume", "paused");
    [RelayCommand(CanExecute = nameof(CanCancel))] private Task Cancel()
        => MessageBox.Show("取消当前打印任务？", "取消打印", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes ? Job("cancel", "") : Task.CompletedTask;
    public void Dispose() { disposed = true; timer.Stop(); timer.Tick -= Tick; lifetime.Cancel(); client?.Dispose(); }
}
