using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Teleprompter.App.Services;

/// <summary>
/// Persists <see cref="AppSettings"/> as JSON under %APPDATA%\TalkPrompter.
/// Load never throws (missing or corrupt files yield defaults); Save writes to
/// a temp file and swaps it in so a crash mid-write cannot corrupt settings.
/// </summary>
public static class JsonSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TalkPrompter",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            string json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch (Exception)
        {
            // Corrupt or unreadable settings must never block startup.
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            string dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);

            string temp = SettingsPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
            File.Move(temp, SettingsPath, overwrite: true);
        }
        catch (Exception)
        {
            // Persisting preferences is best-effort; never crash the app for it.
        }
    }
}
