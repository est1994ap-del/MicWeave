using GameMusicShare.Services;

namespace GameMusicShare.Audio;

/// <summary>Owns capture lifetimes and guarantees one clock drives the mix at a time.</summary>
public sealed class AudioEngine : IDisposable
{
    private CaptureInput? microphone;
    private readonly Dictionary<SourceMix, CaptureInput> sources = [];
    private readonly IVirtualMicrophoneOutput output;
    private CancellationTokenSource? previewStop;
    private Task? previewTask;
    private bool disposed;
    public MixProvider Mix { get; } = new();
    public bool HasMicrophone => microphone?.IsRunning == true;
    public bool HasSource => IsSourceRunning(Mix.PrimarySource);
    public bool IsSourceRunning(SourceMix channel) { lock (Mix.Gate) return sources.TryGetValue(channel, out var capture) && capture.IsRunning; }
    public bool IsSending => output.IsSending;
    public VirtualMicrophoneEndpoints? Endpoints { get; private set; }
    public event Action<string>? Error;
    public AudioEngine(IVirtualMicrophoneOutput? output = null)
    {
        this.output = output ?? new VirtualMicrophoneOutput();
        this.output.Faulted += ex => { AppLog.Write(ex); Error?.Invoke("The virtual microphone disconnected. " + ex.Message); };
        RefreshOutput();
        StartPreview();
    }
    public void RefreshOutput() => Endpoints = output.Discover();
    public void StartMicrophone(string endpointId)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        StopMicrophone();
        var next = CaptureInput.OpenDevice(endpointId, false);
        next.Faulted += ex => { AppLog.Write(ex); Error?.Invoke("Microphone capture stopped. " + ex.Message); };
        lock (Mix.Gate) { microphone = next; Mix.Microphone = next.Samples; }
    }
    public void StopMicrophone()
    {
        CaptureInput? previous;
        lock (Mix.Gate) { previous = microphone; microphone = null; Mix.Microphone = null; Mix.MicrophoneMeter.Reset(); }
        previous?.Dispose();
    }
    public Task SelectSourceAsync(AudioSource? selected) => SelectSourceAsync(Mix.PrimarySource, selected);
    public async Task SelectSourceAsync(SourceMix channel, AudioSource? selected)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        StopSource(channel);
        if (selected == null) return;
        var next = selected.Kind == AudioSourceKind.Program
            ? await CaptureInput.OpenProcessAsync(selected.ProcessId)
            : CaptureInput.OpenDevice(selected.Id, selected.Kind == AudioSourceKind.Playback);
        if (disposed || !Mix.ContainsSource(channel)) { next.Dispose(); throw new ObjectDisposedException(nameof(AudioEngine)); }
        next.Faulted += ex => { AppLog.Write(ex); Error?.Invoke("Source capture stopped. " + ex.Message); };
        lock (Mix.Gate) { sources[channel] = next; channel.Samples = next.Samples; }
    }
    public void StopSource() => StopSource(Mix.PrimarySource);
    public void StopSource(SourceMix channel)
    {
        CaptureInput? previous;
        lock (Mix.Gate) { channel.Enabled = false; sources.Remove(channel, out previous); channel.Samples = null; channel.Meter.Reset(); channel.PreviousGain = 0; }
        previous?.Dispose();
    }
    public void RouteSource()
        => RouteSource(Mix.PrimarySource);
    public void RouteSource(SourceMix channel)
    {
        if (!IsSourceRunning(channel)) throw new InvalidOperationException("Choose a program or input to share first.");
        if (!IsSending) StartSending();
        channel.Enabled = true;
    }
    public void RemoveSource(SourceMix channel) { StopSource(channel); Mix.RemoveSource(channel); }
    public void UnrouteSource() => Mix.SourceRouted = false;
    public void StartSending()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (IsSending) return;
        RefreshOutput();
        if (Endpoints == null) throw new InvalidOperationException($"The {ProductInfo.MicrophoneName} is not installed. You can preview your sources, but other apps cannot receive this mix yet.");
        StopPreview();
        try { output.Start(Endpoints, new PcmOutputProvider(Mix)); }
        catch { StartPreview(); throw; }
    }
    public void StopSending()
    {
        output.Stop(); Mix.OutputMeter.Reset(); StartPreview();
    }
    public void RecoverOutputFailure()
    {
        if (!IsSending && previewTask == null) { output.Stop(); Mix.OutputMeter.Reset(); StartPreview(); }
    }
    private void StartPreview()
    {
        if (disposed || previewTask != null) return;
        previewStop = new CancellationTokenSource();
        var token = previewStop.Token;
        previewTask = Task.Run(async () =>
        {
            var samples = new float[960];
            var clock = System.Diagnostics.Stopwatch.StartNew();
            long blocks = 0;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var due = (long)(clock.Elapsed.TotalMilliseconds / 10);
                    if (due - blocks > 20) blocks = due - 1;
                    while (blocks < due) { Mix.Read(samples, 0, samples.Length); blocks++; }
                    await Task.Delay(2, token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { AppLog.Write(ex); Error?.Invoke("Audio preview stopped. " + ex.Message); }
        });
    }
    private void StopPreview()
    {
        previewStop?.Cancel(); previewTask?.GetAwaiter().GetResult(); previewTask = null; previewStop?.Dispose(); previewStop = null;
    }
    public void Dispose()
    {
        disposed = true; output.Dispose(); StopPreview(); StopMicrophone();
        foreach (var channel in sources.Keys.ToArray()) StopSource(channel);
    }
}
