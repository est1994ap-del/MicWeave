using System.IO;
using System.Text.Json;

namespace GameMusicShare.Services;

public sealed class SettingsStore
{
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductInfo.Name);
    private readonly string path;
    public SettingsStore(string? path = null) => this.path = path ?? Path.Combine(DataDirectory, "settings.json");
    public UserSettings Load()
    {
        try
        {
            var settings = File.Exists(path) ? JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path)) ?? new() : new UserSettings();
            settings.SourceLevelDb = double.IsFinite(settings.SourceLevelDb) ? Math.Clamp(settings.SourceLevelDb, -60, 6) : -14;
            settings.MicrophoneLevelDb = double.IsFinite(settings.MicrophoneLevelDb) ? Math.Clamp(settings.MicrophoneLevelDb, -60, 6) : 0;
            settings.MasterLevelDb = double.IsFinite(settings.MasterLevelDb) ? Math.Clamp(settings.MasterLevelDb, -60, 6) : 0;
            return settings;
        }
        catch (Exception ex) { AppLog.Write(ex); return new(); }
    }
    public void Save(UserSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }
}
public static class AppLog
{
    private static readonly object Gate = new();
    public static void Write(Exception ex)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(SettingsStore.DataDirectory);
                var log = Path.Combine(SettingsStore.DataDirectory, "app.log");
                var message = $"{DateTimeOffset.Now:u} {ex}\n";
                if (message.Length > 16384) message = message[..16384] + "\n[truncated]\n";
                if (File.Exists(log) && new FileInfo(log).Length + System.Text.Encoding.UTF8.GetByteCount(message) > 2 * 1024 * 1024)
                    File.WriteAllText(log, string.Empty);
                File.AppendAllText(log, message);
            }
        }
        catch { /* Logging must not take down the audio engine. */ }
    }
}
