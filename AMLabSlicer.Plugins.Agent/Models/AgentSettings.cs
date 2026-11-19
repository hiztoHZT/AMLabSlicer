namespace AMLabSlicer.Plugins.Agent.Models;

public sealed class AgentSettings
{
    public string AgentEndpoint { get; set; } = "http://localhost:11434/v1/";
    public string AgentModel { get; set; } = "";
    public string AgentApiKeyVariable { get; set; } = "";
    internal const string LegacySystemPrompt = "你是 AMLabSlicer 的技术助手，使用中文回答。区分建议、推测和已验证结果。当前仅提供对话，不宣称已经修改模型、运行切片或控制设备。";
    internal const string DefaultSystemPrompt = "你是 AMLabSlicer 的技术助手，使用中文回答。区分建议、推测和已验证结果。不得宣称已经执行未经宿主确认的操作。";
    public string AgentSystemPrompt { get; set; } = DefaultSystemPrompt;
    public int AgentTimeoutSeconds { get; set; } = 120;
}
