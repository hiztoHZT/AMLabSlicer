using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using AMLabSlicer.EngineHost;
using AMLabSlicer.Services;
using AMLabSlicer.Grpc;

if (Environment.GetEnvironmentVariable("AMLAB_SHUTDOWN_TEST_CHILD") is { Length: > 0 } pidFile)
{
    File.WriteAllText(pidFile, Environment.ProcessId.ToString());
    await Task.Delay(Timeout.Infinite);
    return;
}

var fixture = Path.Combine(Path.GetTempPath(), "amlab-shutdown-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
var childPidFile = Path.Combine(fixture, "child.pid");
using var registry = new EngineRegistry();
Process? child = null;
try
{
    Environment.SetEnvironmentVariable("AMLAB_SHUTDOWN_TEST_CHILD", childPidFile);
    try
    {
        registry.RegisterAndLaunch(new AlgorithmInfo { AlgorithmId = "test.shutdown", DisplayName = "Shutdown test child" },
            "http://127.0.0.1:1", Path.Combine(AppContext.BaseDirectory, "AMLabSlicer.EngineHost.Regression.exe"));
    }
    finally { Environment.SetEnvironmentVariable("AMLAB_SHUTDOWN_TEST_CHILD", null); }
    using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        while (!File.Exists(childPidFile)) await Task.Delay(20, timeout.Token);
    child = Process.GetProcessById(int.Parse(File.ReadAllText(childPidFile)));
    Check(!child.HasExited, "test engine child is running");

    var builder = WebApplication.CreateBuilder();
    builder.Logging.ClearProviders();
    builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, port => port.Protocols = HttpProtocols.Http2));
    await using var app = builder.Build();
    app.MapEngineHostShutdown();
    app.Lifetime.ApplicationStopping.Register(registry.Dispose);
    await app.StartAsync();
    var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    var endpoint = new Uri(address + "/shutdown");
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    try
    {
        using var response = await client.PostAsync(endpoint, null);
        Check(!response.IsSuccessStatusCode, "default HTTP/1.1 cannot shut down the HTTP/2 listener");
    }
    catch (HttpRequestException) { Check(true, "HTTP/1.1 is rejected by the HTTP/2 listener"); }
    Check(!app.Lifetime.ApplicationStopping.IsCancellationRequested, "failed protocol request leaves host running");

    var rejected = false;
    try { await EngineHostShutdown.RequestAsync(client, new Uri(address + "/missing-shutdown")); }
    catch (HttpRequestException) { rejected = true; }
    Check(rejected, "UI shutdown helper reports non-success status instead of silently ignoring it");

    var readiness = registry.WaitForEnginesReady(15000, app.Lifetime.ApplicationStopping);
    await EngineHostShutdown.RequestAsync(client, endpoint);
    await app.WaitForShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
    await readiness.WaitAsync(TimeSpan.FromSeconds(3));
    await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
    Check(app.Lifetime.ApplicationStopped.IsCancellationRequested, "UI HTTP/2 shutdown request gracefully stops EngineHost");
    Check(child.HasExited, "EngineHost shutdown removes its owned engine child");
    Check(readiness.IsCompleted, "shutdown cancels engine warm-up instead of waiting 15 seconds");
    var reservation = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
    reservation.Start(); var freePort = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
    var runtimeAddress = new Uri("http://localhost:" + freePort + "/");
    using var firstLauncher = new EngineHostProcess(AppContext.BaseDirectory, runtimeAddress);
    using var secondLauncher = new EngineHostProcess(AppContext.BaseDirectory, runtimeAddress);
    Environment.SetEnvironmentVariable("AMLAB_FDM_ENGINE", Path.Combine(fixture, "no-native-engine.exe"));
    try { await Task.WhenAll(firstLauncher.EnsureStartedAsync(), secondLauncher.EnsureStartedAsync()); }
    finally { Environment.SetEnvironmentVariable("AMLAB_FDM_ENGINE", null); }
    var pids = new[] { firstLauncher.OwnedProcessId, secondLauncher.OwnedProcessId }.OfType<int>().ToArray();
    Check(pids.Length == 1, "concurrent UI launchers start one Host and reuse it");
    using var launchedHost = Process.GetProcessById(pids.Single());
    await EngineHostShutdown.RequestAsync(client, new Uri(runtimeAddress, "shutdown"));
    await launchedHost.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
    Check(launchedHost.HasExited && launchedHost.ExitCode == 0, "automatically launched standalone Host exits cleanly after UI shutdown request");
    using var consoleLauncher = new EngineHostProcess(AppContext.BaseDirectory, runtimeAddress) { ShowConsole = true };
    Environment.SetEnvironmentVariable("AMLAB_FDM_ENGINE", Path.Combine(fixture, "no-native-engine.exe"));
    try { await consoleLauncher.EnsureStartedAsync(); }
    finally { Environment.SetEnvironmentVariable("AMLAB_FDM_ENGINE", null); }
    using var consoleHost = Process.GetProcessById(consoleLauncher.OwnedProcessId!.Value);
    await EngineHostShutdown.RequestAsync(client, new Uri(runtimeAddress, "shutdown"));
    await consoleHost.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
    Check(consoleHost.ExitCode == 0, "console-enabled Host starts and exits through the same lifecycle");
    Console.WriteLine("All EngineHost shutdown checks passed.");
}
finally { registry.Dispose(); child?.Dispose(); }

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    Console.WriteLine("PASS " + message);
}
