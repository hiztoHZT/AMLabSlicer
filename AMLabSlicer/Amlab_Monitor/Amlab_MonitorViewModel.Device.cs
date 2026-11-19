using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using AMLabSlicer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AMLabSlicer.Amlab_Monitor;

public partial class Amlab_MonitorViewModel
{
    private CancellationTokenSource? _deviceMonitor;
    private PeriodicTimer? _deviceTimer;
    private MoonrakerClient? _deviceClient;
    internal string MoonrakerApiKey { get; set; } = "";

    [ObservableProperty] private string _moonrakerAddress;
    [ObservableProperty] private string _deviceRefreshSeconds;
    [ObservableProperty] private string? _deviceSettingsStatus;
    [ObservableProperty] private string _deviceConnectionStatus = "未连接";
    [ObservableProperty] private string? _deviceConnectionError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeviceConnectionAction), nameof(CanEditDeviceConnection))]
    [NotifyCanExecuteChangedFor(nameof(Amlab_MonitorSendConsoleCommand))]
    private bool _deviceMonitoring;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KlipperStateLabel), nameof(PrintStateLabel), nameof(DeviceFilename))]
    [NotifyCanExecuteChangedFor(nameof(Amlab_MonitorSendConsoleCommand))]
    private MoonrakerTelemetry? _deviceTelemetry;

    public bool CanEditDeviceConnection => !DeviceMonitoring;
    public string DeviceConnectionAction => DeviceMonitoring ? "断开设备" : "连接设备";
    public string DeviceFilename => string.IsNullOrWhiteSpace(DeviceTelemetry?.Filename) ? "—" : DeviceTelemetry.Filename;
    public string KlipperStateLabel => DeviceTelemetry?.KlipperState switch
    {
        "ready" => "就绪",
        "startup" => "正在启动",
        "shutdown" => "已停机",
        "error" => "故障",
        "disconnected" => "未连接",
        { Length: > 0 } state => state,
        _ => "—"
    };
    public string PrintStateLabel => DeviceTelemetry?.PrintState switch
    {
        "standby" => "待机",
        "printing" => "打印中",
        "paused" => "已暂停",
        "complete" => "已完成",
        "cancelled" => "已取消",
        "error" => "故障",
        { Length: > 0 } state => state,
        _ => "—"
    };

    internal void ReloadDeviceSettings()
    {
        var settings = MoonrakerConnectionSettings.Load();
        MoonrakerAddress = settings.Address;
        DeviceRefreshSeconds = settings.RefreshSeconds.ToString(CultureInfo.CurrentCulture);
        DeviceSettingsStatus = null;
    }

    [RelayCommand]
    private void SaveDeviceSettings()
    {
        if (ReadDeviceSettings() is { } settings)
            ApplyDeviceSettings(settings, showConfirmation: true);
    }

    private MoonrakerConnectionSettings? ReadDeviceSettings()
    {
        if (!int.TryParse(DeviceRefreshSeconds, NumberStyles.Integer, CultureInfo.CurrentCulture, out var seconds) ||
            seconds is < 1 or > MoonrakerConnectionSettings.MaxRefreshSeconds)
        {
            DeviceSettingsStatus = Text("刷新间隔请输入 1 到 3600 秒的整数。");
            return null;
        }
        try { return new(MoonrakerClient.NormalizeAddress(MoonrakerAddress).AbsoluteUri, seconds); }
        catch (ArgumentException)
        {
            DeviceSettingsStatus = Text("请输入有效的 Moonraker 地址。不要在地址中放密码或查询参数。");
            return null;
        }
    }

    private void ApplyDeviceSettings(MoonrakerConnectionSettings settings, bool showConfirmation)
    {
        MoonrakerAddress = settings.Address;
        DeviceRefreshSeconds = settings.RefreshSeconds.ToString(CultureInfo.CurrentCulture);
        if (_deviceTimer is not null)
            _deviceTimer.Period = TimeSpan.FromSeconds(settings.RefreshSeconds);
        try
        {
            settings.Save();
            DeviceSettingsStatus = showConfirmation ? Text("设备设置已保存") : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            DeviceSettingsStatus = string.Format(CultureInfo.CurrentCulture, Text("无法保存设备设置：{0}"), error.Message);
        }
    }

    [RelayCommand]
    private void ToggleDeviceConnection()
    {
        if (_deviceMonitor is not null)
        {
            DisconnectDevice();
            return;
        }

        if (ReadDeviceSettings() is not { } settings)
            return;
        ApplyDeviceSettings(settings, showConfirmation: false);
        var monitor = new CancellationTokenSource();
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.RefreshSeconds));
        _deviceMonitor = monitor;
        _deviceTimer = timer;
        ResetAmlab_MonitorConsole();
        DeviceMonitoring = true;
        DeviceConnectionStatus = "正在连接设备…";
        DeviceConnectionError = null;
        _ = MonitorDeviceAsync(settings.Address, MoonrakerApiKey, monitor, timer);
    }

    internal void DisconnectDevice()
    {
        var monitor = _deviceMonitor;
        _deviceMonitor = null;
        _deviceTimer = null;
        _deviceClient = null;
        monitor?.Cancel();
        DeviceMonitoring = false;
        DeviceTelemetry = null;
        DeviceConnectionError = null;
        DeviceConnectionStatus = "未连接";
    }

    private async Task MonitorDeviceAsync(string address, string key, CancellationTokenSource monitor, PeriodicTimer timer)
    {
        var token = monitor.Token;
        var receivedTelemetry = false;
        Task? console = null;
        MoonrakerClient? client = null;
        try
        {
            client = new MoonrakerClient(address, key);
            _deviceClient = client;
            Amlab_MonitorSendConsoleCommand.NotifyCanExecuteChanged();
            console = MonitorAmlab_MonitorConsoleAsync(client, token);
            while (!token.IsCancellationRequested)
            {
                using var request = CancellationTokenSource.CreateLinkedTokenSource(token);
                request.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    var telemetry = await client.ReadTelemetryAsync(request.Token);
                    token.ThrowIfCancellationRequested();
                    DeviceTelemetry = telemetry;
                    DeviceConnectionStatus = "已连接";
                    DeviceConnectionError = null;
                    receivedTelemetry = true;
                }
                catch (Exception error) when (!token.IsCancellationRequested)
                {
                    DeviceTelemetry = null;
                    SetDeviceError(error);
                    if (!receivedTelemetry || error is HttpRequestException
                        { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden })
                        return;
                }
                if (!await timer.WaitForNextTickAsync(token))
                    break;
            }
        }
        catch (Exception) when (token.IsCancellationRequested) { }
        catch (Exception error) { SetDeviceError(error); }
        finally
        {
            if (ReferenceEquals(_deviceMonitor, monitor))
            {
                _deviceMonitor = null;
                _deviceTimer = null;
                _deviceClient = null;
                DeviceMonitoring = false;
            }
            monitor.Cancel();
            if (console is not null)
                await console;
            client?.Dispose();
            timer.Dispose();
            monitor.Dispose();
        }
    }

    private void SetDeviceError(Exception error)
    {
        DeviceConnectionStatus = "设备读取失败";
        DeviceConnectionError = error switch
        {
            HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } =>
                Text("Moonraker 拒绝访问，请检查 API Key 或授权配置。"),
            HttpRequestException { StatusCode: { } code } =>
                string.Format(CultureInfo.CurrentCulture, Text("Moonraker 返回 HTTP {0}。"), (int)code),
            HttpRequestException or OperationCanceledException =>
                Text("无法读取 Moonraker，请检查设备地址和网络。"),
            _ => error.Message
        };
    }
}
