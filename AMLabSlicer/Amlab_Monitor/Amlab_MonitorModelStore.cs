using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AMLabSlicer.Amlab_Monitor;

public sealed partial class Amlab_MonitorModelEntry(string id, string name, string weightPath) : ObservableObject
{
    public string Id { get; } = id;

    [ObservableProperty] private string _name = name;
    [ObservableProperty] private string _weightPath = weightPath;
}

internal static class Amlab_MonitorModelStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal static string ConfigPath => Path.Combine(
        Environment.GetEnvironmentVariable("LOCALAPPDATA") ??
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AMLabSlicer", "Monitor", "models.json");

    internal static (string? Snapshot, List<Amlab_MonitorModelEntry> Entries) Load()
    {
        var snapshot = Read(ConfigPath);
        if (snapshot is null)
            return (null, []);
        var entries = JsonSerializer.Deserialize<List<Amlab_MonitorModelEntry>>(snapshot, JsonOptions)
            ?? throw new InvalidDataException("The vision model configuration must be a JSON array.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry is null || !Guid.TryParseExact(entry.Id, "N", out _) || !ids.Add(entry.Id) ||
                string.IsNullOrWhiteSpace(entry.Name) || !IsWeightPath(entry.WeightPath))
                throw new InvalidDataException("The vision model configuration contains an invalid entry.");
        }
        return (snapshot, entries);
    }

    internal static bool TrySave(string? snapshot, IEnumerable<Amlab_MonitorModelEntry> entries)
    {
        var path = ConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writerLock = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        if (!string.Equals(snapshot, Read(path), StringComparison.Ordinal))
            return false;

        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(entries, JsonOptions));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
        return true;
    }

    internal static bool IsWeightPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) &&
        string.Equals(Path.GetExtension(path), ".pt", StringComparison.OrdinalIgnoreCase);

    private static string? Read(string path)
    {
        try { return File.ReadAllText(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
}
