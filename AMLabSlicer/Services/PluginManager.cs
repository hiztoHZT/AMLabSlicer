using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Windows.Controls;
using AMLabSlicer.Plugin.Abstractions;
using AMLabSlicer.Plugin.Wpf;
using AMLabSlicer.ViewModel;
using CommunityToolkit.Mvvm.ComponentModel;
namespace AMLabSlicer.Services;

public sealed partial class PluginPackage : ObservableObject
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Location { get; init; }
    public PluginManifest? Manifest { get; init; }
    [ObservableProperty] private string _state = "未加载";
    internal IPlugin? Instance;
    internal PluginLoadContext? LoadContext;
    internal bool WasLoaded;
}

public sealed class PluginManager
{
    public string DirectoryPath { get; }
    public ObservableCollection<PluginPackage> Packages { get; } = new();
    public ObservableCollection<WorkspacePanelEntry> Panels { get; } = new();
    private readonly string _dataPath;
    private readonly PreferencesViewModel _preferences;
    private readonly IUserDialogService _dialogs;
    private readonly PluginSettingsStore _registry;
    private readonly SemaphoreSlim _gate = new(1);
    private PluginRegistryState _state;
    public PluginManager(PreferencesViewModel preferences, IUserDialogService dialogs, string? directory = null, string? dataPath = null)
    {
        _preferences = preferences; _dialogs = dialogs;
        DirectoryPath = Path.GetFullPath(directory ?? AppStoragePaths.ExtensionsDirectory);
        _dataPath = dataPath ?? Path.Combine(AppStoragePaths.DataDirectory, "plugins");
        _registry = new PluginSettingsStore(Path.Combine(_dataPath, "registry.json"));
        try { _state = _registry.Load<PluginRegistryState>(); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        { _state = new() { SuspendLoading = true }; Trace.TraceWarning("插件注册表损坏，暂停自动加载：{0}", ex.Message); }
        _state.Disabled ??= new(StringComparer.Ordinal);
        _state.PendingRemoval ??= new(StringComparer.Ordinal);
    }
    public void Discover()
    {
        var existing = Packages.Where(p => p.WasLoaded).ToDictionary(p => p.Location, StringComparer.OrdinalIgnoreCase);
        Packages.Clear();
        if (!Directory.Exists(DirectoryPath)) { foreach (var loaded in existing.Values) Packages.Add(loaded); return; }
        foreach (var idDirectory in Directory.EnumerateDirectories(DirectoryPath).Where(p => !Path.GetFileName(p).StartsWith('.')).Take(200))
        foreach (var versionDirectory in Directory.EnumerateDirectories(idDirectory).Take(50))
        {
            if (existing.TryGetValue(versionDirectory, out var loaded)) { Packages.Add(loaded); continue; }
            try
            {
                var manifest = PluginManifest.Read(versionDirectory);
                if (Path.GetFileName(idDirectory) != manifest.Id || Path.GetFileName(versionDirectory) != manifest.Version)
                    throw new InvalidDataException("插件目录与清单标识或版本不一致");
                Packages.Add(new() { Id = manifest.Id, Name = manifest.Name, Version = manifest.Version, Location = versionDirectory, Manifest = manifest });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
            { Packages.Add(new() { Id = Path.GetFileName(idDirectory), Name = Path.GetFileName(idDirectory), Version = Path.GetFileName(versionDirectory), Location = versionDirectory, State = "无效包：" + ex.Message }); }
        }
        foreach (var group in Packages.Where(p => p.Manifest is not null).GroupBy(p => p.Id))
        {
            var latest = group.OrderByDescending(p => System.Version.Parse(p.Version)).First();
            foreach (var package in group.Where(p => p != latest && !p.WasLoaded)) package.State = "旧版本（重启时使用最新版本）";
            if (!latest.WasLoaded) latest.State = _state.PendingRemoval.Contains(latest.Id + "/" + latest.Version) ? "待重启卸载" :
                _state.SuspendLoading || _state.Disabled.Contains(latest.Id) ? "已禁用" : "待加载";
        }
        // Preserve stopped/loaded contexts even if someone moves their package on disk.
        foreach (var loaded in existing.Values.Where(p => !Packages.Contains(p))) Packages.Add(loaded);
    }
    public async Task LoadEnabledAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var relative in _state.PendingRemoval.ToArray())
            {
                try { DeletePackageDirectory(Path.Combine(DirectoryPath, relative)); _state.PendingRemoval.Remove(relative); _registry.Save(_state); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.TraceWarning("插件包删除失败：{0}", ex.Message); }
            }
            Discover();
            foreach (var package in Packages.Where(p => p.State == "待加载").ToArray()) await LoadAsync(package, cancellationToken);
        }
        finally { _gate.Release(); }
    }
    private async Task LoadAsync(PluginPackage package, CancellationToken cancellationToken)
    {
        IPlugin? plugin = null;
        try
        {
            var manifest = package.Manifest!;
            var settings = new PluginSettingsStore(Path.Combine(_dataPath, manifest.Id, "settings.json"));
            // Migration is one-way and only runs before the plugin owns its configuration.
            if (manifest.Id == "amlab.agent" && !File.Exists(settings.Location)) settings.Save(new
            {
                _preferences.AgentEndpoint, _preferences.AgentModel, _preferences.AgentApiKeyVariable,
                _preferences.AgentSystemPrompt, _preferences.AgentTimeoutSeconds
            });
            var context = new PluginLoadContext(Path.Combine(package.Location, manifest.EntryAssembly));
            package.LoadContext = context; package.WasLoaded = true;
            var assembly = context.LoadFromAssemblyPath(Path.Combine(package.Location, manifest.EntryAssembly));
            var type = assembly.GetType(manifest.EntryType, throwOnError: true)!;
            if (!typeof(IPlugin).IsAssignableFrom(type) || type.IsAbstract) throw new InvalidDataException("插件必须实现 IPlugin");
            plugin = (IPlugin)Activator.CreateInstance(type)!;
            await plugin.InitializeAsync(new HostPluginContext(manifest.Id, settings, () => _dialogs.OpenExtensions(manifest.Id)), cancellationToken);
            // Panel creation is on the host UI thread. Only this right-side collection displays it.
            if (plugin is IPanelPlugin panelPlugin)
            {
                var panel = panelPlugin.CreatePanel() ?? throw new InvalidDataException("交互插件没有返回面板");
                Panels.Add(new(manifest.Id, manifest.Name, string.IsNullOrWhiteSpace(manifest.Icon) ? "P" : manifest.Icon, panel));
            }
            package.Instance = plugin; package.State = "已启用";
        }
        catch (Exception ex)
        {
            if (plugin is not null)
                try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)); await plugin.StopAsync(timeout.Token); } catch (Exception stopError) { Trace.TraceWarning("插件清理：{0}", stopError.Message); }
            package.State = "加载失败：" + ex.Message;
        }
    }
    public UserControl? CreateSettingsPage(string id) => (Packages.FirstOrDefault(p => p.Id == id && p.Instance is not null)?.Instance as IPluginSettingsPage)?.CreateSettingsPage();
    public async Task DisableAsync(PluginPackage package, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _state.Disabled.Add(package.Id); _registry.Save(_state);
            foreach (var panel in Panels.Where(p => p.Id == package.Id).ToArray()) Panels.Remove(panel);
            if (package.Instance is not null)
            {
                try { using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(3)); await package.Instance.StopAsync(timeout.Token); }
                finally { package.Instance = null; package.State = "已禁用，程序集将在退出时释放"; }
            }
            else package.State = "已禁用";
        }
        finally { _gate.Release(); }
    }
    public async Task EnableAsync(PluginPackage package, CancellationToken cancellationToken = default)
    {
        if (package.Manifest is null) return;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (package.Instance is not null) return;
            if (_state.PendingRemoval.Contains(package.Id + "/" + package.Version)) throw new InvalidOperationException("插件已安排卸载，请重启后重新安装");
            if (Packages.Any(p => p.Id == package.Id && p != package && p.Instance is not null)) throw new InvalidOperationException("同一插件只能启用一个版本");
            if (Packages.Any(p => p.Id == package.Id && p.Manifest is not null && System.Version.Parse(p.Version) > System.Version.Parse(package.Version)))
                throw new InvalidOperationException("请启用最新插件版本");
            _state.Disabled.Remove(package.Id); _state.SuspendLoading = false; _registry.Save(_state);
            if (Packages.Any(p => p.Id == package.Id && p.WasLoaded)) package.State = "待重启启用";
            else await LoadAsync(package, cancellationToken);
        }
        finally { _gate.Release(); }
    }
    public async Task StopAllAsync(CancellationToken cancellationToken)
    {
        foreach (var package in Packages.Where(p => p.Instance is not null).ToArray())
        {
            try { await package.Instance!.StopAsync(cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) { Trace.TraceWarning("插件退出：{0}", ex.Message); }
            package.Instance = null;
        }
    }
    public async Task RemoveAsync(PluginPackage package, CancellationToken cancellationToken = default)
    {
        if (package.Manifest is null) throw new InvalidOperationException("无效包请通过目录手动处理");
        await DisableAsync(package, cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (package.WasLoaded)
            {
                _state.PendingRemoval.Add(package.Id + "/" + package.Version); _registry.Save(_state);
                package.State = "待重启卸载";
            }
            else { DeletePackageDirectory(package.Location); Packages.Remove(package); }
        }
        finally { _gate.Release(); }
    }
    private void DeletePackageDirectory(string directory)
    {
        var target = Path.GetFullPath(directory);
        var relative = Path.GetRelativePath(DirectoryPath, target).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!target.StartsWith(DirectoryPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || relative.Length != 2 ||
            relative.Any(p => p is "." or ".." || p.StartsWith('.')))
            throw new InvalidDataException("插件删除路径超出版本目录");
        if (!Directory.Exists(target)) return;
        foreach (var parent in new[] { DirectoryPath, Path.GetDirectoryName(target)! })
            if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("插件路径含目录链接，拒绝递归删除");
        var pending = new Stack<string>(); pending.Push(target);
        while (pending.TryPop(out var current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("插件目录含链接，拒绝递归删除");
            foreach (var child in Directory.EnumerateFileSystemEntries(current))
            {
                var attributes = File.GetAttributes(child);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("插件目录含链接，拒绝递归删除");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(child);
            }
        }
        Directory.Delete(target, recursive: true);
    }
    public async Task InstallAsync(string zipPath, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        var staging = Path.Combine(DirectoryPath, ".install-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);
            using var archive = ZipFile.OpenRead(zipPath);
            if (archive.Entries.Count > 2000) throw new InvalidDataException("插件包文件过多");
            long size = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                size = checked(size + entry.Length);
                if (size > 100 * 1024 * 1024) throw new InvalidDataException("插件包超过 100 MB");
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("插件包不允许符号链接");
                var path = Path.GetFullPath(Path.Combine(staging, entry.FullName));
                if (!path.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("插件包包含目录越界路径");
                if (entry.Name.Length == 0) { Directory.CreateDirectory(path); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(path)!); entry.ExtractToFile(path);
            }
            var manifest = PluginManifest.Read(staging);
            var destination = Path.Combine(DirectoryPath, manifest.Id, manifest.Version);
            if (Directory.Exists(destination)) throw new InvalidDataException("这个插件版本已经安装");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            // Installation does not execute code. Activation is an explicit management action.
            _state.Disabled.Add(manifest.Id); _registry.Save(_state);
            Directory.Move(staging, destination);
            Packages.Add(new() { Id = manifest.Id, Name = manifest.Name, Version = manifest.Version, Location = destination, Manifest = manifest, State = "已安装，未启用" });
        }
        finally
        {
            if (Directory.Exists(staging) && Path.GetFullPath(staging).StartsWith(DirectoryPath + Path.DirectorySeparatorChar + ".install-", StringComparison.OrdinalIgnoreCase))
                Directory.Delete(staging, recursive: true);
            _gate.Release();
        }
    }
    public sealed class PluginRegistryState
    {
        public HashSet<string> Disabled { get; set; } = new(StringComparer.Ordinal);
        public bool SuspendLoading { get; set; }
        public HashSet<string> PendingRemoval { get; set; } = new(StringComparer.Ordinal);
    }
    private sealed record HostPluginContext(string PluginId, IPluginSettingsStore Settings, Action Navigate) : IPluginContext
    {
        public void OpenSettings() => Navigate();
        public void Log(string message) => Trace.TraceInformation("Plugin {0}: {1}", PluginId, message);
    }
}
