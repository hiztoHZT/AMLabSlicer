using System.ComponentModel;
using System.Diagnostics;
using System.Windows;

namespace AMLabSlicer.Services;

/// <summary>Keeps the dispatcher alive during cleanup; bounds exit even for uncooperative plugins.</summary>
public sealed class ApplicationShutdownController
{
    private readonly Application _application;
    private readonly Func<Task> _cleanup;
    private readonly TimeSpan _exitDeadline;
    private int _requested;
    private int _guardStarted;
    public bool IsRequested => Volatile.Read(ref _requested) != 0;

    public ApplicationShutdownController(Application application, Func<Task> cleanup, TimeSpan? exitDeadline = null)
    {
        _application = application;
        _cleanup = cleanup;
        _exitDeadline = exitDeadline ?? TimeSpan.FromSeconds(12);
    }

    public void Attach(Window window) => window.Closing += OnClosing;
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        RequestExit();
    }
    private bool _allowClose;

    public async void RequestExit(int exitCode = 0)
    {
        if (Interlocked.Exchange(ref _requested, 1) != 0) return;
        ArmExitGuard();
        if (_application.MainWindow is { } window) window.IsEnabled = false;
        try { await _cleanup(); }
        catch (Exception error) { Trace.TraceWarning("Application cleanup: {0}", error); }
        finally
        {
            _allowClose = true;
            _application.Shutdown(exitCode);
        }
    }

    // Also called by OnExit for direct Application.Shutdown / OS shutdown paths.
    // Do not disarm after cleanup: a rogue foreground thread can survive WPF shutdown.
    public void ArmExitGuard()
    {
        if (Interlocked.Exchange(ref _guardStarted, 1) != 0) return;
        new Thread(() =>
        {
            Thread.Sleep(_exitDeadline);
            // Kill only this UI process. Owned child processes are reclaimed by Job Objects.
            // Environment.Exit can itself wait on blocked shutdown/finalizer handlers.
            Process.GetCurrentProcess().Kill();
        }) { IsBackground = true, Name = "AMLab exit deadline" }.Start();
    }

    public static async Task RunStageAsync(string name, Func<Task> action, TimeSpan timeout)
    {
        try { await action().WaitAsync(timeout); }
        catch (Exception error) { Trace.TraceWarning("Shutdown {0}: {1}", name, error); }
    }
}
