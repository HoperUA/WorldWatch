using System.IO;
using System.Text.Json;
using WorldWatch.Models;

namespace WorldWatch.Services;

/// <summary>Loads/saves <see cref="AppSettings"/> as JSON under %APPDATA%\WorldWatch.</summary>
public static class SettingsStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WorldWatch");

    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings is { Clocks.Count: > 0 })
                {
                    settings.TaskbarMonitors ??= new();
                    return settings;
                }
            }
        }
        catch
        {
            // Corrupt/unreadable file: fall back to defaults rather than crashing on startup.
        }

        return AppSettings.CreateDefault();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // Saving is best-effort; a failed write should not take the app down.
        }
    }
}
