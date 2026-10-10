using System.Reflection;
using System.IO;
using System.Runtime.Loader;
using AMLabSlicer.Plugin.Abstractions;
using AMLabSlicer.Plugin.Wpf;
namespace AMLabSlicer.Services;

public sealed class PluginLoadContext(string entryPath) : AssemblyLoadContext("plugin:" + entryPath, isCollectible: false)
{
    private readonly AssemblyDependencyResolver _resolver = new(entryPath);
    protected override Assembly? Load(AssemblyName name)
    {
        foreach (var shared in new[] { typeof(IPlugin).Assembly, typeof(IPanelPlugin).Assembly })
            if (name.Name == shared.GetName().Name)
            {
                if (name.Version is not null && name.Version > shared.GetName().Version)
                    throw new FileLoadException("插件需要更高版本的 SDK");
                return shared;
            }
        // Framework assemblies are shared with the WPF host, not loaded from a package.
        if (name.Name is "PresentationFramework" or "PresentationCore" or "WindowsBase" or "System.Xaml" ||
            name.Name is "System" or "mscorlib" or "netstandard") return null;
        var path = _resolver.ResolveAssemblyToPath(name);
        return path is null ? null : LoadFromAssemblyPath(path);
    }
    protected override IntPtr LoadUnmanagedDll(string name)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(name);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}
