using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AMLabSlicer.Plugins.Agent.Services;

namespace AMLabSlicer.Plugins.Agent.ViewModel;

public partial class ChatMessageViewModel(string role, string text) : ObservableObject
{
    public string Role { get; } = role;
    public string Sender => Role switch { "user" => "你", "assistant" => "Agent", _ => "提示" };
    public string Text { get; } = text;
    public bool IsUser => Role == "user";
    [ObservableProperty] private string _deliveryStatus = "";
}

public partial class AgentChatViewModel : ObservableObject, IDisposable
{
    private readonly IAgentChatService _chat;
    private readonly Action _openSettings;
    private bool _disposed;
    private readonly List<AgentMessage> _history = new();
    private string _sessionEndpoint = "";
    private string _sessionModel = "";
    [ObservableProperty] private string? _selectedModel;
    partial void OnSelectedModelChanged(string? value) { if (!string.IsNullOrWhiteSpace(value)) Preferences.AgentModel = value; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(NewConversationCommand), nameof(LoadModelsCommand))]
    private bool _isLoadingModels;
    public AgentSettingsViewModel Preferences { get; }
    public ObservableCollection<ChatMessageViewModel> Messages { get; } = new();
    public ObservableCollection<string> Models { get; } = new();
    public string ConnectionSummary => string.IsNullOrWhiteSpace(Preferences.AgentModel) ? "尚未配置模型" : Preferences.AgentModel;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _input = "";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(NewConversationCommand), nameof(LoadModelsCommand))]
    private bool _isSending;
    [ObservableProperty] private string _status = "就绪";

    public AgentChatViewModel(AgentSettingsViewModel preferences, IAgentChatService chat, Action openSettings)
    {
        Preferences = preferences; _chat = chat; _openSettings = openSettings;
        Preferences.PropertyChanged += PreferencesChanged;
        Messages.Add(new("system", "配置服务地址和模型后即可开始对话。支持 OpenAI 兼容接口与本地模型服务。"));
    }
    private void PreferencesChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
            if (e.PropertyName == nameof(AgentSettingsViewModel.AgentModel))
            { OnPropertyChanged(nameof(ConnectionSummary)); SelectedModel = Models.Contains(Preferences.AgentModel) ? Preferences.AgentModel : null; }
    }
    public void Dispose()
    {
        _disposed = true;
        SendCommand.Cancel(); LoadModelsCommand.Cancel();
        SendCommand.NotifyCanExecuteChanged(); NewConversationCommand.NotifyCanExecuteChanged(); LoadModelsCommand.NotifyCanExecuteChanged();
        Preferences.PropertyChanged -= PreferencesChanged;
    }
    private bool CanSend() => !_disposed && !IsSending && !IsLoadingModels && !string.IsNullOrWhiteSpace(Input);
    private bool CanReset() => !_disposed && !IsSending && !IsLoadingModels;
    [RelayCommand(CanExecute = nameof(CanSend), IncludeCancelCommand = true)]
    private async Task SendAsync(CancellationToken cancellationToken)
    {
        var prompt = Input.Trim();
        var settings = Preferences.Snapshot();
        if (_sessionEndpoint != settings.AgentEndpoint || _sessionModel != settings.AgentModel)
        {
            _history.Clear(); _sessionEndpoint = settings.AgentEndpoint; _sessionModel = settings.AgentModel;
        }
        var user = new ChatMessageViewModel("user", prompt);
        Messages.Add(user); Input = ""; IsSending = true; Status = "正在生成回复…";
        try
        {
            var request = _history.Concat(new[] { new AgentMessage("user", prompt) }).ToArray();
            var answer = await _chat.SendAsync(settings, request, cancellationToken);
            _history.Add(new("user", prompt)); _history.Add(new("assistant", answer));
            Messages.Add(new("assistant", answer)); Status = "回复完成";
        }
        catch (OperationCanceledException)
        {
            user.DeliveryStatus = cancellationToken.IsCancellationRequested ? "已取消" : "请求超时";
            Status = user.DeliveryStatus; if (Input.Length == 0) Input = prompt;
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or InvalidOperationException or System.Text.Json.JsonException or KeyNotFoundException or ArgumentException)
        {
            user.DeliveryStatus = "未完成"; Status = ex.Message;
            if (Input.Length == 0) Input = prompt;
        }
        finally { IsSending = false; }
    }
    [RelayCommand(CanExecute = nameof(CanReset))]
    private void NewConversation() { _history.Clear(); Messages.Clear(); Status = "已开始新对话"; }
    [RelayCommand] private void OpenSettings() => _openSettings();
    [RelayCommand(CanExecute = nameof(CanReset), IncludeCancelCommand = true)]
    private async Task LoadModelsAsync(CancellationToken cancellationToken)
    {
        Status = "正在读取模型…"; IsLoadingModels = true;
        var settings = Preferences.Snapshot();
        try
        {
            var models = await _chat.ListModelsAsync(settings, cancellationToken);
            if (settings.AgentEndpoint != Preferences.AgentEndpoint) { Status = "服务设置已更改，请重新读取模型。"; return; }
            Models.Clear(); foreach (var model in models) Models.Add(model);
            if (Models.Count == 0) Status = "服务没有可用模型，请先在模型服务中安装或启用模型。";
            else { if (!Models.Contains(Preferences.AgentModel)) Preferences.AgentModel = Models[0]; SelectedModel = Preferences.AgentModel; Status = $"已读取 {Models.Count} 个模型"; }
        }
        catch (OperationCanceledException) { Status = "读取已取消或超时"; }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or InvalidOperationException or System.Text.Json.JsonException or KeyNotFoundException or ArgumentException) { Status = ex.Message; }
        finally { IsLoadingModels = false; }
    }
}
