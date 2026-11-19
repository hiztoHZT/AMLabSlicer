using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace AMLabSlicer.Plugins.Tripo;

public sealed class TripoService(HttpClient client)
{
    public const string ApiRoot = "https://openapi.tripo3d.ai/v3/";
    public async Task<string> GenerateAsync(string key, string model, string prompt, string imageUrl,
        string directory, IProgress<string> progress, CancellationToken token)
    {
        var image = !string.IsNullOrWhiteSpace(imageUrl);
        if (image && (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https"))
            throw new InvalidOperationException("图片地址必须为 HTTPS URL");
        if (!image && (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 1024))
            throw new InvalidOperationException("请输入不超过 1024 字符的模型描述");
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiRoot + "generation/" + (image ? "image-to-model" : "text-to-model"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = JsonContent.Create(image
            ? (object)new { input = imageUrl, model, texture = false }
            : new { prompt, model, texture = false });
        using var created = await SendAsync(request, token);
        var id = created.RootElement.GetProperty("data").GetProperty("task_id").GetString()
            ?? throw new InvalidDataException("Tripo 未返回任务 ID");
        progress.Report("Task: " + id);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        string? url = null;
        while (url is null)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), timeout.Token);
            using var query = new HttpRequestMessage(HttpMethod.Get, ApiRoot + "tasks/" + Uri.EscapeDataString(id));
            query.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var result = await SendAsync(query, timeout.Token);
            var data = result.RootElement.GetProperty("data");
            var status = data.GetProperty("status").GetString();
            progress.Report($"Task: {id} · {status} · {(data.TryGetProperty("progress", out var p) ? p.ToString() : "0")}%");
            if (status is "failed" or "cancelled" or "banned" or "expired") throw new InvalidOperationException("Tripo task: " + status);
            if (status == "success") url = data.GetProperty("output").GetProperty("model_url").GetString()
                ?? throw new InvalidDataException("Tripo 未返回模型地址");
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var download) || download.Scheme != "https") throw new InvalidDataException("模型下载地址无效");
        Directory.CreateDirectory(directory);
        // Local name never uses remote paths/task IDs. Partial files cannot be imported.
        var destination = Path.Combine(directory, "Tripo_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")[..8] + ".glb");
        var partial = destination + ".partial";
        try
        {
            // Do not forward the API credential to the model CDN.
            using var response = await client.GetAsync(download, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
            await using (var output = File.Create(partial))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    total += read;
                    if (total > 512L * 1024 * 1024) throw new InvalidDataException("模型文件超过 512 MB");
                    await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                }
            }
            var header = new byte[12];
            using (var file = File.OpenRead(partial))
                if (file.Read(header) != 12 || BitConverter.ToUInt32(header, 0) != 0x46546C67 || BitConverter.ToUInt32(header, 4) != 2 || BitConverter.ToUInt32(header, 8) != file.Length)
                    throw new InvalidDataException("下载结果不是有效的 GLB 2.0 文件");
            File.Move(partial, destination);
            return destination;
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }

    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        using var response = await client.SendAsync(request, token);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Tripo HTTP {(int)response.StatusCode} ({response.StatusCode})");
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        if (!json.RootElement.TryGetProperty("code", out var code) || code.GetInt32() != 0)
        { json.Dispose(); throw new InvalidOperationException("Tripo API 返回错误，请检查额度、模型版本与请求参数"); }
        return json;
    }
}
