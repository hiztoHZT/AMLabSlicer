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
        private static readonly HttpClient EngineHostShutdownClient = new()
        {
            Timeout = TimeSpan.FromSeconds(1)
        };

        public App()
        {
            AppHost = new HostBuilder()
                .ConfigureServices((context, services) =>
                {                    
                    services.AddSingleton<IUserDialogService>(provider => new UserDialogService(
                        () => provider.GetRequiredService<PreferencesWindow>()));
                    services.AddSingleton<IModelImportService, ModelImportService>();
                    services.AddSingleton<ISlicingService, GrpcSlicingService>();
                    services.AddSingleton<ISliceRequestFactory, SliceRequestFactory>();
                    services.AddSingleton<MainWindowViewModel>();
                    services.AddTransient<PrepareWorkspaceViewModel>();                    
                    services.AddSingleton<MainWindow>();
                    services.AddSingleton<IParameterStore, ParameterStore>();
                    services.AddSingleton<PreferencesViewModel>();
                    services.AddTransient<PreferencesWindow>();
                })
                .Build();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 启动 Host
            await AppHost!.StartAsync();

            // 从 DI 容器中提取 MainWindow
            var mainWindow = AppHost.Services.GetRequiredService<MainWindow>();

            // 自动解析并注入 ViewModel（如果需要）
            mainWindow.DataContext = AppHost.Services.GetRequiredService<MainWindowViewModel>();

            mainWindow.Show();
            if (mainWindow.DataContext is MainWindowViewModel { CurrentWorkspace: PrepareWorkspaceViewModel workspace })
                await workspace.InitializeCommand.ExecuteAsync(null);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // WPF does not await OnExit. Finish bounded cleanup before the dispatcher exits.
            try
            {
                ShutdownEngineHostAsync().GetAwaiter().GetResult();
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
                using var response = await EngineHostShutdownClient.PostAsync("http://localhost:50051/shutdown", null).ConfigureAwait(false);
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
