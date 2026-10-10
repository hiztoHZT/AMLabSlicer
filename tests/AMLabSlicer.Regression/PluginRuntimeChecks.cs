using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Runtime.Loader;
using AMLabSlicer.Services;
using AMLabSlicer.ViewModel;

internal static class PluginRuntimeChecks
{
    // A fresh process exercises WPF resource resolution without static Agent references.
    public static async Task RunAsync(string directory, string data, int expectedCount, bool remove = false)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/AMLabSlicer.UI;component/Themes/DarkTheme.xaml") });
        var manager = new PluginManager(new PreferencesViewModel(), new FakeDialogs(), directory, data);
        await manager.LoadEnabledAsync();
        if (manager.Panels.Count != expectedCount) throw new InvalidOperationException(string.Join("; ", manager.Packages.Select(p => p.State)));
        if (expectedCount != 0)
        {
            var panel = (UserControl)manager.Panels.Single().Content!;
            if (AssemblyLoadContext.GetLoadContext(panel.GetType().Assembly) is not PluginLoadContext) throw new InvalidOperationException("插件未独立加载");
            var window = new Window { Content = panel, Width = 400, Height = 640, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
            window.Show(); window.UpdateLayout();
            if (panel.ActualWidth <= 0) throw new InvalidOperationException("插件面板未显示");
            window.Content = null; window.Close();
            var settings = manager.CreateSettingsPage("amlab.agent")!;
            var host = new Window { Content = settings, Width = 650, Height = 640, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
            host.Show(); host.UpdateLayout(); host.Close();
            if ((string?)settings.DataContext.GetType().GetProperty("AgentModel")!.GetValue(settings.DataContext) != "plugin-owned-model")
                throw new InvalidOperationException("插件设置没有跨进程恢复");
        }
        if (remove) await manager.RemoveAsync(manager.Packages.Single());
        await manager.StopAllAsync(CancellationToken.None);
        Console.WriteLine("Fresh-process plugin check passed: " + expectedCount);
    }
}
