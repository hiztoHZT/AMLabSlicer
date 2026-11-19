using System.Diagnostics;
using System.Globalization;
using System.IO;
using AMLabSlicer.Processes;

namespace AMLabSlicer.Amlab_Monitor;

public sealed class Yolo11EngineProcess : IDisposable
{
    private const string ReadyPrefix = "AMLAB_MONITOR_READY=";
    private readonly SemaphoreSlim _gate = new(1);
    private readonly CancellationTokenSource _shutdown = new();
    private OwnedProcessJob? _job;
    private Process? _process;
    private Amlab_MonitorVisionClient? _client;

    internal Amlab_MonitorVisionClient Client =>
        _client ?? throw new InvalidOperationException("YOLO11 vision engine has not started.");

    public async Task EnsureStartedAsync(CancellationToken cancellationToken = default)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        await _gate.WaitAsync(stop.Token).ConfigureAwait(false);
        try
        {
            if (_process is { HasExited: false } && _client is not null)
                return;
            ReleaseProcess();

            var directory = Path.Combine(AppContext.BaseDirectory, "engine.yolo11");
            var python = Path.Combine(directory, "python", "python.exe");
            var script = Path.Combine(directory, "server.py");
            if (!File.Exists(python) || !File.Exists(script))
                throw new FileNotFoundException("engine.yolo11 package is missing. Run AMLabSlicer.Engine.Yolo11/prepare.ps1 and rebuild.");

            var start = new ProcessStartInfo(python)
            {
                WorkingDirectory = directory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in new[] { "-B", "-u", script, "--port", "0", "--device", "cpu", "--preload" })
                start.ArgumentList.Add(argument);

            var ready = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            string? lastError = null;
            var process = new Process { StartInfo = start, EnableRaisingEvents = true };
            _process = process;
            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data is { } line && line.StartsWith(ReadyPrefix, StringComparison.Ordinal) &&
                    int.TryParse(line.AsSpan(ReadyPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var port) &&
                    port is > 0 and <= 65535)
                    ready.TrySetResult(port);
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                    Volatile.Write(ref lastError, args.Data);
            };
            process.Exited += (_, _) => ready.TrySetResult(0);

            try
            {
                _job = new OwnedProcessJob();
                if (!process.Start())
                    throw new InvalidOperationException("Could not start the bundled Python executable.");
                _job.Add(process);
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var port = await ready.Task.WaitAsync(TimeSpan.FromSeconds(45), stop.Token).ConfigureAwait(false);
                if (port == 0 || process.HasExited)
                {
                    await process.WaitForExitAsync(stop.Token).ConfigureAwait(false);
                    throw new InvalidOperationException($"YOLO11 engine exited with code {process.ExitCode}. {Volatile.Read(ref lastError)}");
                }

                _client = new Amlab_MonitorVisionClient(new Uri($"http://127.0.0.1:{port}"));
            }
            catch
            {
                ReleaseProcess();
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    private void ReleaseProcess()
    {
        _client?.Dispose();
        _client = null;
        _job?.Dispose();
        _job = null;
        _process?.Dispose();
        _process = null;
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _gate.Wait();
        try { ReleaseProcess(); }
        finally { _gate.Release(); }
    }
}
