namespace AMLabSlicer.Plugins.Agent.Models;

public sealed class AgentSettings
{
    public string AgentEndpoint { get; set; } = "http://localhost:11434/v1/";
    public string AgentModel { get; set; } = "";
    public string AgentApiKeyVariable { get; set; } = "";
    public string AgentSystemPrompt { get; set; } = "你是 AMLabSlicer 的技术助手，使用中文回答。区分建议、推测和已验证结果。当前仅提供对话，不宣称已经修改模型、运行切片或控制设备。";
    public int AgentTimeoutSeconds { get; set; } = 120;
}
