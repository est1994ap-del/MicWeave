using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GameMusicShare.Audio;

/// <summary>One shared-mode input; another application can use the same microphone simultaneously.</summary>
public sealed class CaptureInput : IDisposable
{
    private readonly IWaveIn capture;
    private readonly BufferedWaveProvider buffer;
    private readonly MMDevice? device;
    private volatile bool disposing;
    public bool IsRunning { get; private set; }
    public ISampleProvider Samples { get; }
    public event Action<Exception>? Faulted;
    private CaptureInput(IWaveIn capture, MMDevice? device = null)
    {
        this.capture = capture;
        this.device = device;
        buffer = new BufferedWaveProvider(capture.WaveFormat) { BufferDuration = TimeSpan.FromMilliseconds(250), DiscardOnBufferOverflow = true, ReadFully = true };
        ISampleProvider samples = buffer.ToSampleProvider();
        if (samples.WaveFormat.Channels == 1) samples = new MonoToStereoSampleProvider(samples);
        else if (samples.WaveFormat.Channels != 2) samples = new FirstTwoChannels(samples);
        if (samples.WaveFormat.SampleRate != VirtualDeviceContract.SampleRate) samples = new WdlResamplingSampleProvider(samples, VirtualDeviceContract.SampleRate);
        Samples = samples;
        capture.DataAvailable += (_, args) =>
        {
            if (disposing) return;
            // Drop stale backlog after a scheduling interruption instead of replaying delayed speech.
            if (buffer.BufferedDuration.TotalMilliseconds > 150) buffer.ClearBuffer();
            buffer.AddSamples(args.Buffer, 0, args.BytesRecorded);
        };
        capture.RecordingStopped += (_, args) => { IsRunning = false; if (!disposing && args.Exception != null) Faulted?.Invoke(args.Exception); };
    }
    public static CaptureInput OpenDevice(string endpointId, bool playback)
    {
        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDevice(endpointId);
        try
        {
            if (EndpointIdentity.IsMicWeave(device)) throw new InvalidOperationException("The virtual microphone cannot be selected as its own source.");
            IWaveIn capture = playback ? new WasapiLoopbackCapture(device) : new WasapiCapture(device, true, 30);
            var input = new CaptureInput(capture, device);
            try { capture.StartRecording(); input.IsRunning = true; return input; } catch { input.Dispose(); throw; }
        }
        catch { device.Dispose(); throw; }
    }
    public static async Task<CaptureInput> OpenProcessAsync(int processId)
    {
        var capture = await ProcessLoopbackCapture.CreateAsync(processId);
        var input = new CaptureInput(capture);
        try { capture.StartRecording(); input.IsRunning = true; return input; } catch { input.Dispose(); throw; }
    }
    public void Dispose()
    {
        if (disposing) return;
        disposing = true; IsRunning = false;
        capture.StopRecording();
        capture.Dispose();
        device?.Dispose();
    }
    private sealed class FirstTwoChannels(ISampleProvider source) : ISampleProvider
    {
        private float[] scratch = [];
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
        public int Read(float[] buffer, int offset, int count)
        {
            var inputCount = count / 2 * source.WaveFormat.Channels;
            if (scratch.Length < inputCount) scratch = new float[inputCount];
            var read = source.Read(scratch, 0, inputCount);
            var frames = read / source.WaveFormat.Channels;
            for (var frame = 0; frame < frames; frame++) { buffer[offset + frame * 2] = scratch[frame * source.WaveFormat.Channels]; buffer[offset + frame * 2 + 1] = scratch[frame * source.WaveFormat.Channels + 1]; }
            return frames * 2;
        }
    }
}
