using System.IO;
using System.Text.Json;

namespace AMLabSlicer.Services;

public sealed record ExtensionPackageInfo(string Name, string Version, string Location, string State);
public interface IExtensionInventory
{
    string DirectoryPath { get; }
    IReadOnlyList<ExtensionPackageInfo> ReadPackages();
}
// Metadata discovery only: packages are not loaded or executed.
public sealed class ExtensionInventory(string? directory = null) : IExtensionInventory
{
    public string DirectoryPath { get; } = directory ?? AppStoragePaths.ExtensionsDirectory;
    public IReadOnlyList<ExtensionPackageInfo> ReadPackages()
    {
        var result = new List<ExtensionPackageInfo>();
        if (!Directory.Exists(DirectoryPath)) return result;
        foreach (var idFolder in Directory.EnumerateDirectories(DirectoryPath).Take(200))
        foreach (var versionFolder in Directory.EnumerateDirectories(idFolder).Take(50))
        {
            var manifest = Path.Combine(versionFolder, "extension.json");
            if (!File.Exists(manifest)) continue;
            try
            {
                if (new FileInfo(manifest).Length > 1024 * 1024) throw new InvalidDataException("拓展清单过大");
                using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
                var name = doc.RootElement.GetProperty("name").GetString();
                var version = doc.RootElement.GetProperty("version").GetString();
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version)) throw new InvalidDataException("拓展名称或版本为空");
                result.Add(new(name, version, versionFolder, "未加载"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException)
            { result.Add(new(Path.GetFileName(idFolder), Path.GetFileName(versionFolder), versionFolder, "清单无法读取")); }
        }
        return result.OrderBy(p => p.Name).ThenBy(p => p.Version).ToArray();
    }
}
