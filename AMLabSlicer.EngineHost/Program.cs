using System.Collections.Concurrent;
using System.Diagnostics;
using AMLabSlicer.Grpc;
using Grpc.Core;
using Grpc.Net.Client;

namespace AMLabSlicer.EngineHost
{
    public sealed class EngineRegistry : IDisposable
    {
        private readonly ConcurrentDictionary<string, EngineRegistration> _algorithms = new();
        private readonly ConcurrentDictionary<string, GrpcChannel> _channels = new();
        private readonly ConcurrentDictionary<string, Process> _processes = new();

        public void Register(AlgorithmInfo algorithm, string address)
        {
            if (string.IsNullOrWhiteSpace(algorithm.AlgorithmId))
                throw new ArgumentException("AlgorithmId is required.", nameof(algorithm));

            _algorithms[algorithm.AlgorithmId] = new EngineRegistration(algorithm.Clone(), address);
            Console.WriteLine($"[EngineHost] Registered algorithm: {algorithm.AlgorithmId} ({algorithm.DisplayName}) -> {address}");
        }

        public void RegisterAndLaunch(AlgorithmInfo algorithm, string address, string executablePath)
        {
            Register(algorithm, address);

            if (!File.Exists(executablePath))
            {
                Console.WriteLine($"[EngineHost] Engine executable not found: {executablePath}");
                Console.WriteLine($"[EngineHost] Algorithm {algorithm.AlgorithmId} registered in passive mode.");
                return;
            }

            var processStartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = Path.GetDirectoryName(executablePath),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            try
            {
                var process = Process.Start(processStartInfo);
                if (process == null)
                {
                    Console.WriteLine($"[EngineHost] Failed to start engine process for {algorithm.AlgorithmId}.");
                    return;
                }

                _processes[algorithm.AlgorithmId] = process;
                process.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Console.WriteLine($"[{algorithm.AlgorithmId}] {e.Data}");
                };
                process.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Console.WriteLine($"[{algorithm.AlgorithmId} ERR] {e.Data}");
                };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                Console.WriteLine($"[EngineHost] Started engine: {algorithm.AlgorithmId} (PID {process.Id}) -> {address}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EngineHost] Failed to start engine {algorithm.AlgorithmId}: {ex.Message}");
                Console.WriteLine($"[EngineHost] Start manually if needed: {executablePath}");
            }
        }

        public async Task WaitForEnginesReady(int timeoutMs = 10000)
        {
            Console.WriteLine("[EngineHost] Waiting for registered engines...");
            var stopwatch = Stopwatch.StartNew();

            foreach (var registration in _algorithms.Values)
            {
                var ready = false;
                while (stopwatch.ElapsedMilliseconds < timeoutMs && !ready)
                {
                    try
                    {
                        var client = GetClient(registration.Algorithm.AlgorithmId);
                        if (client != null)
                        {
                            await client.GetAvailableAlgorithmsAsync(
                                new Empty(),
                                deadline: DateTime.UtcNow.AddSeconds(2));
                            ready = true;
                            Console.WriteLine($"[EngineHost] Engine ready: {registration.Algorithm.AlgorithmId} ({stopwatch.ElapsedMilliseconds}ms)");
                        }
                    }
                    catch
                    {
                        await Task.Delay(300);
                    }
                }

                if (!ready)
                {
                    Console.WriteLine($"[EngineHost] Engine timeout: {registration.Algorithm.AlgorithmId} (>{timeoutMs}ms). Requests may retry later.");
                }
            }

            Console.WriteLine($"[EngineHost] Engine initialization finished ({stopwatch.ElapsedMilliseconds}ms)");
        }

        public IReadOnlyList<AlgorithmInfo> GetAllAlgorithms()
        {
            return _algorithms.Values
                .OrderBy(registration => registration.Algorithm.DisplayName)
                .Select(registration => registration.Algorithm.Clone())
                .ToList();
        }

        public SlicerService.SlicerServiceClient? GetClient(string algorithmId)
        {
            if (!_algorithms.TryGetValue(algorithmId, out var registration))
                return null;

            var channel = _channels.GetOrAdd(registration.Address, address => GrpcChannel.ForAddress(address, new GrpcChannelOptions
            {
                MaxReceiveMessageSize = null,
                MaxSendMessageSize = null
            }));
            return new SlicerService.SlicerServiceClient(channel);
        }

        public bool HasAlgorithm(string algorithmId) => _algorithms.ContainsKey(algorithmId);

        public void Dispose()
        {
            foreach (var (algorithmId, process) in _processes)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        Console.WriteLine($"[EngineHost] Stopping engine: {algorithmId} (PID {process.Id})");
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(3000);
                    }
                    process.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EngineHost] Error while stopping engine {algorithmId}: {ex.Message}");
                }
            }
            _processes.Clear();

            foreach (var channel in _channels.Values)
            {
                channel.Dispose();
            }
            _channels.Clear();
        }

        private sealed record EngineRegistration(AlgorithmInfo Algorithm, string Address);
    }

    public sealed class EngineHostService : SlicerService.SlicerServiceBase
    {
        private readonly EngineRegistry _registry;

        public EngineHostService(EngineRegistry registry)
        {
            _registry = registry;
        }

        public override Task<AlgorithmList> GetAvailableAlgorithms(Empty request, ServerCallContext context)
        {
            var list = new AlgorithmList();
            list.Algorithms.AddRange(_registry.GetAllAlgorithms());
            return Task.FromResult(list);
        }

        public override async Task<ParameterTemplateList> GetAlgorithmParameters(AlgorithmRequest request, ServerCallContext context)
        {
            var client = _registry.GetClient(request.AlgorithmId);
            if (client == null)
            {
                return new ParameterTemplateList();
            }

            try
            {
                return await client.GetAlgorithmParametersAsync(request, cancellationToken: context.CancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EngineHost] Failed to get parameters for {request.AlgorithmId}: {ex.Message}");
                return new ParameterTemplateList();
            }
        }

        public override async Task Slice(
            IAsyncStreamReader<SliceClientMessage> requestStream,
            IServerStreamWriter<SliceServerMessage> responseStream,
            ServerCallContext context)
        {
            await foreach (var message in requestStream.ReadAllAsync(context.CancellationToken))
            {
                if (message.MsgCase != SliceClientMessage.MsgOneofCase.StartRequest)
                    continue;

                var request = message.StartRequest;
                var client = _registry.GetClient(request.AlgorithmId);

                if (client == null)
                {
                    await responseStream.WriteAsync(Failure($"No engine registered for algorithm: {request.AlgorithmId}"), context.CancellationToken);
                    return;
                }

                try
                {
                    await responseStream.WriteAsync(new SliceServerMessage
                    {
                        Log = new LogMessage
                        {
                            Level = LogMessage.Types.LogLevel.Info,
                            Text = $"Forwarding slice request to {request.AlgorithmId}"
                        }
                    }, context.CancellationToken);

                    using var engineCall = client.Slice(cancellationToken: context.CancellationToken);
                    await engineCall.RequestStream.WriteAsync(new SliceClientMessage { StartRequest = request }, context.CancellationToken);

                    var requestPump = ForwardClientMessagesAsync(requestStream, engineCall.RequestStream, context.CancellationToken);
                    await foreach (var engineResponse in engineCall.ResponseStream.ReadAllAsync(context.CancellationToken))
                    {
                        await responseStream.WriteAsync(engineResponse, context.CancellationToken);
                    }

                    await CompleteRequestStreamAsync(engineCall.RequestStream);
                    await requestPump;
                }
                catch (RpcException ex)
                {
                    await responseStream.WriteAsync(Failure($"Engine communication failed: {ex.Status.Detail}"), CancellationToken.None);
                }
                catch (Exception ex)
                {
                    await responseStream.WriteAsync(Failure($"Slice failed: {ex.Message}"), CancellationToken.None);
                }
                return;
            }
        }

        private static async Task ForwardClientMessagesAsync(
            IAsyncStreamReader<SliceClientMessage> source,
            IClientStreamWriter<SliceClientMessage> target,
            CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var message in source.ReadAllAsync(cancellationToken))
                {
                    if (message.MsgCase == SliceClientMessage.MsgOneofCase.CancelRequest)
                    {
                        await target.WriteAsync(new SliceClientMessage { CancelRequest = true }, cancellationToken);
                        await CompleteRequestStreamAsync(target);
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static async Task CompleteRequestStreamAsync(IClientStreamWriter<SliceClientMessage> stream)
        {
            try
            {
                await stream.CompleteAsync();
            }
            catch
            {
            }
        }

        private static SliceServerMessage Failure(string message)
        {
            return new SliceServerMessage
            {
                Result = new SliceResult
                {
                    Success = false,
                    Message = message,
                    Error = new ErrorInfo
                    {
                        Code = "ENGINE_HOST_ERROR",
                        Message = message,
                        Recoverable = true
                    }
                }
            };
        }
    }

    internal static class Program
    {
        private const string FdmAlgorithmId = "fdm.cartesian.v1";

        private static async Task Main(string[] args)
        {
            var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args);

            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenLocalhost(50051, o => o.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2);
            });

            var registry = new EngineRegistry();
            registry.RegisterAndLaunch(CreateFdmAlgorithmInfo(), "http://localhost:50100", FindFdmEngine());

            await registry.WaitForEnginesReady(timeoutMs: 15000);

            builder.Services.AddSingleton(registry);
            builder.Services.AddGrpc(options =>
            {
                options.MaxReceiveMessageSize = null;
                options.MaxSendMessageSize = null;
            });

            var app = builder.Build();
            app.MapGrpcService<EngineHostService>();
            app.MapGet("/", () => "AMLabSlicer EngineHost - gRPC engine router");

            app.Lifetime.ApplicationStopping.Register(() =>
            {
                Console.WriteLine("[EngineHost] Shutting down...");
                registry.Dispose();
            });

            Console.WriteLine("========================================");
            Console.WriteLine("  AMLabSlicer EngineHost");
            Console.WriteLine("  gRPC: http://localhost:50051");
            Console.WriteLine($"  Registered algorithms: {registry.GetAllAlgorithms().Count}");
            foreach (var algorithm in registry.GetAllAlgorithms())
                Console.WriteLine($"    - {algorithm.AlgorithmId}: {algorithm.DisplayName}");
            Console.WriteLine("========================================");

            await app.RunAsync();
        }

        private static AlgorithmInfo CreateFdmAlgorithmInfo()
        {
            var algorithm = new AlgorithmInfo
            {
                AlgorithmId = FdmAlgorithmId,
                DisplayName = "FDM Cartesian Slicing",
                EngineId = "engine.fdm.cpp",
                Category = "FDM",
                Version = "1.0.0"
            };
            algorithm.Capabilities.Add("slice.mesh");
            algorithm.Capabilities.Add("output.gcode");
            algorithm.InputKinds.Add("mesh.triangle.float32");
            algorithm.OutputKinds.Add("gcode");
            return algorithm;
        }

        private static string FindFdmEngine()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "AMLabSlicer.Engine.FDM", "build", "Release", "fdm_engine.exe"),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "AMLabSlicer.Engine.FDM", "build", "Release", "fdm_engine.exe")),
                @"F:\2026.3\AMLabSlicer\AMLabSlicer\AMLabSlicer.Engine.FDM\build\Release\fdm_engine.exe",
            };

            foreach (var path in candidates)
            {
                var fullPath = Path.GetFullPath(path);
                if (File.Exists(fullPath))
                {
                    Console.WriteLine($"[EngineHost] Found FDM engine: {fullPath}");
                    return fullPath;
                }
            }

            Console.WriteLine("[EngineHost] FDM engine not found. Falling back to default path.");
            return @"F:\2026.3\AMLabSlicer\AMLabSlicer\AMLabSlicer.Engine.FDM\build\Release\fdm_engine.exe";
        }
    }
}
