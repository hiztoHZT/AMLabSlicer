using AMLabSlicer.Grpc;
using Grpc.Core;

namespace AMLabSlicer.Engine.Atom;

public sealed class AtomService(AtomRunner runner, IHostApplicationLifetime lifetime) : SlicerService.SlicerServiceBase
{
    public override async Task<AlgorithmList> GetAvailableAlgorithms(Empty request, ServerCallContext context)
    {
        var instance = Environment.GetEnvironmentVariable("AMLAB_ENGINE_INSTANCE");
        if (!string.IsNullOrEmpty(instance)) await context.WriteResponseHeadersAsync(new Metadata { { "x-amlab-instance", instance } });
        var result = new AlgorithmList(); result.Algorithms.Add(AtomParameters.Algorithm()); return result;
    }
    public override Task<ParameterTemplateList> GetAlgorithmParameters(AlgorithmRequest request, ServerCallContext context) =>
        Task.FromResult(request.AlgorithmId == AtomParameters.AlgorithmId ? AtomParameters.Templates() : new ParameterTemplateList());

    public override async Task Slice(IAsyncStreamReader<SliceClientMessage> input, IServerStreamWriter<SliceServerMessage> output, ServerCallContext context)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, lifetime.ApplicationStopping);
        if (!await input.MoveNext(cancellation.Token) || input.Current.StartRequest is not { } request)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "首条消息必须是切片请求。"));
        var monitor = MonitorCancellation();
        try
        {
            SliceResult result;
            try
            {
                result = await runner.RunAsync(request, line => output.WriteAsync(new SliceServerMessage
                { Log = new LogMessage { Level = LogMessage.Types.LogLevel.Info, Text = line } }), cancellation.Token,
                    progress => output.WriteAsync(new SliceServerMessage { Progress=progress }));
            }
            catch (Exception ex)
            {
                var code = ex is TimeoutException ? "ATOM_TIMEOUT" : ex is OperationCanceledException ? "ATOM_CANCELLED" : "ATOM_ERROR";
                result = new SliceResult { Success = false, Message = ex.Message,
                    Error = new ErrorInfo { Code = code, Message = ex.Message, Recoverable = true } };
            }
            if (!context.CancellationToken.IsCancellationRequested)
                await output.WriteAsync(new SliceServerMessage { Result = result });
        }
        finally { cancellation.Cancel(); await monitor; }

        async Task MonitorCancellation()
        {
            try
            {
                while (await input.MoveNext(cancellation.Token))
                    if (input.Current.CancelRequest) { cancellation.Cancel(); return; }
            }
            catch (Exception) when (cancellation.IsCancellationRequested) { }
            catch (RpcException) { cancellation.Cancel(); }
        }
    }
}
