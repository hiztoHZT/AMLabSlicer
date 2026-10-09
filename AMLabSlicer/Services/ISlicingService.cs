using AMLabSlicer.Grpc;

namespace AMLabSlicer.Services;

public interface ISlicingService
{
    Task<AlgorithmList> GetAlgorithmsAsync(CancellationToken cancellationToken = default);
    Task<ParameterTemplateList> GetParametersAsync(string algorithm, CancellationToken cancellationToken = default);
    IAsyncEnumerable<SliceServerMessage> SliceAsync(SliceRequest request, CancellationToken cancellationToken = default);
}
