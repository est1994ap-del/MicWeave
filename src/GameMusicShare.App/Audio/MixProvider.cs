using NAudio.Wave;

namespace GameMusicShare.Audio;

public sealed class SourceMix
{
    public ISampleProvider? Samples { get; set; }
    public SpectrumAnalyzer Meter { get; } = new();
    public bool Enabled { get; set; }
    public bool Muted { get; set; }
    public float Gain { get; set; } = .2f;
    internal float[] Buffer = [];
    internal float PreviousGain;
    internal float EffectiveGain => Enabled && !Muted ? Gain : 0;
}

/// <summary>Pull mixer. Source taps are before gain/mute; final tap is after PCM16 quantization.</summary>
public sealed class MixProvider : ISampleProvider
{
    public object Gate { get; } = new();
    public ISampleProvider? Microphone { get; set; }
    public SourceMix PrimarySource { get; } = new();
    private readonly List<SourceMix> additionalSources = [];
    public ISampleProvider? Source { get => PrimarySource.Samples; set => PrimarySource.Samples = value; }
    public SpectrumAnalyzer MicrophoneMeter { get; } = new();
    public SpectrumAnalyzer SourceMeter => PrimarySource.Meter;
    public SpectrumAnalyzer OutputMeter { get; } = new();
    public bool MicrophoneMuted { get; set; }
    public bool SourceMuted { get => PrimarySource.Muted; set => PrimarySource.Muted = value; }
    public bool OutputMuted { get; set; }
    public bool SourceRouted { get => PrimarySource.Enabled; set => PrimarySource.Enabled = value; }
    public float MicrophoneGain { get; set; } = 1;
    public float SourceGain { get => PrimarySource.Gain; set => PrimarySource.Gain = value; }
    public float MasterGain { get; set; } = 1;
    public long LastLimitedAt { get; private set; }
    private float[] microphoneSamples = [];
    private float previousMicGain = 1, previousMasterGain = 1;
    public SourceMix AddSource() { lock (Gate) { var source = new SourceMix(); additionalSources.Add(source); return source; } }
    public void RemoveSource(SourceMix source) { lock (Gate) { additionalSources.Remove(source); } }
    public bool ContainsSource(SourceMix source) { lock (Gate) return source == PrimarySource || additionalSources.Contains(source); }
    private static void ReadSource(SourceMix source, int count)
    {
        if (source.Buffer.Length < count) source.Buffer = new float[count];
        Array.Clear(source.Buffer, 0, count);
        source.Samples?.Read(source.Buffer, 0, count);
        if (source.Samples != null) source.Meter.Add(source.Buffer, 0, count);
    }
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    public int Read(float[] buffer, int offset, int count)
    {
        lock (Gate)
        {
            if (microphoneSamples.Length < count) microphoneSamples = new float[count];
            Array.Clear(microphoneSamples, 0, count);
            Microphone?.Read(microphoneSamples, 0, count);
            ReadSource(PrimarySource, count);
            foreach (var source in additionalSources) ReadSource(source, count);
            if (Microphone != null) MicrophoneMeter.Add(microphoneSamples, 0, count);
            var micGain = MicrophoneMuted ? 0 : MicrophoneGain;
            var masterGain = OutputMuted ? 0 : MasterGain;
            for (var i = 0; i < count; i += 2)
            {
                // A short, frame-aligned ramp avoids clicks when routing or muting live audio.
                var proportion = Math.Min(1f, (i / 2 + 1) / 240f);
                var mg = previousMicGain + (micGain - previousMicGain) * proportion;
                var sg = PrimarySource.PreviousGain + (PrimarySource.EffectiveGain - PrimarySource.PreviousGain) * proportion;
                var master = previousMasterGain + (masterGain - previousMasterGain) * proportion;
                for (var channel = 0; channel < 2 && i + channel < count; channel++)
                {
                    var sample = microphoneSamples[i + channel] * mg + PrimarySource.Buffer[i + channel] * sg;
                    foreach (var source in additionalSources)
                        sample += source.Buffer[i + channel] * (source.PreviousGain + (source.EffectiveGain - source.PreviousGain) * proportion);
                    sample *= master;
                    if (Math.Abs(sample) > 0.98f) LastLimitedAt = Environment.TickCount64;
                    buffer[offset + i + channel] = Math.Clamp(sample, -0.98f, 0.98f);
                }
            }
            previousMicGain = micGain; PrimarySource.PreviousGain = PrimarySource.EffectiveGain; previousMasterGain = masterGain;
            foreach (var source in additionalSources) source.PreviousGain = source.EffectiveGain;
            return count;
        }
    }
    public static float DecibelsToGain(double db) => (float)Math.Pow(10, db / 20);
}

/// <summary>The exact signed 16-bit stream passed to the driver, with an identical post-conversion meter tap.</summary>
internal sealed class PcmOutputProvider(MixProvider mix) : IWaveProvider
{
    private float[] samples = [];
    public WaveFormat WaveFormat { get; } = new(48000, 16, 2);
    public int Read(byte[] buffer, int offset, int count)
    {
        var sampleCount = count / 4 * 2;
        if (samples.Length < sampleCount) samples = new float[sampleCount];
        mix.Read(samples, 0, sampleCount);
        for (var i = 0; i < sampleCount; i++)
        {
            var sample = (short)Math.Clamp((int)Math.Round(samples[i] * 32767f), short.MinValue, short.MaxValue);
            buffer[offset + i * 2] = (byte)sample;
            buffer[offset + i * 2 + 1] = (byte)(sample >> 8);
            samples[i] = sample / 32768f;
        }
        mix.OutputMeter.Add(samples, 0, sampleCount);
        return sampleCount * 2;
    }
}
