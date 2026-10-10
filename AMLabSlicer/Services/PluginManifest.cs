using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace AMLabSlicer.Services;

public sealed record PluginManifest(string Id, string Name, string Version, int ApiVersion,
    string TargetFramework, string Architecture, string EntryAssembly, string EntryType, string Icon)
{
    public static PluginManifest Read(string directory)
    {
        var path = Path.Combine(directory, "extension.json");
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("插件清单过大");
        var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("插件清单为空");
        if (string.IsNullOrWhiteSpace(manifest.Id) || !Regex.IsMatch(manifest.Id, @"^[a-z][a-z0-9.-]{0,79}$") || manifest.Id.Contains("..") ||
            !System.Version.TryParse(manifest.Version, out _) || string.IsNullOrWhiteSpace(manifest.Name) ||
            manifest.Name.Length > 100 || string.IsNullOrWhiteSpace(manifest.EntryType))
            throw new InvalidDataException("插件标识、名称、版本或入口无效");
        if (manifest.ApiVersion != 1 || manifest.TargetFramework != "net8.0-windows" || manifest.Architecture is not ("x64" or "any"))
            throw new InvalidDataException("插件 API、框架或平台不兼容");
        if (string.IsNullOrWhiteSpace(manifest.EntryAssembly) || Path.GetFileName(manifest.EntryAssembly) != manifest.EntryAssembly ||
            !manifest.EntryAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !File.Exists(Path.Combine(directory, manifest.EntryAssembly)))
            throw new InvalidDataException("插件入口 DLL 不存在或路径无效");
        return manifest;
    }
}
