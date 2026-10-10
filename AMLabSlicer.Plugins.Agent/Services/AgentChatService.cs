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

// Provider transport is independent from the UI and future tool/extension implementations.
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

    public async Task<string> SendAsync(AgentSettings settings, IReadOnlyList<AgentMessage> conversation, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.AgentModel)) throw new InvalidOperationException("请先在“插件 → Agent”中填写模型名称，或在插件设置中读取模型列表。");
        using var request = Request(settings, HttpMethod.Post, "chat/completions");
        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(settings.AgentSystemPrompt)) messages.Add(new { role = "system", content = settings.AgentSystemPrompt });
        messages.AddRange(conversation.Select(m => (object)new { role = m.Role, content = m.Content }));
        request.Content = JsonContent.Create(new { model = settings.AgentModel.Trim(), messages, stream = false });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.AgentTimeoutSeconds));
        using var response = await client.SendAsync(request, timeout.Token);
        await EnsureSuccess(response);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);
        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            throw new InvalidOperationException("服务未返回有效的对话结果。");
        var message = choices[0].GetProperty("message");
        var text = message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String ? content.GetString() : null;
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("服务返回空内容。请检查模型是否支持 Chat Completions 文本对话。");
        return text;
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
