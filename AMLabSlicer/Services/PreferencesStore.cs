using System.IO;
using System.Text.Json;
using AMLabSlicer.Models;

namespace AMLabSlicer.Services;

public interface IPreferencesStore
{
    UserPreferences Load();
    void Save(UserPreferences preferences);
    string Location { get; }
    string LoadWarning { get; }
}

public sealed class JsonPreferencesStore(string? path = null) : IPreferencesStore
{
    public string Location { get; } = path ?? Path.Combine(AppStoragePaths.DataDirectory, "preferences.json");
    public string LoadWarning { get; private set; } = "";
    public UserPreferences Load()
    {
        try
        {
            var result = File.Exists(Location) ? JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(Location)) ?? new() : new();
            result.Normalize();
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            LoadWarning = "设置文件无法读取，已使用默认值：" + ex.Message;
            return new();
        }
    }
    public void Save(UserPreferences preferences) => LocalJsonFile.Save(Location, preferences);
}

internal static class LocalJsonFile
{
    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(path)) File.Copy(path, path + ".bak", true);
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
