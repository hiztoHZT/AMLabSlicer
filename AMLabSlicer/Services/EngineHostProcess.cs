using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
namespace AMLabSlicer.Services;

public sealed class EngineHostProcess : IDisposable
{
    private readonly string _directory;
    private readonly Uri _address;
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromMilliseconds(600) };
    private Process? _owned;
    private readonly object _processGate = new();
    private bool _disposed;
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
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(20)); }
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
            return response.IsSuccessStatusCode && (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false))
                .StartsWith("AMLabSlicer EngineHost -", StringComparison.Ordinal);
        }
        catch (HttpRequestException) { return false; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
    }
    private async Task EnsureStartedCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (await IsReadyAsync(cancellationToken).ConfigureAwait(false)) return;
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
            _owned = Process.Start(start) ?? throw new InvalidOperationException("无法启动 EngineHost");
            if (start.RedirectStandardOutput)
            {
                _owned.OutputDataReceived += (_, e) => { if (e.Data is not null) Trace.TraceInformation("EngineHost: {0}", e.Data); };
                _owned.ErrorDataReceived += (_, e) => { if (e.Data is not null) Trace.TraceWarning("EngineHost: {0}", e.Data); };
                _owned.BeginOutputReadLine(); _owned.BeginErrorReadLine();
            }
        }
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(15))
        {
            if (await IsReadyAsync(cancellationToken).ConfigureAwait(false)) return;
            lock (_processGate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_owned.HasExited) throw new InvalidOperationException($"EngineHost 启动失败，退出码 {_owned.ExitCode}；请检查端口或运行时依赖。");
            }
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException("EngineHost 启动后没有响应，请检查 localhost 端口 " + _address.Port);
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
                // UI already requested graceful HTTP shutdown. Only fall back for our own process.
                if (!_owned.HasExited && !_owned.WaitForExit(2000))
                { _owned.Kill(entireProcessTree: true); _owned.WaitForExit(2000); }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { Trace.TraceWarning("EngineHost cleanup: {0}", ex.Message); }
            finally { _owned.Dispose(); _owned = null; }
        }
        _client.Dispose();
        }
    }
}
