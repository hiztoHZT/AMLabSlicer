using System.Net.Http;
using System.Windows.Controls;
using AMLabSlicer.Plugin.Abstractions;
using AMLabSlicer.Plugin.Wpf;
using AMLabSlicer.Plugins.Agent.Services;
using AMLabSlicer.Plugins.Agent.ViewModel;
using AMLabSlicer.Plugins.Agent.Views;
namespace AMLabSlicer.Plugins.Agent;

public sealed class AgentPlugin : IPanelPlugin
{
    private HttpClient? _client;
    private AgentChatViewModel? _chat;
    private AgentSettingsViewModel? _settings;
    public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _settings = new AgentSettingsViewModel(context.Settings);
        _client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _chat = new AgentChatViewModel(_settings, new OpenAiCompatibleChatService(_client), context.OpenSettings);
        _settings.Chat = _chat;
        return Task.CompletedTask;
    }
    public UserControl CreatePanel() => new AgentChatPanelView { DataContext = _chat ?? throw new InvalidOperationException("插件未初始化") };
    public UserControl CreateSettingsPage() => new AgentSettingsView { DataContext = _settings ?? throw new InvalidOperationException("插件未初始化") };
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_chat is not null)
            {
                _chat.Dispose();
                var tasks = new[] { _chat.SendCommand.ExecutionTask, _chat.LoadModelsCommand.ExecutionTask }.OfType<Task>();
                await Task.WhenAll(tasks).WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally { _chat = null; _settings = null; _client?.Dispose(); _client = null; }
    }
}
