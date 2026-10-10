namespace AMLabSlicer.Models;

public sealed class UserPreferences
{
    public string Theme { get; set; } = "深色";
    public string Language { get; set; } = "zh-CN";
    public string FontFamilyName { get; set; } = "Microsoft YaHei";
    public double FontSize { get; set; } = 12;
    public bool EnableFXAA { get; set; } = true;
    public bool ShowEngineHostConsole { get; set; }
    public bool EnableSSAO { get; set; }
    public bool ShowCoordinateSystem { get; set; } = true;
    public bool ShowViewCube { get; set; } = true;
    public bool ShowBuildGrid { get; set; } = true;
    public bool ShowViewportStatus { get; set; } = true;
    public bool UseOrthographic { get; set; }
    public bool EnableDeleteConfirm { get; set; } = true;
    public bool EnableArrangeConfirm { get; set; } = true;
    public bool EnableSplitConfirm { get; set; } = true;
    public bool SplitUndoable { get; set; }
    public int UndoStackDepth { get; set; } = 25;
    public bool EnablePanelAnimations { get; set; } = true;
    public bool OpenDeveloperPanelOnStartup { get; set; }
    public double DeveloperPanelWidth { get; set; } = 360;

    public string AgentEndpoint { get; set; } = "http://localhost:11434/v1/";
    public string AgentModel { get; set; } = "";
    public string AgentApiKeyVariable { get; set; } = "";
    public string AgentSystemPrompt { get; set; } = "你是 AMLabSlicer 的技术助手，使用中文回答。区分建议、推测和已验证结果。当前仅提供对话，不宣称已经修改模型、运行切片或控制设备。";
    public int AgentTimeoutSeconds { get; set; } = 120;

    public void Normalize()
    {
        AgentTimeoutSeconds = Math.Clamp(AgentTimeoutSeconds, 10, 600);
        AgentEndpoint ??= "http://localhost:11434/v1/";
        AgentModel ??= "";
        AgentApiKeyVariable ??= "";
        AgentSystemPrompt ??= "";
        Theme = Theme == "浅色" ? "浅色" : "深色";
        Language = Language == "en" ? "en" : "zh-CN";
        FontFamilyName = string.IsNullOrWhiteSpace(FontFamilyName) ? "Microsoft YaHei" : FontFamilyName;
        FontSize = double.IsFinite(FontSize) ? Math.Clamp(FontSize, 10, 20) : 12;
        UndoStackDepth = Math.Clamp(UndoStackDepth, 0, 200);
        DeveloperPanelWidth = double.IsFinite(DeveloperPanelWidth) ? Math.Clamp(DeveloperPanelWidth, 300, 600) : 360;
    }
}
