using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using GameMusicShare.Services;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace GameMusicShare.Audio;

public sealed record VirtualMicrophoneEndpoints(string CaptureId, string CaptureName);

public interface IVirtualMicrophoneOutput : IDisposable
{
    VirtualMicrophoneEndpoints? Discover();
    bool IsSending { get; }
    event Action<Exception>? Faulted;
    void Start(VirtualMicrophoneEndpoints endpoints, IWaveProvider samples);
    void Stop();
}

/// <summary>
/// Streams the final mix directly into MicWeave's capture-only USB Audio Class
/// device. Windows supplies its built-in USB audio driver, so there is no
/// private render endpoint and no MicWeave kernel-audio driver.
/// </summary>
public sealed class VirtualMicrophoneOutput : IVirtualMicrophoneOutput
{
    private readonly HelperSession helper = new();
    private readonly object gate = new();
    private CancellationTokenSource? stop;
    private Task? pump;
    private TcpClient? client;
    private bool disposed;
    private string? configuredEndpoint;

    public bool IsSending
    {
        get { lock (gate) return pump is { IsCompleted: false }; }
    }

    public event Action<Exception>? Faulted;

    public VirtualMicrophoneEndpoints? Discover()
    {
        if (disposed) return null;
        helper.EnsureStarted();
        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            using (device)
            {
                if (EndpointIdentity.IsMicWeave(device))
                {
                    if (configuredEndpoint != device.ID)
                    {
                        MicrophoneProperties.DisableEffects(device.ID);
                        configuredEndpoint = device.ID;
                    }
                    return new(device.ID, ProductInfo.MicrophoneName);
                }
            }
        }
        return null;
    }

    public void Start(VirtualMicrophoneEndpoints endpoints, IWaveProvider samples)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Stop();
        helper.EnsureStarted();

        var nextClient = helper.Connect();
        var nextStop = new CancellationTokenSource();
        var stream = nextClient.GetStream();

        lock (gate)
        {
            client = nextClient;
            stop = nextStop;
            pump = Task.Run(() => PumpAsync(samples, stream, nextStop.Token));
        }
    }

    private async Task PumpAsync(IWaveProvider samples, NetworkStream stream, CancellationToken token)
    {
        // 10 ms of 48 kHz stereo PCM16. This clock drives the mixer and the
        // USB capture ring at real-time speed.
        var buffer = new byte[1920];
        var clock = Stopwatch.StartNew();
        long blocksWritten = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                // Windows may wake a 10 ms timer after 15.6 ms. Count frames
                // from elapsed time so a late wake never slows down the audio.
                // A 30 ms lead absorbs ordinary scheduling jitter.
                var dueBlocks = (long)(clock.Elapsed.TotalMilliseconds / 10) + 3;
                if (dueBlocks - blocksWritten > 20) blocksWritten = dueBlocks - 3;
                while (blocksWritten < dueBlocks)
                {
                    var count = samples.Read(buffer, 0, buffer.Length);
                    if (count < buffer.Length) Array.Clear(buffer, count, buffer.Length - count);
                    await stream.WriteAsync(buffer.AsMemory(0, buffer.Length), token);
                    blocksWritten++;
                }
                await Task.Delay(2, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            Faulted?.Invoke(ex);
        }
    }

    public void Stop()
    {
        CancellationTokenSource? previousStop;
        TcpClient? previousClient;
        Task? previousPump;
        lock (gate)
        {
            previousStop = stop;
            previousClient = client;
            previousPump = pump;
            stop = null;
            client = null;
            pump = null;
        }
        previousStop?.Cancel();
        previousClient?.Dispose();
        try { previousPump?.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        previousStop?.Dispose();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Stop();
        helper.Dispose();
    }
}
