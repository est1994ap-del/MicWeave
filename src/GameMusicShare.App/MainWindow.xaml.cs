using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Shell;
using System.Diagnostics;
using GameMusicShare.Audio;
using GameMusicShare.Services;
using GameMusicShare.Controls;

namespace GameMusicShare;

public partial class MainWindow : Window
{
    private readonly SettingsStore store = new();
    private readonly AudioCatalog catalog = new();
    private readonly AudioEngine engine;
    private readonly UserSettings settings;
    private readonly DispatcherTimer timer;
    private bool ready, refreshing, sourceBusy;
    private bool playbackAvailable, playbackBusy;
    private long lastPlaybackCheck;
    private bool synchronizingActivation;
    private bool setupBusy;
    private string? setupHeading;
    private readonly CancellationTokenSource windowClosed = new();
    private readonly List<AdditionalSourceCard> additionalCards = [];
    public MainWindow()
    {
        InitializeComponent();
        settings = store.Load();
        engine = new AudioEngine();
        engine.Error += message => Dispatcher.BeginInvoke(() => { StatusText.Text = message; engine.RecoverOutputFailure(); });
        MicGain.Value = settings.MicrophoneLevelDb; SourceGain.Value = settings.SourceLevelDb; MasterGain.Value = settings.MasterLevelDb;
        // Migrate the old pinned widget to an ordinary desktop window.
        Topmost = false; settings.AlwaysOnTop = false;
        if (settings.WindowWidth is double width && double.IsFinite(width)) Width = Math.Clamp(width, MinWidth, SystemParameters.VirtualScreenWidth);
        if (settings.WindowHeight is double height && double.IsFinite(height)) Height = Math.Clamp(height, MinHeight, SystemParameters.VirtualScreenHeight);
        if (settings.WindowLeft.HasValue && settings.WindowTop.HasValue &&
            settings.WindowLeft.Value >= SystemParameters.VirtualScreenLeft && settings.WindowLeft.Value < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80 &&
            settings.WindowTop.Value >= SystemParameters.VirtualScreenTop && settings.WindowTop.Value < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 80)
        { WindowStartupLocation = WindowStartupLocation.Manual; Left = settings.WindowLeft.Value; Top = settings.WindowTop.Value; }
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        timer.Tick += (_, _) => UpdateMeters();
        SizeChanged += (_, _) => Dispatcher.BeginInvoke(UpdateWindowChrome, DispatcherPriority.Loaded);
        StateChanged += (_, _) =>
        {
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
            UpdateWindowChrome();
        };
        Loaded += async (_, _) =>
        {
            ready = true; RefreshLists(); ApplyLevels(); timer.Start(); UpdateWindowChrome();
            settings.ExtraSources ??= [];
            foreach (var source in settings.ExtraSources.ToArray()) AddSourceCard(source);
            TrySave();
            try
            {
                if (engine.Endpoints == null && VirtualMicrophoneSetup.FindUsbip() != null)
                {
                    await SetupMicrophoneAsync(false);
                }
                else if (engine.Endpoints == null && AskToInstallMicrophone())
                {
                    await SetupMicrophoneAsync(true);
                }
                if (ready && engine.Endpoints != null) Execute(engine.StartSending);
            }
            catch (Exception ex) { if (ready) ShowError(ex); }
        };
        Closed += (_, _) =>
        {
            ready = false; timer.Stop();
            windowClosed.Cancel();
            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
            settings.WindowLeft = bounds.Left; settings.WindowTop = bounds.Top;
            settings.WindowWidth = bounds.Width; settings.WindowHeight = bounds.Height;
            TrySave();
            foreach (var card in additionalCards) card.DisposeSource();
            engine.Dispose();
        };
    }
    private void RefreshLists()
    {
        refreshing = true;
        try
        {
            var previousMic = (MicrophoneSelector.SelectedItem as AudioDevice)?.Id ?? settings.MicrophoneId;
            var previousSource = (SourceSelector.SelectedItem as AudioSource)?.Id ?? settings.SourceId;
            var microphones = catalog.GetMicrophones();
            MicrophoneSelector.ItemsSource = microphones;
            var defaultId = catalog.GetDefaultMicrophoneId();
            MicrophoneSelector.SelectedItem = microphones.FirstOrDefault(d => d.Id == previousMic) ?? microphones.FirstOrDefault(d => d.Id == defaultId && !d.Name.Contains("Voicemeeter", StringComparison.OrdinalIgnoreCase)) ?? microphones.FirstOrDefault();
            var sources = catalog.GetSources();
            SourceSelector.ItemsSource = sources;
            SourceSelector.SelectedItem = sources.FirstOrDefault(s => s.Id == previousSource);
            if (SourceSelector.SelectedItem == null)
                SourceSelector.SelectedItem = sources.FirstOrDefault(s => s.Kind == AudioSourceKind.Program &&
                    s.Name.StartsWith(settings.SourceProgramName ?? "Spotify", StringComparison.OrdinalIgnoreCase));
            if (SourceSelector.SelectedItem == null) engine.StopSource();
            if (MicrophoneSelector.SelectedItem is not AudioDevice currentMic || previousMic != null && currentMic.Id != previousMic) engine.StopMicrophone();
            engine.RefreshOutput();
            StatusText.Text = engine.Endpoints == null ? "Virtual microphone not installed. Source previews work; sending to other apps is unavailable." : $"Virtual microphone ready. Select ‘{ProductInfo.MicrophoneName}’ in your calling or recording app.";
        }
        catch (Exception ex) { ShowError(ex); }
        finally { refreshing = false; }
        if (!engine.HasMicrophone && MicrophoneSelector.SelectedItem is AudioDevice selected) Execute(() => engine.StartMicrophone(selected.Id));
        if (!engine.HasSource && SourceSelector.SelectedItem != null) SourceChanged(SourceSelector, null!);
        UpdateControls();
    }
    private void MicrophoneChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || refreshing || MicrophoneSelector.SelectedItem is not AudioDevice selected) return;
        if (SourceSelector.SelectedItem is AudioSource source && source.Kind == AudioSourceKind.Input && source.Id == selected.Id)
        {
            engine.StopSource(); SourceSelector.SelectedItem = null;
        }
        settings.MicrophoneId = selected.Id;
        Execute(() => engine.StartMicrophone(selected.Id)); TrySave();
    }
    private async void SourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || refreshing || sourceBusy) return;
        sourceBusy = true; SourceSelector.IsEnabled = false;
        try
        {
            var selected = SourceSelector.SelectedItem as AudioSource;
            engine.StopSource();
            if (selected != null && additionalCards.Any(c => c.SelectedSource?.Id == selected.Id))
                throw new InvalidOperationException("That source is already selected in another card. Choose a different source to avoid doubling its audio.");
            if (selected?.Kind == AudioSourceKind.Input && selected.Id == (MicrophoneSelector.SelectedItem as AudioDevice)?.Id)
                throw new InvalidOperationException("That input is already your voice microphone. Choose another input to avoid doubling your voice.");
            StatusText.Text = "Opening source preview…";
            await engine.SelectSourceAsync(selected);
            settings.SourceId = selected?.Id;
            settings.SourceProgramName = selected?.Kind == AudioSourceKind.Program ? selected.Name.Split(" — ")[0] : null;
            playbackAvailable = false; lastPlaybackCheck = 0;
            StatusText.Text = selected == null ? "Choose an audio source." : $"Previewing {selected.Name}. Click Route to add it to your microphone.";
            TrySave();
        }
        catch (Exception ex) { if (ready) ShowError(ex); }
        finally { sourceBusy = false; SourceSelector.IsEnabled = true; UpdateControls(); }
    }
    private void UpdateMeters()
    {
        var mic = engine.Mix.MicrophoneMeter.Snapshot(); var source = engine.Mix.SourceMeter.Snapshot(); var output = engine.Mix.OutputMeter.Snapshot();
        MicSpectrum.Update(mic); SourceSpectrum.Update(source); OutputSpectrum.Update(output);
        foreach (var card in additionalCards) card.UpdateMeters();
        MicState.Text = !engine.HasMicrophone ? "STOPPED" : engine.Mix.MicrophoneMuted ? "MUTED · PREVIEW" : mic.HasSignal ? "AUDIO DETECTED" : "LISTENING";
        SourceState.Text = !engine.HasSource ? "CHOOSE A SOURCE" : engine.Mix.SourceMuted ? "MUTED · PREVIEW" : source.HasSignal ? (engine.Mix.SourceRouted ? "SHARING AUDIO" : "AUDIO · PREVIEW") : "LISTENING";
        OutputState.Text = engine.Endpoints == null ? "DISCONNECTED" : !engine.IsSending ? "NOT ROUTED" : engine.Mix.OutputMuted ? "MUTED" : output.HasSignal ? "LIVE MIX DETECTED" : "SILENT";
        ReadyText.Text = setupHeading ?? (engine.Endpoints == null ? "Microphone setup needed" : "Virtual microphone ready");
        ReadySubtitle.Text = engine.Endpoints == null ? "Click the gear to set up your microphone." : "Choose MicWeave Microphone in your app.";
        ReadyDot.Fill = engine.Endpoints == null ? Brushes.Orange : Brushes.MediumSpringGreen;
        LimiterState.Text = Environment.TickCount64 - engine.Mix.LastLimitedAt < 600 ? "Limiter reducing peaks  ·  Lower a level if this stays on." : "Limiter active  ·  Keeping your audio clear.";
        UpdateControls();
        if (!playbackBusy && Environment.TickCount64 - lastPlaybackCheck > 1500) UpdatePlaybackAsync();
    }
    private async void UpdatePlaybackAsync()
    {
        playbackBusy = true; lastPlaybackCheck = Environment.TickCount64;
        var selected = SourceSelector.SelectedItem as AudioSource;
        try
        {
            var state = await SourcePlayback.StateAsync(selected);
            if (!ready || !ReferenceEquals(SourceSelector.SelectedItem, selected)) return;
            playbackAvailable = state.Available;
            PauseSourceButton.Content = state.Playing ? "Pause" : "Play";
            PauseSourceButton.Tag = state.Playing ? "\uE769" : "\uE768";
            PauseSourceButton.ToolTip = state.Available ? "Control playback in the selected program" : "This source does not expose playback controls. Mute still works.";
        }
        catch { playbackAvailable = false; }
        finally { playbackBusy = false; if (ready) UpdateControls(); }
    }
    private void UpdateControls()
    {
        if (!ready) return;
        StartMicButton.IsEnabled = !engine.HasMicrophone && MicrophoneSelector.SelectedItem != null;
        StopMicButton.IsEnabled = engine.HasMicrophone;
        StartMicButton.Visibility = engine.HasMicrophone ? Visibility.Collapsed : Visibility.Visible;
        StopMicButton.Visibility = engine.HasMicrophone ? Visibility.Visible : Visibility.Collapsed;
        RouteButton.IsEnabled = engine.HasSource && !engine.Mix.SourceRouted && !sourceBusy;
        UnrouteButton.IsEnabled = engine.Mix.SourceRouted;
        RouteButton.Visibility = engine.Mix.SourceRouted ? Visibility.Collapsed : Visibility.Visible;
        UnrouteButton.Visibility = engine.Mix.SourceRouted ? Visibility.Visible : Visibility.Collapsed;
        PauseSourceButton.IsEnabled = playbackAvailable && !playbackBusy;
        SendButton.IsEnabled = !engine.IsSending;
        StopSendButton.IsEnabled = engine.IsSending;
        SendButton.Visibility = engine.IsSending ? Visibility.Collapsed : Visibility.Visible;
        StopSendButton.Visibility = engine.IsSending ? Visibility.Visible : Visibility.Collapsed;
        MicMuteButton.Content = engine.Mix.MicrophoneMuted ? "Unmute" : "Mute";
        SourceMuteButton.Content = engine.Mix.SourceMuted ? "Unmute" : "Mute";
        OutputMuteButton.Content = engine.Mix.OutputMuted ? "Unmute" : "Mute";
        synchronizingActivation = true;
        MicActiveSwitch.IsChecked = engine.HasMicrophone && !engine.Mix.MicrophoneMuted;
        SourceActiveSwitch.IsChecked = engine.Mix.SourceRouted;
        SourceActiveSwitch.IsEnabled = engine.HasSource && !sourceBusy;
        OutputActiveSwitch.IsChecked = engine.IsSending;
        synchronizingActivation = false;
    }
    private void MicActiveChanged(object sender, RoutedEventArgs e)
    {
        if (!ready || synchronizingActivation) return;
        Execute(() =>
        {
            if (MicActiveSwitch.IsChecked == true && !engine.HasMicrophone && MicrophoneSelector.SelectedItem is AudioDevice microphone)
                engine.StartMicrophone(microphone.Id);
            engine.Mix.MicrophoneMuted = MicActiveSwitch.IsChecked != true;
        });
    }
    private void SourceActiveChanged(object sender, RoutedEventArgs e)
    {
        if (!ready || synchronizingActivation) return;
        if (SourceActiveSwitch.IsChecked == true) RouteClick(sender, e);
        else UnrouteClick(sender, e);
    }
    private void OutputActiveChanged(object sender, RoutedEventArgs e)
    {
        if (!ready || synchronizingActivation) return;
        if (OutputActiveSwitch.IsChecked == true) SendClick(sender, e);
        else StopSendClick(sender, e);
    }
    private void AddSourceClick(object sender, RoutedEventArgs e)
    {
        var source = new SourceSettings(); settings.ExtraSources.Add(source);
        var card = AddSourceCard(source); TrySave();
        Dispatcher.BeginInvoke(() => card.BringIntoView());
        StatusText.Text = "Choose an audio source in the new card, then turn on its Active switch.";
    }
    private AdditionalSourceCard AddSourceCard(SourceSettings source)
    {
        AdditionalSourceCard? card = null;
        card = new AdditionalSourceCard(engine, catalog, source, selected =>
        {
            if (selected == null) return null;
            if (selected.Kind == AudioSourceKind.Input && selected.Id == (MicrophoneSelector.SelectedItem as AudioDevice)?.Id)
                return "That input is already your voice microphone. Choose another input.";
            if (selected.Id == (SourceSelector.SelectedItem as AudioSource)?.Id || additionalCards.Any(c => c != card && c.SelectedSource?.Id == selected.Id))
                return "That source is already selected in another card. Choose a different source.";
            return null;
        });
        card.Changed += TrySave;
        card.Status += message => StatusText.Text = message;
        card.RemoveRequested += removed =>
        {
            removed.DisposeSource(); additionalCards.Remove(removed); AdditionalSourcesPanel.Children.Remove(removed);
            settings.ExtraSources.Remove(source);
            for (var i = 0; i < additionalCards.Count; i++) additionalCards[i].SetNumber(i + 2);
            TrySave(); StatusText.Text = "Source removed. Your other sources continue playing.";
        };
        additionalCards.Add(card); card.SetNumber(additionalCards.Count + 1); AdditionalSourcesPanel.Children.Add(card);
        return card;
    }
    private void StartMicrophoneClick(object sender, RoutedEventArgs e) { if (MicrophoneSelector.SelectedItem is AudioDevice selected) Execute(() => engine.StartMicrophone(selected.Id)); }
    private void StopMicrophoneClick(object sender, RoutedEventArgs e) { engine.StopMicrophone(); StatusText.Text = "Microphone capture stopped. Other programs can still use your hardware microphone."; UpdateControls(); }
    private void MicMuteClick(object sender, RoutedEventArgs e) { engine.Mix.MicrophoneMuted = !engine.Mix.MicrophoneMuted; UpdateControls(); }
    private void SourceMuteClick(object sender, RoutedEventArgs e) { engine.Mix.SourceMuted = !engine.Mix.SourceMuted; UpdateControls(); }
    private void OutputMuteClick(object sender, RoutedEventArgs e) { engine.Mix.OutputMuted = !engine.Mix.OutputMuted; UpdateControls(); }
    private void RouteClick(object sender, RoutedEventArgs e) => Execute(() => { engine.RouteSource(); StatusText.Text = $"Selected source and microphone are being sent to {ProductInfo.MicrophoneName}."; });
    private void UnrouteClick(object sender, RoutedEventArgs e) { engine.UnrouteSource(); StatusText.Text = "Source removed from the mix. Your microphone keeps working; source preview stays available."; UpdateControls(); }
    private async void PauseSourceClick(object sender, RoutedEventArgs e)
    {
        if (SourceSelector.SelectedItem is not AudioSource { Kind: AudioSourceKind.Program })
        {
            StatusText.Text = "Playback controls are available for program audio sources.";
            return;
        }
        try
        {
            var succeeded = await SourcePlayback.ToggleAsync(SourceSelector.SelectedItem as AudioSource);
            StatusText.Text = succeeded ? "Playback changed in the selected program." : "This program did not accept the playback command.";
            lastPlaybackCheck = 0;
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void SendClick(object sender, RoutedEventArgs e) => Execute(() => { engine.StartSending(); StatusText.Text = $"Sending to {ProductInfo.MicrophoneName}. Choose it as the microphone in any app."; });
    private void StopSendClick(object sender, RoutedEventArgs e) { engine.StopSending(); StatusText.Text = "Sending stopped. Source previews remain available."; UpdateControls(); }
    private void RefreshClick(object sender, RoutedEventArgs e) { if (!sourceBusy) RefreshLists(); }
    private void GainChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (ready) { ApplyLevels(); TrySave(); } }
    private void ApplyLevels()
    {
        settings.MicrophoneLevelDb = MicGain.Value; settings.SourceLevelDb = SourceGain.Value; settings.MasterLevelDb = MasterGain.Value;
        engine.Mix.MicrophoneGain = MixProvider.DecibelsToGain(MicGain.Value); engine.Mix.SourceGain = MixProvider.DecibelsToGain(SourceGain.Value); engine.Mix.MasterGain = MixProvider.DecibelsToGain(MasterGain.Value);
        MicGainText.Text = $"{MicGain.Value:0} dB"; SourceGainText.Text = $"{SourceGain.Value:0} dB"; MasterGainText.Text = $"{MasterGain.Value:0} dB";
    }
    private void Execute(Action action) { try { action(); } catch (Exception ex) { ShowError(ex); } UpdateControls(); }
    private void ShowError(Exception ex) { AppLog.Write(ex); StatusText.Text = ex.Message; }
    private void TrySave() { try { store.Save(settings); } catch (Exception ex) { ShowError(ex); } }
    private void UpdateWindowChrome()
    {
        if (!IsLoaded) return;
        // Match the native draggable title area to the scaled visual header.
        // Windows then handles drag, double-click, edge resize and snapping.
        var chrome = WindowChrome.GetWindowChrome(this);
        if (chrome != null)
            chrome.CaptionHeight = Math.Max(28, WindowHeader.TransformToAncestor(this).Transform(new Point(0, WindowHeader.ActualHeight)).Y);
    }
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeClick(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }
    private async void SettingsClick(object sender, RoutedEventArgs e)
    {
        if (setupBusy) return;
        if (engine.Endpoints == null)
        {
            if (VirtualMicrophoneSetup.FindUsbip() == null)
            {
                if (!AskToInstallMicrophone()) return;
            }
            await SetupMicrophoneAsync(true);
            return;
        }
        Execute(() => Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true }));
    }

    private bool AskToInstallMicrophone() => MessageBox.Show(this,
        "MicWeave needs to install a free, already-signed USBip component once. Windows will ask for administrator approval. You do not need VoiceMeeter, a signing certificate, or changes to Secure Boot.\n\nSave your work first: USB devices such as your mouse, keyboard, audio interface, or external drive can disconnect briefly. Stop file transfers and calls before continuing. A restart may be needed.\n\nInstall the microphone component now?",
        "Set up MicWeave Microphone", MessageBoxButton.OKCancel, MessageBoxImage.Information, MessageBoxResult.Cancel) == MessageBoxResult.OK;

    private async Task SetupMicrophoneAsync(bool allowInstall)
    {
        if (setupBusy) return;
        setupBusy = true;
        setupHeading = "Setting up microphone…";
        StatusText.Text = allowInstall ? "Setting up MicWeave Microphone…" : "Connecting MicWeave Microphone…";
        try
        {
            var result = allowInstall
                ? await VirtualMicrophoneSetup.InstallAndAttachAsync(windowClosed.Token)
                : await VirtualMicrophoneSetup.AttachExistingAsync(windowClosed.Token);
            if (!ready) return;
            StatusText.Text = result.Message;
            if (result.Outcome == SetupOutcome.RestartRequired)
            {
                setupHeading = "Restart Windows to finish setup";
                MessageBox.Show(this, result.Message, ProductInfo.Name, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (result.Outcome == SetupOutcome.Cancelled)
            {
                setupHeading = "Microphone setup cancelled";
                return;
            }
            for (var i = 0; i < 50 && ready && engine.Endpoints == null; i++)
            {
                await Task.Delay(200, windowClosed.Token);
                if (ready) engine.RefreshOutput();
            }
            if (!ready) return;
            if (engine.Endpoints == null)
                throw new InvalidOperationException("Windows has not made MicWeave Microphone available yet. Save your work, restart Windows, and reopen MicWeave. Your existing microphone and speakers have not been replaced.");
            engine.StartSending();
            setupHeading = null;
            StatusText.Text = ProductInfo.OutputInstructions;
        }
        catch (OperationCanceledException) when (!ready) { }
        catch (Exception ex)
        {
            if (ready) { setupHeading = "Microphone setup needs attention"; ShowError(ex); }
        }
        finally { setupBusy = false; if (ready) UpdateControls(); }
    }
    private void CloseClick(object sender, RoutedEventArgs e) => Close();

}
