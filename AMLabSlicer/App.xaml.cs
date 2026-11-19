using AMLabSlicer.Services;
using AMLabSlicer.Amlab_Monitor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;
using AMLabSlicer.Views;
using AMLabSlicer.ViewModel;
using Microsoft.Extensions.Logging;
using AMLabSlicer.Core.Parameters;

namespace AMLabSlicer
{
    public partial class App : Application
    {
        // 全局的 Host 实例（DI 容器）
        public static IHost? AppHost { get; private set; }
        private readonly EngineHostProcess _engineHost = new();
        private readonly Yolo11EngineProcess _yolo11 = new();
        private readonly CancellationTokenSource _startupCancellation = new();
        private readonly ApplicationShutdownController _shutdown;
        public App()
        {
            _shutdown = new ApplicationShutdownController(this, CleanupAsync);
            AppHost = new HostBuilder()
                .ConfigureServices((context, services) =>
                {                    
                    services.AddSingleton<IUserDialogService>(provider => new UserDialogService(
                        () => provider.GetRequiredService<PreferencesWindow>(), () => provider.GetRequiredService<ExtensionsWindow>(),
                        () => provider.GetRequiredService<PrinterSettingsWindow>()));
                    services.AddSingleton<PrinterProfileStore>();
                    services.AddSingleton<AMLabSlicer.Core.Printers.IPrinterProfileProvider>(provider => provider.GetRequiredService<PrinterProfileStore>());
                    services.AddSingleton<AMLabSlicer.Core.Printers.PrinterKinematicsRegistry>();
                    services.AddTransient<PrinterSettingsViewModel>();
                    services.AddTransient<PrinterSettingsWindow>();
                    services.AddSingleton<IModelImportService, ModelImportService>();
                    services.AddSingleton(_engineHost);
                    services.AddSingleton(_yolo11);
                    services.AddSingleton<ISlicingService, GrpcSlicingService>();
                    services.AddSingleton<ISliceRequestFactory, SliceRequestFactory>();
                    services.AddSingleton<MainWindowViewModel>();
                    services.AddTransient<PrepareWorkspaceViewModel>();                    
                    services.AddSingleton<MainWindow>();
                    services.AddSingleton<IParameterStore, ParameterStore>();
                    services.AddSingleton<IPreferencesStore>(_ => new JsonPreferencesStore());
                    services.AddSingleton<IAppearanceService, AppearanceService>();
                    services.AddSingleton<PreferencesViewModel>(provider => new PreferencesViewModel(provider.GetRequiredService<IPreferencesStore>(), provider.GetRequiredService<IAppearanceService>()));
                    services.AddSingleton<PluginManager>();
                    services.AddTransient<PreferencesWindow>();
                    services.AddSingleton<ExtensionsViewModel>();
                    services.AddTransient<ExtensionsWindow>();
                })
                .Build();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var startup = new StartupWindow();
            MainWindow = startup;
            void CancelStartup(object? sender, EventArgs args)
            {
                _shutdown.RequestExit();
            }
            startup.Closed += CancelStartup;
            startup.Show();
            var token = _startupCancellation.Token;
            try
            {
                // Let the splash render before resolving UI services or loading plugins.
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                token.ThrowIfCancellationRequested();
                await AppHost!.StartAsync(token);
                token.ThrowIfCancellationRequested();
                startup.SetStatus("正在加载扩展…");
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                token.ThrowIfCancellationRequested();
                await AppHost.Services.GetRequiredService<PluginManager>().LoadEnabledAsync(token);
                token.ThrowIfCancellationRequested();

                startup.SetStatus("正在准备工作区…");
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                token.ThrowIfCancellationRequested();
                var mainWindow = AppHost.Services.GetRequiredService<MainWindow>();
                mainWindow.DataContext = AppHost.Services.GetRequiredService<MainWindowViewModel>();

                var visionStartup = StartVisionAsync(token);
                startup.SetStatus("正在准备切片服务…");
                _engineHost.ShowConsole = AppHost.Services.GetRequiredService<PreferencesViewModel>().ShowEngineHostConsole;
                Exception? engineError = null;
                try { await _engineHost.EnsureStartedAsync(token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex) { engineError = ex; }
                token.ThrowIfCancellationRequested();

                startup.SetStatus("正在准备视觉服务…");
                await visionStartup;
                token.ThrowIfCancellationRequested();

                startup.SetStatus("正在打开工作区…");
                // Startup completion is independent of the decorative looping animation.
                token.ThrowIfCancellationRequested();
                MainWindow = mainWindow;
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                _shutdown.Attach(mainWindow);
                mainWindow.Show();
                startup.Closed -= CancelStartup;
                startup.Close();
                mainWindow.Activate();

                // Keep initialization dialogs owned by the visible main window.
                if (engineError is not null)
                    AppHost.Services.GetRequiredService<IUserDialogService>().ShowMessage("后端启动失败：" + engineError.Message, "EngineHost");
                else if (mainWindow.DataContext is MainWindowViewModel main)
                    await main.PrepareWorkspace.InitializeCommand.ExecuteAsync(null);
            }
            catch (OperationCanceledException) when (_startupCancellation.IsCancellationRequested) { }
            catch (Exception ex)
            {
                if (!_startupCancellation.IsCancellationRequested)
                {
                    MessageBox.Show(MainWindow, "应用启动失败：" + ex.Message, "AMLab Slicer", MessageBoxButton.OK, MessageBoxImage.Error);
                    _shutdown.RequestExit(1);
                }
            }
        }

        private async Task StartVisionAsync(CancellationToken token)
        {
            try { await _yolo11.EnsureStartedAsync(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error) { System.Diagnostics.Trace.TraceWarning("YOLO11 startup: {0}", error); }
        }

        private async Task CleanupAsync()
        {
            // Never block the WPF dispatcher: plugin commands may finish on this thread.
            await ApplicationShutdownController.RunStageAsync("startup cancellation",
                () => _startupCancellation.CancelAsync(), TimeSpan.FromSeconds(1));
            var engineShutdown = Task.Run(async () =>
            {
                try { await _engineHost.StopConnectedHostAsync(); }
                finally { _engineHost.Dispose(); }
            });
            var host = AppHost;
            if (host != null)
                await ApplicationShutdownController.RunStageAsync("plugins", async () =>
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await host.Services.GetRequiredService<PluginManager>().StopAllAsync(timeout.Token);
                }, TimeSpan.FromSeconds(3));

            // Independent stages must still run if a plugin faults or ignores cancellation.
            await Task.WhenAll(
                ApplicationShutdownController.RunStageAsync("vision", () => Task.Run(_yolo11.Dispose), TimeSpan.FromSeconds(3)),
                ApplicationShutdownController.RunStageAsync("engine", () => engineShutdown, TimeSpan.FromSeconds(5)));
            if (host != null)
            {
                await ApplicationShutdownController.RunStageAsync("host", async () =>
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                    await host.StopAsync(timeout.Token);
                }, TimeSpan.FromSeconds(1));
                await ApplicationShutdownController.RunStageAsync("services", () => Task.Run(host.Dispose), TimeSpan.FromSeconds(1));
            }
            AppHost = null;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _shutdown.ArmExitGuard();
            // Direct Shutdown / OS exit bypasses Closing. Do not synchronously wait on UI
            // continuations here; dispose owned processes off-thread with the same exit bound.
            if (!_shutdown.IsRequested)
            {
                // This path cannot await the dispatcher. Run only context-free engine
                // cleanup on a worker and wait for a bounded time before WPF exits.
                try
                {
                    Task.Run(async () =>
                    {
                        try { await _engineHost.StopConnectedHostAsync(); }
                        finally { _engineHost.Dispose(); }
                    }).Wait(TimeSpan.FromSeconds(5));
                }
                catch (Exception error) { System.Diagnostics.Trace.TraceWarning("Exit engine cleanup: {0}", error); }
            }
            base.OnExit(e);
        }
    }
}
