using System.IO;
using System.Text.Json;
using AMLabSlicer.Plugin.Abstractions;
namespace AMLabSlicer.Services;

public sealed class PluginSettingsStore(string path) : IPluginSettingsStore
{
    public string Location { get; } = path;
    public T Load<T>() where T : class, new() => File.Exists(Location)
        ? JsonSerializer.Deserialize<T>(File.ReadAllText(Location)) ?? new() : new();
    public void Save<T>(T settings) where T : class => LocalJsonFile.Save(Location, settings);
}
