using System.Runtime.CompilerServices;
using AMLabSlicer.Grpc;
using Grpc.Core;
using Grpc.Net.Client;

namespace AMLabSlicer.Services;

public sealed class GrpcSlicingService : ISlicingService, IDisposable
{
    private readonly GrpcChannel _channel = GrpcChannel.ForAddress("http://localhost:50051",
        new GrpcChannelOptions { MaxReceiveMessageSize = null, MaxSendMessageSize = null });
    private readonly SlicerService.SlicerServiceClient _client;

    public GrpcSlicingService() => _client = new(_channel);

    public async Task<AlgorithmList> GetAlgorithmsAsync(CancellationToken cancellationToken = default)
        => await _client.GetAvailableAlgorithmsAsync(new Empty(),
            deadline: DateTime.UtcNow.AddSeconds(15), cancellationToken: cancellationToken);

    public async Task<ParameterTemplateList> GetParametersAsync(string algorithm, CancellationToken cancellationToken = default)
        => await _client.GetAlgorithmParametersAsync(new AlgorithmRequest { AlgorithmId = algorithm },
            deadline: DateTime.UtcNow.AddSeconds(15), cancellationToken: cancellationToken);

    public async IAsyncEnumerable<SliceServerMessage> SliceAsync(SliceRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var call = _client.Slice(cancellationToken: cancellationToken);
        await call.RequestStream.WriteAsync(new SliceClientMessage { StartRequest = request }, cancellationToken);
        // No more client messages; do not wait for the server before half-closing.
        await call.RequestStream.CompleteAsync();
        await foreach (var message in call.ResponseStream.ReadAllAsync(cancellationToken))
            yield return message;
    }

    public void Dispose() => _channel.Dispose();
}
