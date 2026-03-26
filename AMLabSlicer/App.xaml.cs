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

        public App()
        {
            AppHost = new HostBuilder()
                .ConfigureServices((context, services) =>
                {                    
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
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            // 优雅地关闭并释放资源
            await AppHost!.StopAsync();
            AppHost.Dispose();

            base.OnExit(e);
        }
    }
}