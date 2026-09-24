namespace GameMusicShare;

public enum AudioSourceKind { Program, Input, Playback }
public sealed record AudioDevice(string Id, string Name) { public override string ToString() => Name; }
public sealed record AudioSource(string Id, string Name, AudioSourceKind Kind, int ProcessId = 0)
{
    public string DisplayName => $"{(Kind == AudioSourceKind.Program ? "Program" : Kind == AudioSourceKind.Input ? "Input" : "Speakers")} · {Name}";
    public override string ToString() => DisplayName;
}
public sealed class UserSettings
{
    public string? MicrophoneId { get; set; }
    public string? SourceId { get; set; }
    public string? SourceProgramName { get; set; }
    public double SourceLevelDb { get; set; } = -14;
    public double MicrophoneLevelDb { get; set; }
    public double MasterLevelDb { get; set; }
    public bool AlwaysOnTop { get; set; }
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public List<SourceSettings> ExtraSources { get; set; } = [];
}

public sealed class SourceSettings
{
    public string? SourceId { get; set; }
    public string? ProgramName { get; set; }
    public double LevelDb { get; set; } = -14;
}
