using System.IO;
namespace AMLabSlicer.Services;

public static class AppStoragePaths
{
    public static bool IsPortable => File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.mode"));
    public static string DataDirectory => IsPortable ? Path.Combine(AppContext.BaseDirectory, "Data") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AMLabSlicer");
    public static string ExtensionsDirectory => Path.Combine(AppContext.BaseDirectory, "extensions");
}
