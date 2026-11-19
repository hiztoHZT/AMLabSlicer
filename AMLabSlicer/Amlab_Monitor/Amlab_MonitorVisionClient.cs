using System.IO;
using System.Net.Http;
using AMLabSlicer.Amlab_Monitor.Grpc;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;

namespace AMLabSlicer.Amlab_Monitor;

internal sealed class Amlab_MonitorVisionClient : IDisposable
{
    private const int MaxImageBytes = 12 * 1024 * 1024;
    private static readonly HttpClient CameraHttp = new();
    private static readonly Lazy<Amlab_MonitorVisionClient> DevelopmentClient = new(() => new(new Uri("http://127.0.0.1:50120")));
    private readonly GrpcChannel _channel;
    private readonly AmlabMonitorVision.AmlabMonitorVisionClient _client;

    internal static Amlab_MonitorVisionClient Standalone => DevelopmentClient.Value;

    internal Amlab_MonitorVisionClient(Uri address)
    {
        _channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions { MaxSendMessageSize = MaxImageBytes + 4096 });
        _client = new AmlabMonitorVision.AmlabMonitorVisionClient(_channel);
    }

    public void Dispose() => _channel.Dispose();

    public async Task<IReadOnlyList<VisionModel>> ListModelsAsync(CancellationToken token)
    {
        var reply = await _client.ListModelsAsync(
            new ListModelsRequest(), deadline: DateTime.UtcNow.AddSeconds(3), cancellationToken: token);
        return reply.Models.ToArray();
    }

    public async Task<DetectionResult> DetectFrameAsync(
        DetectionOptions options, byte[] image, CancellationToken token)
    {
        using var call = _client.Detect(deadline: DateTime.UtcNow.AddSeconds(60), cancellationToken: token);
        await call.RequestStream.WriteAsync(new DetectionRequest { Options = options }, token);
        var frame = new DetectionFrame { FrameId = 1, Image = ByteString.CopyFrom(image) };
        await call.RequestStream.WriteAsync(new DetectionRequest { Frame = frame }, token);
        await call.RequestStream.CompleteAsync();
        if (!await call.ResponseStream.MoveNext(token) || call.ResponseStream.Current.FrameId != frame.FrameId)
            throw new InvalidDataException("The vision service did not return the requested frame.");
        var result = call.ResponseStream.Current;
        if (await call.ResponseStream.MoveNext(token))
            throw new InvalidDataException("The vision service returned an unexpected frame.");
        return result;
    }

    public static async Task<byte[]> ReadImageAsync(string path, CancellationToken token)
    {
        await using var file = File.OpenRead(path);
        return await ReadImageBytesAsync(file, file.Length, token);
    }

    public static async Task<byte[]> ReadSnapshotAsync(Uri cameraUri, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var request = new HttpRequestMessage(HttpMethod.Get, cameraUri);
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        using var response = await CameraHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentType?.MediaType?.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase) == true)
            throw new InvalidDataException("Use the camera snapshot URL instead of an MJPEG stream URL.");
        await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
        return await ReadImageBytesAsync(body, response.Content.Headers.ContentLength, timeout.Token);
    }

    private static async Task<byte[]> ReadImageBytesAsync(Stream source, long? length, CancellationToken token)
    {
        if (length > MaxImageBytes)
            throw new InvalidDataException("The image exceeds the 12 MiB limit.");
        using var buffer = new MemoryStream();
        var block = new byte[64 * 1024];
        int count;
        while ((count = await source.ReadAsync(block.AsMemory(), token)) != 0)
        {
            if (buffer.Length + count > MaxImageBytes)
                throw new InvalidDataException("The image exceeds the 12 MiB limit.");
            buffer.Write(block, 0, count);
        }
        return buffer.ToArray();
    }
}
