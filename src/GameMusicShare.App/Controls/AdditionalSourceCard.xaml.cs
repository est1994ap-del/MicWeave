using System.Windows;
using System.Windows.Controls;
using GameMusicShare.Audio;
using GameMusicShare.Services;

namespace GameMusicShare.Controls;

public partial class AdditionalSourceCard : UserControl
{
    private readonly AudioEngine engine;
    private readonly SourceMix channel;
    private readonly AudioCatalog catalog;
    private readonly SourceSettings settings;
    private readonly Func<AudioSource?, string?> validate;
    private bool refreshing, busy, disposed, synchronizing, playbackBusy;
    private long lastPlaybackCheck;
    public AudioSource? SelectedSource => SourceSelector.SelectedItem as AudioSource;
    public event Action? Changed;
    public event Action<AdditionalSourceCard>? RemoveRequested;
    public event Action<string>? Status;

    public AdditionalSourceCard(AudioEngine engine, AudioCatalog catalog, SourceSettings settings, Func<AudioSource?, string?> validate)
    {
        this.engine = engine; this.catalog = catalog; this.settings = settings; this.validate = validate;
        channel = engine.Mix.AddSource();
        InitializeComponent();
        Level.Value = double.IsFinite(settings.LevelDb) ? Math.Clamp(settings.LevelDb, -60, 6) : -14;
        channel.Gain = MixProvider.DecibelsToGain(Level.Value);
        LevelText.Text = $"{Level.Value:0} dB";
        Loaded += (_, _) => RefreshSources();
    }

    public void SetNumber(int number) => Heading.Text = $"AUDIO SOURCE {number}";
    public void RefreshSources()
    {
        if (disposed || busy) return;
        refreshing = true;
        var previous = SelectedSource?.Id ?? settings.SourceId;
        try
        {
            var list = catalog.GetSources(); SourceSelector.ItemsSource = list;
            SourceSelector.SelectedItem = list.FirstOrDefault(s => s.Id == previous) ??
                list.FirstOrDefault(s => settings.ProgramName != null && s.Kind == AudioSourceKind.Program && s.Name.StartsWith(settings.ProgramName + " — ", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) { Status?.Invoke(ex.Message); }
        finally { refreshing = false; }
        if (!engine.IsSourceRunning(channel)) SourceChanged(this, null!);
    }
    private async void SourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (refreshing || busy || disposed) return;
        busy = true; SourceSelector.IsEnabled = false;
        try
        {
            engine.StopSource(channel);
            if (validate(SelectedSource) is string error) throw new InvalidOperationException(error);
            await engine.SelectSourceAsync(channel, SelectedSource);
            settings.SourceId = SelectedSource?.Id;
            settings.ProgramName = SelectedSource?.Kind == AudioSourceKind.Program ? SelectedSource.Name.Split(" — ")[0] : null;
            Changed?.Invoke();
            Status?.Invoke("Source ready. Turn on its Active switch to add it to your microphone.");
        }
        catch (Exception ex) { if (!disposed) Status?.Invoke(ex.Message); }
        finally { busy = false; if (!disposed) { SourceSelector.IsEnabled = true; UpdateMeters(); } }
    }
    private void ActiveChanged(object sender, RoutedEventArgs e)
    {
        if (synchronizing || disposed || channel == null) return;
        try
        {
            if (ActiveSwitch.IsChecked == true) engine.RouteSource(channel);
            else channel.Enabled = false;
            Status?.Invoke(channel.Enabled ? "Audio source added to MicWeave Microphone." : "Audio source removed from the mix. Other sources continue.");
        }
        catch (Exception ex) { Status?.Invoke(ex.Message); }
        UpdateMeters();
    }
    public void UpdateMeters()
    {
        if (disposed) return;
        var meter = channel.Meter.Snapshot(); SourceSpectrum.Update(meter);
        SourceState.Text = !engine.IsSourceRunning(channel) ? "CHOOSE A SOURCE" : channel.Muted ? "MUTED" : channel.Enabled ? (meter.HasSignal ? "SHARING AUDIO" : "LISTENING") : "PREVIEW ONLY";
        synchronizing = true;
        ActiveSwitch.IsChecked = channel.Enabled;
        ActiveSwitch.IsEnabled = !busy && engine.IsSourceRunning(channel);
        synchronizing = false;
        MuteButton.Content = channel.Muted ? "Unmute" : "Mute";
        if (!playbackBusy && Environment.TickCount64 - lastPlaybackCheck > 1500) UpdatePlayback();
    }
    private async void UpdatePlayback()
    {
        playbackBusy = true; lastPlaybackCheck = Environment.TickCount64;
        var selected = SelectedSource;
        try
        {
            var playback = await SourcePlayback.StateAsync(selected);
            if (disposed || !ReferenceEquals(selected, SelectedSource)) return;
            PlaybackButton.IsEnabled = playback.Available;
            PlaybackButton.Content = playback.Playing ? "Pause" : "Play";
            PlaybackButton.Tag = playback.Playing ? "\uE769" : "\uE768";
        }
        catch { if (!disposed) PlaybackButton.IsEnabled = false; }
        finally { playbackBusy = false; }
    }
    private async void PlaybackClick(object sender, RoutedEventArgs e)
    {
        try { if (!await SourcePlayback.ToggleAsync(SelectedSource)) Status?.Invoke("This source does not accept playback controls."); lastPlaybackCheck = 0; }
        catch (Exception ex) { Status?.Invoke(ex.Message); }
    }
    private void MuteClick(object sender, RoutedEventArgs e) { channel.Muted = !channel.Muted; UpdateMeters(); }
    private void LevelChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (channel == null || LevelText == null) return;
        channel.Gain = MixProvider.DecibelsToGain(Level.Value); settings.LevelDb = Level.Value;
        LevelText.Text = $"{Level.Value:0} dB"; Changed?.Invoke();
    }
    private void RefreshClick(object sender, RoutedEventArgs e) => RefreshSources();
    private void RemoveClick(object sender, RoutedEventArgs e) => RemoveRequested?.Invoke(this);
    public void DisposeSource() { if (disposed) return; disposed = true; engine.RemoveSource(channel); }
}
