using System.IO;
using System.Text.Json;

namespace AMLabSlicer.Services;

internal sealed record MoonrakerConnectionSettings(string Address, int RefreshSeconds)
{
    internal const int DefaultRefreshSeconds = 1;
    internal const int MaxRefreshSeconds = 3600;

    private static string Location => Path.Combine(AppStoragePaths.DataDirectory, "moonraker-monitor.json");

    internal static MoonrakerConnectionSettings Load()
    {
        try
        {
            if (File.Exists(Location) && JsonSerializer.Deserialize<MoonrakerConnectionSettings>(File.ReadAllText(Location)) is { } saved)
                return new MoonrakerConnectionSettings(
                    MoonrakerClient.NormalizeAddress(saved.Address).AbsoluteUri,
                    saved.RefreshSeconds is >= 1 and <= MaxRefreshSeconds ? saved.RefreshSeconds : DefaultRefreshSeconds);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            System.Diagnostics.Trace.TraceWarning("Moonraker settings: {0}", error.Message);
        }
        return new(MoonrakerClient.DefaultAddress, DefaultRefreshSeconds);
    }

    internal void Save() => LocalJsonFile.Save(Location, this);
}
