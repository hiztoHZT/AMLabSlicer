using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace AMLabSlicer.Services;

public sealed class MoonrakerClient : IDisposable
{
    public const string DefaultAddress = "http://172.20.10.2/";
    private readonly HttpClient http;
    public Uri Endpoint { get; }
    public MoonrakerClient(string address, string apiKey = "", HttpMessageHandler? handler = null)
    {
        Endpoint = NormalizeAddress(address);
        http = new HttpClient(handler ?? new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
            { BaseAddress = Endpoint, Timeout = TimeSpan.FromSeconds(30) };
        if (!string.IsNullOrWhiteSpace(apiKey)) http.DefaultRequestHeaders.Add("X-Api-Key", apiKey.Trim());
    }
    internal static Uri NormalizeAddress(string? address)
    {
        var value = address?.Trim() ?? "";
        if (!value.Contains("://", StringComparison.Ordinal))
            value = "http://" + value;
        if (!Uri.TryCreate(value.TrimEnd('/') + "/", UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https") || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new ArgumentException("请输入有效的 Moonraker 地址。不要在地址中放密码或查询参数。");
        return uri;
    }

    private async Task<JsonElement> SendAsync(HttpRequestMessage request, CancellationToken token, bool includeDeviceError = false)
    {
        using (request)
        using (var response = await http.SendAsync(request, token))
        {
            if (!response.IsSuccessStatusCode)
            {
                if (includeDeviceError && response.Content.Headers.ContentType?.MediaType == "application/json")
                {
                    var body = await response.Content.ReadAsStringAsync(token);
                    try
                    {
                        using var error = JsonDocument.Parse(body);
                        if (ReadText(ReadProperty(error.RootElement, "error"), "message") is { Length: > 0 } detail)
                            throw new HttpRequestException(detail, null, response.StatusCode);
                    }
                    catch (JsonException) { }
                }
                throw new HttpRequestException($"Moonraker HTTP {(int)response.StatusCode}：{response.ReasonPhrase}。401/403 请检查 API Key 或授权配置；写入失败后请先刷新状态，不要盲目重试。", null, response.StatusCode);
            }
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            if (json.RootElement.TryGetProperty("error", out _)) throw new InvalidOperationException("Moonraker 返回 API 错误，请检查设备网页日志。");
            return json.RootElement.GetProperty("result").Clone();
        }
    }
    public Task<JsonElement> StatusAsync(CancellationToken token) => SendAsync(new(HttpMethod.Get,
        "printer/objects/query?webhooks&print_stats&extruder&heater_bed&virtual_sdcard&toolhead"), token);
    public async Task<MoonrakerTelemetry> ReadTelemetryAsync(CancellationToken token)
    {
        JsonElement status;
        try
        {
            var result = await SendAsync(new(HttpMethod.Get,
                "printer/objects/query?webhooks=state&print_stats=state,filename&virtual_sdcard=progress&extruder=temperature,target&heater_bed=temperature,target"), token);
            status = result.GetProperty("status");
            if (status.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Moonraker returned an invalid printer status.");
        }
        catch (HttpRequestException error) when (error.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
        {
            var info = await SendAsync(new(HttpMethod.Get, "server/info"), token);
            return new MoonrakerTelemetry(ReadText(info, "klippy_state"), null, null, null, null, null, null, null);
        }

        var print = ReadProperty(status, "print_stats");
        var extruder = ReadProperty(status, "extruder");
        var bed = ReadProperty(status, "heater_bed");
        var progress = ReadNumber(ReadProperty(status, "virtual_sdcard"), "progress");
        return new MoonrakerTelemetry(
            ReadText(ReadProperty(status, "webhooks"), "state"), ReadText(print, "state"), ReadText(print, "filename"),
            progress is >= 0 and <= 1 ? progress : null,
            ReadNumber(extruder, "temperature"), ReadNumber(extruder, "target"),
            ReadNumber(bed, "temperature"), ReadNumber(bed, "target"));
    }

    private static JsonElement ReadProperty(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) ? value : default;

    private static string? ReadText(JsonElement parent, string name)
    {
        var value = ReadProperty(parent, name);
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static double? ReadNumber(JsonElement parent, string name)
    {
        var value = ReadProperty(parent, name);
        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number)
            ? number : null;
    }

    internal async Task<string> ReadConsoleHistoryAsync(CancellationToken token)
    {
        var result = await SendAsync(new(HttpMethod.Get, "server/gcode_store?count=100"), token);
        var entries = result.GetProperty("gcode_store").EnumerateArray();
        return string.Join(Environment.NewLine, entries.Select(entry =>
        {
            var message = ReadText(entry, "message") ?? "";
            return ReadText(entry, "type") == "command" ? $"> {message}" : message;
        }));
    }

    internal async Task SendConsoleCommandAsync(string command, bool emergencyStop, CancellationToken token)
    {
        var request = emergencyStop
            ? new HttpRequestMessage(HttpMethod.Post, "printer/emergency_stop")
            : new HttpRequestMessage(HttpMethod.Post, "printer/gcode/script") { Content = JsonContent.Create(new { script = command }) };
        var result = await SendAsync(request, token, includeDeviceError: true);
        if (result.ValueKind != JsonValueKind.String || result.GetString() != "ok")
            throw new InvalidDataException("Moonraker did not acknowledge the command.");
    }

    public Task<JsonElement> FilesAsync(CancellationToken token) => SendAsync(new(HttpMethod.Get, "server/files/list?root=gcodes"), token);
    public async Task<string> UploadAsync(string file, CancellationToken token)
    {
        if (!new[] { ".gcode", ".gco", ".g" }.Contains(Path.GetExtension(file).ToLowerInvariant())) throw new ArgumentException("请选择 G-code 文件。");
        // A unique remote name avoids silently replacing existing jobs. No print=true parameter.
        var name = Path.GetFileNameWithoutExtension(file) + "_" + Guid.NewGuid().ToString("N") + ".gcode";
        var form = new MultipartFormDataContent();
        form.Add(new StringContent("gcodes"), "root");
        form.Add(new StreamContent(File.OpenRead(file)), "file", name);
        var result = await SendAsync(new(HttpMethod.Post, "server/files/upload") { Content = form }, token);
        return result.GetProperty("item").GetProperty("path").GetString() ?? throw new InvalidDataException("上传响应缺少文件名。");
    }
    public Task<JsonElement> StartAsync(string filename, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(filename) || filename.StartsWith('/') || filename.Split('/').Contains("..")) throw new ArgumentException("无效的远程文件名。");
        return SendAsync(new(HttpMethod.Post, "printer/print/start") { Content = JsonContent.Create(new { filename }) }, token);
    }
    public Task<JsonElement> JobAsync(string action, CancellationToken token)
    {
        if (action is not ("pause" or "resume" or "cancel")) throw new ArgumentException("未知打印操作。");
        return SendAsync(new(HttpMethod.Post, "printer/print/" + action), token);
    }
    public void Dispose() => http.Dispose();
}

public sealed record MoonrakerTelemetry(string? KlipperState, string? PrintState, string? Filename,
    double? Progress, double? NozzleTemperature, double? NozzleTarget, double? BedTemperature, double? BedTarget);
