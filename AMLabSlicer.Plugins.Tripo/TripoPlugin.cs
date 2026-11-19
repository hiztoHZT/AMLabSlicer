using System.Net.Http;
using System.Windows.Controls;
using AMLabSlicer.Plugin.Abstractions;
using AMLabSlicer.Plugin.Wpf;

namespace AMLabSlicer.Plugins.Tripo;

public sealed class TripoPlugin : IPanelPlugin
{
    private HttpClient? _client;
    private TripoViewModel? _vm;
    public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        _vm = new TripoViewModel(context, new TripoService(_client));
        return Task.CompletedTask;
    }
    public UserControl CreatePanel() => new TripoPanel { DataContext = _vm };
    public UserControl CreateSettingsPage() => new TripoSettingsPage { DataContext = _vm };
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            _vm?.Dispose();
            if (_vm is not null) await Task.WhenAll(new[] { _vm.GenerateCommand.ExecutionTask, _vm.ImportCommand.ExecutionTask }.OfType<Task>()).WaitAsync(cancellationToken);
        }
        finally { _client?.Dispose(); _client = null; }
    }
}
