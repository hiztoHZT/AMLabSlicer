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

// Optional host capability. Plugins submit local files; scene ownership stays with the host.
public interface IModelImportContext : IPluginContext
{
    Task ImportModelAsync(string localPath, CancellationToken cancellationToken);
}
public interface ISliceParameterContext : IPluginContext
{
    SliceParameterSnapshot? ReadSliceParameters();
    void ApplySliceParameters(SliceParameterSnapshot snapshot, IReadOnlyList<SliceParameterChange> changes);
}

public sealed record SliceParameterSnapshot(string Context, string Algorithm, IReadOnlyList<SliceParameterEntry> Parameters);
public sealed record SliceParameterEntry(string Key, string Name, object? Value, string? Unit,
    string? Description, double? Minimum, double? Maximum, bool Editable);
public sealed record SliceParameterChange(string Key, double Value);
