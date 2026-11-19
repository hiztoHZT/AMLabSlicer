using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
namespace AMLabSlicer.Services;

public sealed class EngineHostProcess : IDisposable
{
    private readonly string _directory;
    private Uri _address;
    public Uri Address => _address;
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromMilliseconds(600) };
    private Process? _owned;
    private AMLabSlicer.Processes.OwnedProcessJob? _job;
    private readonly object _processGate = new();
    private bool _disposed;
    private bool _existingHostSeen;
    private bool _differentBuildSeen;
    private string? _expectedBuild;
    private string? _connectedInstance;
    private string? _attachedInstance;
    public bool ShowConsole { get; set; }
    internal int? OwnedProcessId => _owned?.Id;
    public EngineHostProcess(string? directory = null, Uri? address = null)
    {
        _directory = directory ?? Path.Combine(AppContext.BaseDirectory, "enginehost");
        _address = address ?? new Uri("http://localhost:50051/");
    }
    public Task EnsureStartedAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        // Keep the named mutex on one worker thread while awaiting startup internally.
        using var mutex = new Mutex(false, "Local\\AMLabSlicer.EngineHost.Start." + _address.Port);
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(45)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new TimeoutException("等待 EngineHost 启动锁超时");
            EnsureStartedCoreAsync(cancellationToken).GetAwaiter().GetResult();
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }, cancellationToken);

    private async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _address)
            { Version = HttpVersion.Version20, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var isHost = (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false))
                .StartsWith("AMLabSlicer EngineHost -", StringComparison.Ordinal);
            if (!isHost) return false;
            _existingHostSeen = true;
            var expected = _expectedBuild ??= AMLabSlicer.Processes.HostBuildIdentity.Compute(_directory);
            if (!response.Headers.TryGetValues("x-amlab-build", out var builds) || builds.SingleOrDefault() != expected)
            {
                _differentBuildSeen = true;
                return false;
            }
            _connectedInstance = response.Headers.TryGetValues("x-amlab-host-instance", out var instances) ? instances.SingleOrDefault() : null;
            if (!string.IsNullOrEmpty(_connectedInstance) && _attachedInstance != _connectedInstance)
            {
                using var ui = Process.GetCurrentProcess();
                using var attach = new HttpRequestMessage(HttpMethod.Post, new Uri(_address, "owner"))
                { Version = HttpVersion.Version20, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
                attach.Headers.Add("x-amlab-host-instance", _connectedInstance);
                attach.Headers.Add("x-amlab-ui-pid", ui.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                attach.Headers.Add("x-amlab-ui-start", ui.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
                using var attached = await _client.SendAsync(attach, cancellationToken).ConfigureAwait(false);
                attached.EnsureSuccessStatusCode();
                _attachedInstance = _connectedInstance;
            }
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
    }
    private async Task EnsureStartedCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _existingHostSeen = false;
        _differentBuildSeen = false;
        if (await IsReadyAsync(cancellationToken).ConfigureAwait(false)) return;
        if (_differentBuildSeen || (!_existingHostSeen && System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpListeners().Any(p => p.Port == _address.Port)))
        {
            var reservation = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            reservation.Start();
            try { _address = new Uri("http://localhost:" + ((IPEndPoint)reservation.LocalEndpoint).Port + "/"); }
            finally { reservation.Stop(); }
            _existingHostSeen = false;
            _differentBuildSeen = false;
            Trace.TraceInformation("Existing Host is a different build or port is occupied; starting owned Host at {0}", _address);
        }
        if (_existingHostSeen)
        {
            var wait = Stopwatch.StartNew();
            while (wait.Elapsed < TimeSpan.FromSeconds(35))
            {
                if (await IsReadyAsync(cancellationToken).ConfigureAwait(false)) return;
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
            throw new TimeoutException("已有 EngineHost 正在运行，但引擎未就绪。请查看 Host 启动日志。");
        }
        var executable = Path.Combine(_directory, "AMLabSlicer.EngineHost.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("缺少 EngineHost 运行文件，请重新生成或完整复制程序目录。", executable);
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = _directory, UseShellExecute = ShowConsole, CreateNoWindow = !ShowConsole,
            RedirectStandardOutput = !ShowConsole, RedirectStandardError = !ShowConsole
        };
        start.ArgumentList.Add("--EngineHost:Port=" + _address.Port);
        lock (_processGate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            _job ??= new AMLabSlicer.Processes.OwnedProcessJob();
            _owned = Process.Start(start) ?? throw new InvalidOperationException("无法启动 EngineHost");
            try { _job.Add(_owned); }
            catch (System.ComponentModel.Win32Exception) when (_owned.HasExited && _owned.ExitCode == 0)
            {
                // A debugger-started Host may win the instance lock between probe and launch.
                // The short-lived duplicate owns nothing; wait for the winner below.
            }
            if (start.RedirectStandardOutput)
            {
                _owned.OutputDataReceived += (_, e) => { if (e.Data is not null) Trace.TraceInformation("EngineHost: {0}", e.Data); };
                _owned.ErrorDataReceived += (_, e) => { if (e.Data is not null) Trace.TraceWarning("EngineHost: {0}", e.Data); };
                _owned.BeginOutputReadLine(); _owned.BeginErrorReadLine();
            }
        }
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(35))
        {
            if (await IsReadyAsync(cancellationToken).ConfigureAwait(false)) return;
            lock (_processGate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_owned is { HasExited: true })
                {
                    if (_owned.ExitCode != 0) throw new InvalidOperationException($"EngineHost 启动失败，退出码 {_owned.ExitCode}；请检查端口或运行时依赖。");
                    _owned.Dispose(); _owned = null;
                }
            }
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException("EngineHost 启动后没有响应，请检查 localhost 端口 " + _address.Port);
    }
    public async Task StopConnectedHostAsync()
    {
        // Stop precisely the service this window connected to, including a reused debug Host.
        // Never send a blind shutdown to the default port or a replacement process.
        if (string.IsNullOrEmpty(_connectedInstance)) return;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_address, "shutdown"))
            { Version = HttpVersion.Version20, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
            request.Headers.Add("x-amlab-host-instance", _connectedInstance);
            using var response = await _client.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) Trace.TraceWarning("Host shutdown rejected: {0}", response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        { Trace.TraceWarning("Host shutdown: {0}", ex.Message); }
        finally { _connectedInstance = null; }
    }
    public void Dispose()
    {
        lock (_processGate)
        {
        if (_disposed) return;
        _disposed = true;
        if (_owned is not null)
        {
            try
            {
                // Only terminate the process this UI launched; reused Hosts belong to another owner.
                if (!_owned.HasExited && !_owned.WaitForExit(2000))
                { _owned.Kill(entireProcessTree: true); _owned.WaitForExit(2000); }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { Trace.TraceWarning("EngineHost cleanup: {0}", ex.Message); }
            finally { _owned.Dispose(); _owned = null; }
        }
        _job?.Dispose();
        _client.Dispose();
        }
    }
}
