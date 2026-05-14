using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using AMLabSlicer.Grpc;

namespace AMLabSlicer.EngineHost
{
    // =================================================================
    // 引擎注册表：管理所有已注册的后端引擎连接及其进程生命周期
    // =================================================================
    public class EngineRegistry : IDisposable
    {
        // 算法名 → gRPC 连接地址
        private readonly ConcurrentDictionary<string, string> _engines = new();
        // 算法名 → gRPC Channel（懒加载）
        private readonly ConcurrentDictionary<string, GrpcChannel> _channels = new();
        // 算法名 → 子进程（如果由 EngineHost 启动）
        private readonly ConcurrentDictionary<string, Process> _processes = new();

        /// <summary>
        /// 注册引擎（仅记录地址，不启动进程，用于外部已启动的引擎）
        /// </summary>
        public void Register(string algorithmName, string address)
        {
            _engines[algorithmName] = address;
            Console.WriteLine($"[EngineHost] 已注册算法: {algorithmName} -> {address}");
        }

        /// <summary>
        /// 注册并自动启动引擎子进程
        /// </summary>
        public void RegisterAndLaunch(string algorithmName, string address, string executablePath)
        {
            if (!File.Exists(executablePath))
            {
                Console.WriteLine($"[EngineHost] ⚠ 引擎可执行文件不存在: {executablePath}");
                Console.WriteLine($"[EngineHost]   算法 \"{algorithmName}\" 将以被动模式注册（需手动启动引擎）");
                Register(algorithmName, address);
                return;
            }

            _engines[algorithmName] = address;

            var psi = new ProcessStartInfo
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
                var process = Process.Start(psi);
                if (process != null)
                {
                    _processes[algorithmName] = process;

                    // 异步读取子进程日志
                    process.OutputDataReceived += (_, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            Console.WriteLine($"[{algorithmName}] {e.Data}");
                    };
                    process.ErrorDataReceived += (_, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            Console.WriteLine($"[{algorithmName} ERR] {e.Data}");
                    };
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    Console.WriteLine($"[EngineHost] ✓ 已启动引擎: {algorithmName} (PID {process.Id}) -> {address}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EngineHost] ✗ 启动引擎失败 ({algorithmName}): {ex.Message}");
                Console.WriteLine($"[EngineHost]   请手动启动: {executablePath}");
            }
        }

        /// <summary>
        /// 等待引擎就绪（轮询健康检查）
        /// </summary>
        public async Task WaitForEnginesReady(int timeoutMs = 10000)
        {
            Console.WriteLine("[EngineHost] 等待所有引擎就绪...");
            var sw = Stopwatch.StartNew();

            foreach (var (name, address) in _engines)
            {
                bool ready = false;
                while (sw.ElapsedMilliseconds < timeoutMs && !ready)
                {
                    try
                    {
                        var client = GetClient(name);
                        if (client != null)
                        {
                            // 尝试调用 GetAvailableAlgorithms 作为健康检查
                            var result = await client.GetAvailableAlgorithmsAsync(
                                new Empty(),
                                deadline: DateTime.UtcNow.AddSeconds(2));
                            ready = true;
                            Console.WriteLine($"[EngineHost] ✓ 引擎就绪: {name} ({sw.ElapsedMilliseconds}ms)");
                        }
                    }
                    catch
                    {
                        await Task.Delay(300);
                    }
                }

                if (!ready)
                {
                    Console.WriteLine($"[EngineHost] ⚠ 引擎超时: {name} (>{timeoutMs}ms)，将在首次请求时重试");
                }
            }

            Console.WriteLine($"[EngineHost] 引擎初始化完成 ({sw.ElapsedMilliseconds}ms)");
        }

        public List<string> GetAllAlgorithms() => new(_engines.Keys);

        public SlicerService.SlicerServiceClient? GetClient(string algorithmName)
        {
            if (!_engines.TryGetValue(algorithmName, out var address))
                return null;

            var channel = _channels.GetOrAdd(address, addr => GrpcChannel.ForAddress(addr, new GrpcChannelOptions 
            { 
                MaxReceiveMessageSize = null, 
                MaxSendMessageSize = null 
            }));
            return new SlicerService.SlicerServiceClient(channel);
        }

        public bool HasAlgorithm(string name) => _engines.ContainsKey(name);

        public void Dispose()
        {
            // 优雅关闭所有子进程
            foreach (var (name, process) in _processes)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        Console.WriteLine($"[EngineHost] 正在关闭引擎: {name} (PID {process.Id})");
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(3000);
                    }
                    process.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EngineHost] 关闭引擎异常 ({name}): {ex.Message}");
                }
            }
            _processes.Clear();

            // 关闭所有 gRPC channel
            foreach (var channel in _channels.Values)
            {
                channel.Dispose();
            }
            _channels.Clear();
        }
    }

    // =================================================================
    // EngineHost gRPC 服务实现
    // 面向前端的 Server，将请求路由到对应的后端引擎
    // =================================================================
    public class EngineHostService : SlicerService.SlicerServiceBase
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
            var client = _registry.GetClient(request.AlgorithmName);
            if (client == null)
            {
                return new ParameterTemplateList();
            }

            try
            {
                // 直接转发给对应的引擎
                return await client.GetAlgorithmParametersAsync(request);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EngineHost] 获取参数失败 ({request.AlgorithmName}): {ex.Message}");
                return new ParameterTemplateList();
            }
        }

        public override async Task Slice(
            IAsyncStreamReader<SliceClientMessage> requestStream,
            IServerStreamWriter<SliceServerMessage> responseStream,
            ServerCallContext context)
        {
            await foreach (var message in requestStream.ReadAllAsync())
            {
                if (message.MsgCase == SliceClientMessage.MsgOneofCase.StartRequest)
                {
                    var req = message.StartRequest;
                    var client = _registry.GetClient(req.AlgorithmName);

                    if (client == null)
                    {
                        await responseStream.WriteAsync(new SliceServerMessage
                        {
                            Result = new SliceResult { Success = false, Message = $"未找到算法引擎: {req.AlgorithmName}" }
                        });
                        return;
                    }

                    try
                    {
                        await responseStream.WriteAsync(new SliceServerMessage
                        {
                            Log = new LogMessage { Level = LogMessage.Types.LogLevel.Info, Text = $"正在将切片请求转发给引擎: {req.AlgorithmName}" }
                        });

                        // 开启与后端引擎的双向流
                        using var engineCall = client.Slice();

                        // 转发切片请求给引擎
                        await engineCall.RequestStream.WriteAsync(new SliceClientMessage { StartRequest = req });
                        await engineCall.RequestStream.CompleteAsync();

                        // 将引擎的响应流转发回前端
                        await foreach (var engineResponse in engineCall.ResponseStream.ReadAllAsync(context.CancellationToken))
                        {
                            await responseStream.WriteAsync(engineResponse);
                        }
                    }
                    catch (RpcException ex)
                    {
                        await responseStream.WriteAsync(new SliceServerMessage
                        {
                            Result = new SliceResult { Success = false, Message = $"引擎通信失败: {ex.Status.Detail}" }
                        });
                    }
                    catch (Exception ex)
                    {
                        await responseStream.WriteAsync(new SliceServerMessage
                        {
                            Result = new SliceResult { Success = false, Message = $"切片过程出错: {ex.Message}" }
                        });
                    }
                    return;
                }
            }
        }
    }

    // =================================================================
    // 入口
    // =================================================================
    class Program
    {
        static async Task Main(string[] args)
        {
            var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args);

            // 配置 Kestrel 强制 HTTP/2
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenLocalhost(50051, o => o.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2);
            });

            // ── 引擎注册表 ─────────────────────────────────────
            var registry = new EngineRegistry();

            // 查找 fdm_engine.exe 的路径（相对于 EngineHost 项目目录）
            var fdmEnginePath = FindFdmEngine();

            // 注册并自动启动 C++ FDM 引擎
            registry.RegisterAndLaunch("FDM 三轴切片", "http://localhost:50100", fdmEnginePath);

            // 未来：五轴引擎
            // registry.RegisterAndLaunch("五轴切片", "http://localhost:50101", "path/to/five_axis_engine.exe");
            // ─────────────────────────────────────────────────

            // 等待引擎就绪
            await registry.WaitForEnginesReady(timeoutMs: 15000);

            builder.Services.AddSingleton(registry);
            builder.Services.AddGrpc(options => 
            {
                options.MaxReceiveMessageSize = null;
                options.MaxSendMessageSize = null;
            });

            var app = builder.Build();
            app.MapGrpcService<EngineHostService>();
            app.MapGet("/", () => "AMLabSlicer EngineHost - gRPC 引擎调度服务");

            // 注册退出清理
            app.Lifetime.ApplicationStopping.Register(() =>
            {
                Console.WriteLine("[EngineHost] 正在关闭...");
                registry.Dispose();
            });

            Console.WriteLine("========================================");
            Console.WriteLine("  AMLabSlicer EngineHost");
            Console.WriteLine("  gRPC 监听: http://localhost:50051");
            Console.WriteLine($"  已注册引擎数: {registry.GetAllAlgorithms().Count}");
            foreach (var alg in registry.GetAllAlgorithms())
                Console.WriteLine($"    - {alg}");
            Console.WriteLine("========================================");

            await app.RunAsync();
        }

        /// <summary>
        /// 在常见位置查找 fdm_engine.exe
        /// </summary>
        static string FindFdmEngine()
        {
            // 优先级：
            // 1. 同级目录下的 AMLabSlicer.Engine.FDM/build/Release/fdm_engine.exe
            // 2. 绝对路径（开发环境）
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
                    Console.WriteLine($"[EngineHost] 找到 FDM 引擎: {fullPath}");
                    return fullPath;
                }
            }

            Console.WriteLine("[EngineHost] ⚠ 未找到 fdm_engine.exe，使用默认路径");
            return @"F:\2026.3\AMLabSlicer\AMLabSlicer\AMLabSlicer.Engine.FDM\build\Release\fdm_engine.exe";
        }
    }
}
