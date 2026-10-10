using System.Text.Json;
namespace AMLabSlicer.Plugins.Agent.Services;

// Stable boundary reserved for later portable extension packages; no plugin is loaded here.
public sealed record AgentToolDescriptor(string Id, string Name, string Description, JsonElement InputSchema);
public interface IAgentTool
{
    AgentToolDescriptor Descriptor { get; }
    Task<string> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken);
}
public interface IAgentToolRegistry
{
    IReadOnlyList<AgentToolDescriptor> Tools { get; }
    bool TryGetTool(string id, out IAgentTool? tool);
}
public sealed class EmptyAgentToolRegistry : IAgentToolRegistry
{
    public IReadOnlyList<AgentToolDescriptor> Tools => Array.Empty<AgentToolDescriptor>();
    public bool TryGetTool(string id, out IAgentTool? tool) { tool = null; return false; }
}
