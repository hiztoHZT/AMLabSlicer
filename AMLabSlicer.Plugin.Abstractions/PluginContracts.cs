namespace AMLabSlicer.Plugin.Abstractions;

public interface IPlugin
{
    Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public interface IPluginContext
{
    string PluginId { get; }
    IPluginSettingsStore Settings { get; }
    // Navigation only: settings are hosted under the application's Plugin menu.
    void OpenSettings();
    void Log(string message);
}

public interface IPluginSettingsStore
{
    string Location { get; }
    T Load<T>() where T : class, new();
    void Save<T>(T settings) where T : class;
}
