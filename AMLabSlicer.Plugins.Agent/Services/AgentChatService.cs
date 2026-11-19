using AMLabSlicer.Plugin.Abstractions;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AMLabSlicer.Plugins.Agent.Models;

namespace AMLabSlicer.Plugins.Agent.Services;

public record AgentMessage(string Role, string Content);
public interface IAgentChatService
{
    Task<string> SendAsync(AgentSettings settings, IReadOnlyList<AgentMessage> conversation, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ListModelsAsync(AgentSettings settings, CancellationToken cancellationToken);
}

public sealed record AgentReply(string Text, IReadOnlyList<SliceParameterChange>? Changes = null);
public sealed class OpenAiCompatibleChatService(HttpClient client) : IAgentChatService
{
    public static Uri ResolveEndpoint(string endpoint, string resource)
    {
        if (!Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("服务地址应为 HTTP/HTTPS 基础地址，例如 http://localhost:11434/v1/。");
        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) path = path[..^17];
        return new UriBuilder(uri) { Path = path + "/" + resource }.Uri;
    }

    private static HttpRequestMessage Request(AgentSettings settings, HttpMethod method, string resource)
    {
        var request = new HttpRequestMessage(method, ResolveEndpoint(settings.AgentEndpoint, resource));
        if (!string.IsNullOrWhiteSpace(settings.AgentApiKeyVariable))
        {
            var key = Environment.GetEnvironmentVariable(settings.AgentApiKeyVariable.Trim());
            if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("指定的 API Key 环境变量未设置。请设置后重新启动软件。");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }
        return request;
    }

    public async Task<string> SendAsync(AgentSettings settings, IReadOnlyList<AgentMessage> conversation, CancellationToken cancellationToken) =>
        (await SendWithParametersAsync(settings, conversation, null, cancellationToken)).Text;

    public async Task<AgentReply> SendWithParametersAsync(AgentSettings settings, IReadOnlyList<AgentMessage> conversation,
        SliceParameterSnapshot? snapshot, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.AgentModel)) throw new InvalidOperationException("请先在“插件 → Agent”中填写模型名称，或在插件设置中读取模型列表。");
        using var request = Request(settings, HttpMethod.Post, "chat/completions");
        var messages = new List<object>();
        var prompt = settings.AgentSystemPrompt == AgentSettings.LegacySystemPrompt
            ? AgentSettings.DefaultSystemPrompt : settings.AgentSystemPrompt;
        if (!string.IsNullOrWhiteSpace(prompt)) messages.Add(new { role = "system", content = prompt });
        if (snapshot != null)
        {
            var parameters = JsonSerializer.Serialize(new { snapshot.Algorithm, snapshot.Parameters });
            messages.Add(new
            {
                role = "system",
                content = "以下是宿主当前切片算法和参数。回答切片设置请求时优先结合实际值，不要假设有未列出的参数。" +
                    "用户希望提高打印或切片精细度时，优先考虑减小当前算法的层高或层高与喷嘴直径的比值。" +
                    "如果用户想调整设置，调用 propose_slice_parameters 建议可编辑参数的新值；应用由用户在界面确认。" +
                    "不得在文字回复里输出修改 JSON，也不得声称建议已经应用。普通参数查询直接回答。\n" + parameters
            });
        }
        messages.AddRange(conversation.Select(m => (object)new { role = m.Role, content = m.Content }));
        var body = new Dictionary<string, object>
        {
            ["model"] = settings.AgentModel.Trim(), ["messages"] = messages, ["stream"] = false
        };
        if (snapshot?.Parameters.Any(p => p.Editable) == true)
            body["tools"] = new[] { ParameterProposalTool };
        request.Content = JsonContent.Create(body);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.AgentTimeoutSeconds));
        using var response = await client.SendAsync(request, timeout.Token);
        await EnsureSuccess(response);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);
        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            throw new InvalidOperationException("服务未返回有效的对话结果。");
        var message = choices[0].GetProperty("message");
        var text = message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String ? content.GetString() : null;
        if (snapshot != null && message.TryGetProperty("tool_calls", out var calls) &&
            calls.ValueKind == JsonValueKind.Array && calls.GetArrayLength() > 0)
            return new AgentReply("", ReadProposal(snapshot, calls));

        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("服务返回空内容。请检查模型是否支持 Chat Completions 文本对话。");
        return new AgentReply(text);
    }

    private static readonly JsonElement ParameterProposalTool = JsonSerializer.Deserialize<JsonElement>("""
        {
          "type": "function",
          "function": {
            "name": "propose_slice_parameters",
            "description": "Propose changes to editable slice parameters. Does not apply them.",
            "parameters": {
              "type": "object",
              "properties": {
                "changes": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": { "key": { "type": "string" }, "value": { "type": "number" } },
                    "required": ["key", "value"],
                    "additionalProperties": false
                  }
                }
              },
              "required": ["changes"],
              "additionalProperties": false
            }
          }
        }
        """);

    private static IReadOnlyList<SliceParameterChange> ReadProposal(SliceParameterSnapshot snapshot, JsonElement calls)
    {
        var updates = new Dictionary<string, SliceParameterChange>(StringComparer.Ordinal);
        foreach (var call in calls.EnumerateArray())
        {
            var function = call.GetProperty("function");
            if (function.GetProperty("name").GetString() != "propose_slice_parameters")
                throw new InvalidOperationException("模型请求了未知的参数工具。");
            using var arguments = JsonDocument.Parse(function.GetProperty("arguments").GetString() ?? "");
            foreach (var item in arguments.RootElement.GetProperty("changes").EnumerateArray())
            {
                var key = item.GetProperty("key").GetString() ?? "";
                var number = item.GetProperty("value");
                var parameter = snapshot.Parameters.FirstOrDefault(p => p.Key == key && p.Editable);
                if (parameter == null || number.ValueKind != JsonValueKind.Number ||
                    !number.TryGetDouble(out var value) || !double.IsFinite(value) ||
                    value < parameter.Minimum || value > parameter.Maximum)
                    throw new InvalidOperationException("模型建议的参数不可修改或超出范围。");
                if (Equals(parameter.Value, value)) continue;
                if (!updates.TryAdd(key, new(key, value)))
                    throw new InvalidOperationException("模型对同一个参数提出了多次修改。");
            }
        }
        if (updates.Count == 0) throw new InvalidOperationException("模型没有提出有效的参数改动。");
        return updates.Values.ToArray();
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(AgentSettings settings, CancellationToken cancellationToken)
    {
        using var request = Request(settings, HttpMethod.Get, "models");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Min(settings.AgentTimeoutSeconds, 30)));
        using var response = await client.SendAsync(request, timeout.Token);
        await EnsureSuccess(response);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("服务未返回兼容的模型列表，请手动填写模型名称。");
        return data.EnumerateArray().Where(x => x.TryGetProperty("id", out _)).Select(x => x.GetProperty("id").GetString() ?? "").Where(x => x.Length > 0).Distinct().OrderBy(x => x).ToArray();
    }
    private static Task EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "服务拒绝认证，请检查 API Key 环境变量。",
                HttpStatusCode.NotFound => "服务路径或模型不存在，请检查地址和模型名称。",
                HttpStatusCode.TooManyRequests => "服务请求额度或频率已达到限制。",
                _ => $"模型服务请求失败（HTTP {(int)response.StatusCode}）。"
            }, null, response.StatusCode);
        return Task.CompletedTask;
    }
}
