using System.Net.Http;
using System.Text.RegularExpressions;
using AMLabSlicer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AMLabSlicer.Amlab_Monitor;

public partial class Amlab_MonitorViewModel
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(Amlab_MonitorSendConsoleCommand))]
    private string _amlab_MonitorConsoleInput = "";

    [ObservableProperty] private string _amlab_MonitorConsoleLog = "";
    [ObservableProperty] private string? _amlab_MonitorConsoleHistoryError;
    [ObservableProperty] private string? _amlab_MonitorConsoleStatus;

    private bool _amlab_MonitorSendingCommand;
    private bool _amlab_MonitorSendingStop;
    private int _amlab_MonitorConsoleRequest;

    private void ResetAmlab_MonitorConsole()
    {
        Amlab_MonitorConsoleLog = "";
        Amlab_MonitorConsoleInput = "";
        Amlab_MonitorConsoleHistoryError = null;
        Amlab_MonitorConsoleStatus = null;
        _amlab_MonitorSendingCommand = false;
        _amlab_MonitorSendingStop = false;
    }

    private async Task MonitorAmlab_MonitorConsoleAsync(MoonrakerClient client, CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            do
            {
                if (!Amlab_MonitorConsolePanelOpen)
                    continue;

                using var request = CancellationTokenSource.CreateLinkedTokenSource(token);
                request.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    var log = await client.ReadConsoleHistoryAsync(request.Token);
                    token.ThrowIfCancellationRequested();
                    Amlab_MonitorConsoleLog = log;
                    Amlab_MonitorConsoleHistoryError = null;
                }
                catch (Exception) when (!token.IsCancellationRequested)
                {
                    Amlab_MonitorConsoleHistoryError = "无法读取设备消息";
                }
            } while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private bool CanSendAmlab_MonitorConsole()
    {
        if (!DeviceMonitoring || _deviceClient is null ||
            string.IsNullOrWhiteSpace(Amlab_MonitorConsoleInput) ||
            Amlab_MonitorConsoleInput.Contains('\r') || Amlab_MonitorConsoleInput.Contains('\n'))
            return false;

        return Amlab_MonitorIsEmergencyStop(Amlab_MonitorConsoleInput.Trim())
            ? !_amlab_MonitorSendingStop
            : !_amlab_MonitorSendingCommand && !_amlab_MonitorSendingStop && DeviceTelemetry?.KlipperState == "ready";
    }

    [RelayCommand(CanExecute = nameof(CanSendAmlab_MonitorConsole), AllowConcurrentExecutions = true)]
    private async Task Amlab_MonitorSendConsoleAsync()
    {
        if (!CanSendAmlab_MonitorConsole() || _deviceClient is not { } client || _deviceMonitor is not { } monitor)
            return;

        var command = Amlab_MonitorConsoleInput.Trim();
        var stop = Amlab_MonitorIsEmergencyStop(command);
        if (stop)
            _amlab_MonitorSendingStop = true;
        else
            _amlab_MonitorSendingCommand = true;

        var request = ++_amlab_MonitorConsoleRequest;
        Amlab_MonitorConsoleInput = "";
        Amlab_MonitorConsoleStatus = Text("正在发送命令…");
        try
        {
            await client.SendConsoleCommandAsync(command, stop, monitor.Token);
            SetStatus(Text("设备已确认命令"));
        }
        catch (HttpRequestException error) when (error.StatusCode is { } code && (int)code is >= 400 and < 500)
        {
            SetStatus(string.Format(Text("命令被拒绝：{0}"), error.Message));
        }
        catch (Exception)
        {
            SetStatus(Text("未收到命令确认，请先检查设备状态"));
        }
        finally
        {
            if (ReferenceEquals(_deviceMonitor, monitor))
            {
                if (stop)
                    _amlab_MonitorSendingStop = false;
                else
                    _amlab_MonitorSendingCommand = false;
                Amlab_MonitorSendConsoleCommand.NotifyCanExecuteChanged();
            }
        }

        void SetStatus(string status)
        {
            if (ReferenceEquals(_deviceMonitor, monitor) && request == _amlab_MonitorConsoleRequest)
                Amlab_MonitorConsoleStatus = status;
        }
    }

    private static bool Amlab_MonitorIsEmergencyStop(string command) =>
        Regex.IsMatch(command, @"^(?:N\d+\s*)?M112(?=\s|;|\*|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
