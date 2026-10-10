using AMLabSlicer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net.Http;
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
        private readonly CancellationTokenSource _startupCancellation = new();
        private static readonly HttpClient EngineHostShutdownClient = new()
        {
            Timeout = TimeSpan.FromSeconds(2)
        };

        public App()
        {
            AppHost = new HostBuilder()
                .ConfigureServices((context, services) =>
                {                    
                    services.AddSingleton<IUserDialogService>(provider => new UserDialogService(
                        () => provider.GetRequiredService<PreferencesWindow>(), () => provider.GetRequiredService<ExtensionsWindow>()));
                    services.AddSingleton<IModelImportService, ModelImportService>();
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

            // 启动 Host
            await AppHost!.StartAsync();

            await AppHost.Services.GetRequiredService<PluginManager>().LoadEnabledAsync();

            // 从 DI 容器中提取 MainWindow
            var mainWindow = AppHost.Services.GetRequiredService<MainWindow>();

            // 自动解析并注入 ViewModel（如果需要）
            mainWindow.DataContext = AppHost.Services.GetRequiredService<MainWindowViewModel>();

            mainWindow.Show();
            try
            {
                _engineHost.ShowConsole = AppHost.Services.GetRequiredService<PreferencesViewModel>().ShowEngineHostConsole;
                await _engineHost.EnsureStartedAsync(_startupCancellation.Token);
                _startupCancellation.Token.ThrowIfCancellationRequested();
                if (mainWindow.DataContext is MainWindowViewModel { CurrentWorkspace: PrepareWorkspaceViewModel workspace })
                    await workspace.InitializeCommand.ExecuteAsync(null);
            }
            catch (OperationCanceledException) when (_startupCancellation.IsCancellationRequested) { }
            catch (Exception ex)
            {
                if (!_startupCancellation.IsCancellationRequested)
                    AppHost.Services.GetRequiredService<IUserDialogService>().ShowMessage("后端启动失败：" + ex.Message, "EngineHost");
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _startupCancellation.Cancel();
            // WPF does not await OnExit. Finish bounded cleanup before the dispatcher exits.
            try
            {
                // EngineHost shutdown must not be skipped when a plugin fails to stop.
                ShutdownEngineHostAsync().GetAwaiter().GetResult();
                _engineHost.Dispose();
                using var pluginTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try { AppHost?.Services.GetRequiredService<PluginManager>().StopAllAsync(pluginTimeout.Token).GetAwaiter().GetResult(); }
                catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("Plugin shutdown: {0}", ex.Message); }
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                AppHost?.StopAsync(timeout.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Trace.TraceWarning("Application host shutdown timed out.");
            }
            finally
            {
                AppHost?.Dispose();
                AppHost = null;
                base.OnExit(e);
            }
        }

        private static async Task ShutdownEngineHostAsync()
        {
            try
            {
                await EngineHostShutdown.RequestAsync(EngineHostShutdownClient).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                System.Diagnostics.Trace.TraceWarning("EngineHost shutdown: {0}", ex.Message);
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Trace.TraceWarning("EngineHost shutdown timed out.");
            }
        }
    }
}
