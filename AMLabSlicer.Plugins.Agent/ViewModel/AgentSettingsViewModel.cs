using AMLabSlicer.Plugin.Abstractions;
using AMLabSlicer.Plugins.Agent.Models;
using CommunityToolkit.Mvvm.ComponentModel;
namespace AMLabSlicer.Plugins.Agent.ViewModel;

public partial class AgentSettingsViewModel : ObservableObject
{
    private readonly IPluginSettingsStore _store;
    [ObservableProperty] private string _agentEndpoint = "";
    [ObservableProperty] private string _agentModel = "";
    [ObservableProperty] private string _agentApiKeyVariable = "";
    [ObservableProperty] private string _agentSystemPrompt = "";
    [ObservableProperty] private string _saveStatus = "设置自动保存";
    private int _agentTimeoutSeconds = 120;
    public int AgentTimeoutSeconds { get => _agentTimeoutSeconds; set => SetProperty(ref _agentTimeoutSeconds, Math.Clamp(value, 10, 600)); }
    public string SettingsLocation => _store.Location;
    public AgentChatViewModel? Chat { get; set; }
    public AgentSettingsViewModel(IPluginSettingsStore store)
    {
        _store = store;
        try
        {
            var settings = store.Load<AgentSettings>();
            AgentEndpoint = settings.AgentEndpoint ?? ""; AgentModel = settings.AgentModel ?? "";
            AgentApiKeyVariable = settings.AgentApiKeyVariable ?? ""; AgentSystemPrompt = settings.AgentSystemPrompt ?? "";
            AgentTimeoutSeconds = settings.AgentTimeoutSeconds;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            // A corrupt configuration must stay visible rather than being overwritten silently.
            throw new InvalidOperationException("Agent 配置无法读取：" + ex.Message, ex);
        }
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SaveStatus) or nameof(Chat)) return;
            try { _store.Save(Snapshot()); SaveStatus = "插件设置已保存"; }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { SaveStatus = "保存失败：" + ex.Message; }
        };
    }
    public AgentSettings Snapshot() => new()
    {
        AgentEndpoint = AgentEndpoint, AgentModel = AgentModel, AgentApiKeyVariable = AgentApiKeyVariable,
        AgentSystemPrompt = AgentSystemPrompt, AgentTimeoutSeconds = AgentTimeoutSeconds
    };
}
