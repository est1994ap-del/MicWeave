using System.Diagnostics;
using Windows.Media.Control;

namespace GameMusicShare.Services;

internal static class SourcePlayback
{
    public static async Task<(bool Available, bool Playing)> StateAsync(AudioSource? source)
    {
        var session = await FindAsync(source);
        if (session == null) return (false, false);
        var playback = session.GetPlaybackInfo();
        return (playback.Controls.IsPlayEnabled || playback.Controls.IsPauseEnabled,
            playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
    }

    public static async Task<bool> ToggleAsync(AudioSource? source)
    {
        var session = await FindAsync(source);
        if (session == null) return false;
        var playing = session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        return playing ? await session.TryPauseAsync() : await session.TryPlayAsync();
    }

    public static async Task<bool> SetPlayingAsync(AudioSource? source, bool playing)
    {
        var session = await FindAsync(source);
        if (session == null) return false;
        return playing ? await session.TryPlayAsync() : await session.TryPauseAsync();
    }

    private static async Task<GlobalSystemMediaTransportControlsSession?> FindAsync(AudioSource? source)
    {
        if (source is not { Kind: AudioSourceKind.Program }) return null;
        string name;
        try { using var process = Process.GetProcessById(source.ProcessId); name = process.ProcessName; }
        catch { return null; }
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        return manager.GetSessions().FirstOrDefault(s =>
            s.SourceAppUserModelId.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            s.SourceAppUserModelId.StartsWith(name + ".", StringComparison.OrdinalIgnoreCase) ||
            s.SourceAppUserModelId.Contains("." + name + "_", StringComparison.OrdinalIgnoreCase) ||
            (name.Equals("Spotify", StringComparison.OrdinalIgnoreCase) && s.SourceAppUserModelId.StartsWith("SpotifyAB.SpotifyMusic_", StringComparison.OrdinalIgnoreCase)));
    }
}
