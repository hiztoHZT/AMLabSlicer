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
        private readonly ConcurrentDictionary<string, string> _instances = new();
        private readonly ConcurrentDictionary<string, byte> _ready = new();
        private readonly AMLabSlicer.Processes.OwnedProcessJob _job = new();
        private readonly object _registrationGate = new();

        public void Register(AlgorithmInfo algorithm, string address)
        {
            if (string.IsNullOrWhiteSpace(algorithm.AlgorithmId))
                throw new ArgumentException("AlgorithmId is required.", nameof(algorithm));

            var uri = new Uri(address);
            if (!uri.IsLoopback || uri.Scheme != "http") throw new ArgumentException("Engine endpoint must be loopback HTTP.");
            address = "http://127.0.0.1:" + uri.Port;
            lock (_registrationGate)
            {
                if (_algorithms.ContainsKey(algorithm.AlgorithmId)) throw new ArgumentException("Duplicate algorithm ID: " + algorithm.AlgorithmId);
                if (_algorithms.Values.Any(r => new Uri(r.Address).Port == uri.Port)) throw new ArgumentException("Duplicate engine port: " + uri.Port);
                _algorithms[algorithm.AlgorithmId] = new EngineRegistration(algorithm.Clone(), address);
            }
            Console.WriteLine($"[EngineHost] Registered algorithm: {algorithm.AlgorithmId} ({algorithm.DisplayName}) -> {address}");
        }

        public void RegisterAndLaunch(AlgorithmInfo algorithm, string executablePath)
        {
            if (!File.Exists(executablePath))
            {
                throw new FileNotFoundException("Engine executable missing; refusing passive fallback to stale engines.", executablePath);
            }
            // Managed engines always receive a fresh port from the OS.
            var reservation = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            reservation.Start();
            var port = ((System.Net.IPEndPoint)reservation.LocalEndpoint).Port;
            var address = "http://127.0.0.1:" + port;
            try { Register(algorithm, address); }
            catch { reservation.Stop(); throw; }
            var instance = Guid.NewGuid().ToString("N");
            _instances[algorithm.AlgorithmId] = instance;

            var processStartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = Path.GetDirectoryName(executablePath),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            processStartInfo.Environment["AMLAB_ENGINE_PORT"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            processStartInfo.Environment["AMLAB_ENGINE_INSTANCE"] = instance;
            reservation.Stop();

            try
            {
                var process = Process.Start(processStartInfo);
                if (process == null)
                {
                    throw new InvalidOperationException($"Failed to start engine {algorithm.AlgorithmId}.");
                }

                _processes[algorithm.AlgorithmId] = process;
                _job.Add(process);
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
                throw;
            }
        }

        public async Task WaitForEnginesReady(int timeoutMs = 10000, CancellationToken cancellationToken = default)
        {
            Console.WriteLine("[EngineHost] Waiting for registered engines...");

            foreach (var registration in _algorithms.Values)
            {
                var stopwatch = Stopwatch.StartNew();
                var ready = false;
                while (stopwatch.ElapsedMilliseconds < timeoutMs && !ready)
                {
                    if (_processes.TryGetValue(registration.Algorithm.AlgorithmId, out var owned) && owned.HasExited)
                        throw new InvalidOperationException($"Engine {registration.Algorithm.AlgorithmId} exited during startup: {owned.ExitCode}");
                    try
                    {
                        var client = CreateClient(registration);
                        if (client != null)
                        {
                            using var call = client.GetAvailableAlgorithmsAsync(
                                new Empty(),
                                deadline: DateTime.UtcNow.AddSeconds(2), cancellationToken: cancellationToken);
                            var response = await call.ResponseAsync;
                            var headers = await call.ResponseHeadersAsync;
                            if (!response.Algorithms.Any(a => a.AlgorithmId == registration.Algorithm.AlgorithmId))
                                throw new InvalidDataException("Wrong engine algorithm at " + registration.Address);
                            if (_instances.TryGetValue(registration.Algorithm.AlgorithmId, out var expected) && headers.GetValue("x-amlab-instance") != expected)
                                throw new InvalidDataException("Engine instance mismatch; refusing stale/foreign service.");
                            _ready[registration.Algorithm.AlgorithmId] = 1;
                            ready = true;
                            Console.WriteLine($"[EngineHost] Engine ready: {registration.Algorithm.AlgorithmId} ({stopwatch.ElapsedMilliseconds}ms)");
                        }
                    }
                    catch when (cancellationToken.IsCancellationRequested) { return; }
                    catch (RpcException)
                    {
                        try { await Task.Delay(300, cancellationToken); }
                        catch (OperationCanceledException) { return; }
                    }
                }

                if (!ready)
                {
                    throw new TimeoutException($"Engine startup timed out: {registration.Algorithm.AlgorithmId}");
                }
            }

            Console.WriteLine("[EngineHost] All engine instances verified.");
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
            if (!_ready.ContainsKey(algorithmId)) throw new RpcException(new Status(StatusCode.Unavailable, "Engine not ready."));
            if (_processes.TryGetValue(algorithmId, out var owned) && owned.HasExited)
                throw new RpcException(new Status(StatusCode.Unavailable, "Owned engine exited."));
            return CreateClient(registration);
        }

        private SlicerService.SlicerServiceClient CreateClient(EngineRegistration registration)
        {
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
            _ready.Clear();
            _job.Dispose();

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
                        return;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            finally
            {
                await CompleteRequestStreamAsync(target);
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

        private static void Main(string[] args)
        {
            var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args);
            var port = builder.Configuration.GetValue<int?>("EngineHost:Port") ?? 50051;
            // Hold the mutex on this thread while the async server runs on worker threads.
            using var instance = new Mutex(false, "Local\\AMLabSlicer.EngineHost.Instance." + port);
            bool acquired;
            try { acquired = instance.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) { Console.WriteLine($"[EngineHost] Port {port} already has an EngineHost instance; duplicate launch skipped."); return; }
            try
            {
                if (System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
                    .Any(endpoint => endpoint.Port == port))
                {
                    Console.WriteLine($"[EngineHost] Port {port} is already in use; no engines were launched. Close the existing service before debugging a new host.");
                    return;
                }
                RunAsync(builder).GetAwaiter().GetResult();
            }
            finally { instance.ReleaseMutex(); }
        }

        private static async Task RunAsync(Microsoft.AspNetCore.Builder.WebApplicationBuilder builder)
        {

            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenLocalhost(builder.Configuration.GetValue<int?>("EngineHost:Port") ?? 50051,
                    o => o.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2);
            });

            using var registry = new EngineRegistry();
            builder.Services.AddSingleton(registry);
            builder.Services.AddGrpc(options =>
            {
                options.MaxReceiveMessageSize = null;
                options.MaxSendMessageSize = null;
            });

            await using var app = builder.Build();
            app.MapGrpcService<EngineHostService>();
            var buildId = AMLabSlicer.Processes.HostBuildIdentity.Compute(AppContext.BaseDirectory);
            var hostInstance = Guid.NewGuid().ToString("N");
            var enginesReady = false;
            app.MapGet("/", (Microsoft.AspNetCore.Http.HttpContext context) => {
                context.Response.Headers["x-amlab-build"] = buildId;
                context.Response.Headers["x-amlab-host-instance"] = hostInstance;
                if (!Volatile.Read(ref enginesReady)) context.Response.StatusCode = 503;
                return "AMLabSlicer EngineHost - gRPC engine router";
            });
            app.MapEngineHostShutdown(hostInstance);

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

            // Listen before engine warm-up so a closing UI can stop a starting host too.
            await app.StartAsync();
            registry.RegisterAndLaunch(CreateFdmAlgorithmInfo(),
                builder.Configuration["EngineHost:EngineExecutable"] ?? Environment.GetEnvironmentVariable("AMLAB_FDM_ENGINE") ?? FindFdmEngine());
            var atom = new AlgorithmInfo { AlgorithmId = "atom.nonplanar.v1", DisplayName = "AtomSlicer 非平面切片",
                EngineId = "engine.atom", Category = "Non-planar", Version = "3ee70a8" };
            atom.Capabilities.Add("slice.mesh"); atom.InputKinds.Add("mesh.triangle.float32");
            atom.OutputKinds.AddRange(new[] { "toolpath.ply", "gcode" });
            registry.RegisterAndLaunch(atom,
                builder.Configuration["EngineHost:AtomExecutable"] ?? Path.Combine(AppContext.BaseDirectory,
                    "engines", "atom", "AMLabSlicer.Engine.Atom.exe"));


            await registry.WaitForEnginesReady(timeoutMs: 15000, cancellationToken: app.Lifetime.ApplicationStopping);
            Volatile.Write(ref enginesReady, true);
            await app.WaitForShutdownAsync();
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
                Path.Combine(AppContext.BaseDirectory, "engines", "fdm", "fdm_engine.exe"),
                Path.Combine(AppContext.BaseDirectory, "fdm_engine.exe"),
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
