using NAudio.Dsp;

namespace GameMusicShare.Audio;

/// <summary>Real signal analysis only: no generated or decorative activity.</summary>
public sealed class SpectrumAnalyzer
{
    private const int Size = 2048;
    private readonly Complex[] fft = new Complex[Size];
    private readonly float[] bars = new float[28];
    private readonly object gate = new();
    private int position;
    private float peak;
    private long lastSignal;
    public void Add(float[] samples, int offset, int count, int channels = 2)
    {
        lock (gate)
        {
            for (var i = offset; i + channels <= offset + count; i += channels)
            {
                var mono = 0f;
                for (var c = 0; c < channels; c++) { var sample = samples[i + c]; mono += sample; peak = Math.Max(peak, Math.Abs(sample)); }
                mono /= channels;
                fft[position].X = mono * (float)FastFourierTransform.HammingWindow(position, Size);
                fft[position].Y = 0;
                if (++position < Size) continue;
                position = 0;
                FastFourierTransform.FFT(true, 11, fft);
                for (var band = 0; band < bars.Length; band++)
                {
                    var low = Math.Max(1, (int)(45 * Math.Pow(18000.0 / 45, band / (double)bars.Length) * Size / 48000));
                    var high = Math.Min(Size / 2, Math.Max(low + 1, (int)(45 * Math.Pow(18000.0 / 45, (band + 1.0) / bars.Length) * Size / 48000)));
                    var magnitude = 0f;
                    for (var bin = low; bin < high; bin++) magnitude = Math.Max(magnitude, MathF.Sqrt(fft[bin].X * fft[bin].X + fft[bin].Y * fft[bin].Y));
                    bars[band] = Math.Clamp((20 * MathF.Log10(Math.Max(0.000001f, magnitude)) + 72) / 60, 0, 1);
                }
                if (peak > 0.001f) lastSignal = Environment.TickCount64;
            }
        }
    }
    public SpectrumSnapshot Snapshot()
    {
        lock (gate)
        {
            var snapshot = new SpectrumSnapshot((float[])bars.Clone(), peak, Environment.TickCount64 - lastSignal < 350);
            peak = 0;
            return snapshot;
        }
    }
    public void Reset() { lock (gate) { Array.Clear(bars); Array.Clear(fft); peak = 0; position = 0; lastSignal = 0; } }
}
public sealed record SpectrumSnapshot(float[] Bars, float Peak, bool HasSignal);
