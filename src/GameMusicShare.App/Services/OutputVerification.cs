using System.Diagnostics;
using GameMusicShare.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace GameMusicShare.Services;

internal static class OutputVerification
{
    public static async Task<int> RunAsync(string[] args)
    {
        var results = new List<object>();
        try
        {
            using var transport = new VirtualMicrophoneOutput();
            transport.Discover();
            await VirtualMicrophoneSetup.AttachExistingAsync();
            var endpoint = transport.Discover() ?? throw new InvalidOperationException("MicWeave microphone is missing.");
            MicrophoneProperties.DisableEffects(endpoint.CaptureId);
            await Task.Delay(400);
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDevice(endpoint.CaptureId);
            using var capture = new WasapiCapture(device, true, 30);
            var chunks = new List<float>();
            var gate = new object();
            capture.DataAvailable += (_, e) =>
            {
                lock (gate)
                {
                    var bytesPerSample = capture.WaveFormat.BitsPerSample / 8;
                    for (var i = 0; i + capture.WaveFormat.BlockAlign <= e.BytesRecorded; i += capture.WaveFormat.BlockAlign)
                        chunks.Add(bytesPerSample == 4 ? BitConverter.ToSingle(e.Buffer, i) : BitConverter.ToInt16(e.Buffer, i) / 32768f);
                }
            };
            Exception? captureError = null;
            capture.RecordingStopped += (_, e) => captureError = e.Exception;
            var mix = new MixProvider { Microphone = new Tone(440), Source = new Tone(880), SourceRouted = true, SourceGain = 1 };
            capture.StartRecording();
            transport.Start(endpoint, new PcmOutputProvider(mix));
            async Task<bool> Check(string name, bool expectVoice, bool expectProgram, bool expectExtra = false)
            {
                await Task.Delay(700);
                lock (gate) chunks.Clear();
                var clock = Stopwatch.StartNew();
                await Task.Delay(1700);
                float[] samples;
                lock (gate) samples = chunks.ToArray();
                var rms = samples.Length == 0 ? 0 : Math.Sqrt(samples.Average(s => (double)s * s));
                var voice = Magnitude(samples, capture.WaveFormat.SampleRate, 440);
                var program = Magnitude(samples, capture.WaveFormat.SampleRate, 880);
                var extra = Magnitude(samples, capture.WaveFormat.SampleRate, 1320);
                var passed = captureError == null && samples.Length > capture.WaveFormat.SampleRate &&
                    (expectVoice ? voice > .03 : voice < .004) && (expectProgram ? program > .03 : program < .004) &&
                    (expectExtra ? extra > .03 : extra < .004) &&
                    (expectVoice || expectProgram || expectExtra || rms < .0001);
                results.Add(new { name, passed, samples = samples.Length, elapsedSeconds = clock.Elapsed.TotalSeconds,
                    sampleRate = capture.WaveFormat.SampleRate, rms, voice440Hz = voice, program880Hz = program, extra1320Hz = extra, error = captureError?.Message });
                return passed;
            }
            var passed = await Check("Both tones survive the Windows microphone", true, true);
            var extraSource = mix.AddSource(); extraSource.Samples = new Tone(1320); extraSource.Gain = 1; extraSource.Enabled = true;
            passed &= await Check("Microphone and two program sources reach Windows together", true, true, true);
            extraSource.Enabled = false;
            passed &= await Check("Deactivating an extra source preserves the other two", true, true);
            extraSource.Enabled = true; mix.RemoveSource(extraSource);
            passed &= await Check("Removing an active source preserves the other two", true, true);
            mix.SourceRouted = false;
            passed &= await Check("Unroute removes program but keeps microphone", true, false);
            mix.SourceRouted = true; mix.MicrophoneMuted = true;
            passed &= await Check("Microphone mute keeps program audio", false, true);
            mix.MicrophoneMuted = false; mix.OutputMuted = true;
            passed &= await Check("Master mute produces silence", false, false);
            mix.OutputMuted = false; mix.SourceMuted = true;
            passed &= await Check("Source mute keeps microphone", true, false);
            transport.Stop();
            capture.StopRecording();
            Diagnostics.WriteResult(args, new { passed, endpoint, captureFormat = capture.WaveFormat.ToString(), tests = results });
            return passed ? 0 : 1;
        }
        catch (Exception ex) { Diagnostics.WriteResult(args, new { passed = false, error = ex.ToString(), tests = results }); return 1; }
    }

    public static async Task<int> LiveAsync(string[] args)
    {
        AudioSource? spotify = null;
        var restorePause = false;
        try
        {
            var catalog = new AudioCatalog();
            spotify = catalog.GetSources().FirstOrDefault(s => s.Kind == AudioSourceKind.Program && s.Name.StartsWith("Spotify", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Open Spotify with a playable track before this test.");
            var micArg = Array.IndexOf(args, "--microphone");
            var micName = micArg >= 0 && micArg + 1 < args.Length ? args[micArg + 1] : null;
            var mic = catalog.GetMicrophones().FirstOrDefault(m => micName == null
                ? m.Id == catalog.GetDefaultMicrophoneId() : m.Name.Contains(micName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("The selected or default microphone is unavailable.");
            using var engine = new AudioEngine();
            await VirtualMicrophoneSetup.AttachExistingAsync();
            engine.RefreshOutput();
            if (engine.Endpoints == null) throw new InvalidOperationException("MicWeave microphone missing after attach.");
            engine.StartMicrophone(mic.Id);
            await engine.SelectSourceAsync(spotify);
            engine.Mix.SourceGain = 1;
            engine.RouteSource();
            var state = await SourcePlayback.StateAsync(spotify);
            if (!state.Playing)
            {
                restorePause = await SourcePlayback.SetPlayingAsync(spotify, true);
                if (!restorePause) throw new InvalidOperationException("Spotify did not start playback. Play a track and repeat the test.");
            }
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDevice(engine.Endpoints.CaptureId);
            using var capture = new WasapiCapture(device, true, 30);
            using var speakers = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            using var desktop = new WasapiLoopbackCapture(speakers);
            double outputPeak = 0, desktopPeak = 0, micPeak = 0, sourcePeak = 0;
            long outputFrames = 0, desktopFrames = 0;
            Exception? error = null;
            void Measure(WaveInEventArgs e, WaveFormat format, ref double peak, ref long frames)
            {
                for (var i = 0; i + format.BlockAlign <= e.BytesRecorded; i += format.BlockAlign)
                {
                    var sample = format.BitsPerSample == 32 ? BitConverter.ToSingle(e.Buffer, i) : BitConverter.ToInt16(e.Buffer, i) / 32768f;
                    peak = Math.Max(peak, Math.Abs(sample)); frames++;
                }
            }
            capture.DataAvailable += (_, e) => Measure(e, capture.WaveFormat, ref outputPeak, ref outputFrames);
            desktop.DataAvailable += (_, e) => Measure(e, desktop.WaveFormat, ref desktopPeak, ref desktopFrames);
            capture.RecordingStopped += (_, e) => error ??= e.Exception;
            desktop.RecordingStopped += (_, e) => error ??= e.Exception;
            capture.StartRecording(); desktop.StartRecording();
            for (var i = 0; i < 100; i++)
            {
                await Task.Delay(100);
                micPeak = Math.Max(micPeak, engine.Mix.MicrophoneMeter.Snapshot().Peak);
                sourcePeak = Math.Max(sourcePeak, engine.Mix.SourceMeter.Snapshot().Peak);
            }
            capture.StopRecording(); desktop.StopRecording();
            var passed = error == null && outputFrames > 96000 && desktopFrames > 96000 && sourcePeak > .001 && outputPeak > .001 && desktopPeak > .001 && engine.HasMicrophone;
            Diagnostics.WriteResult(args, new { passed, microphone = mic.Name, program = spotify.Name, speakers = speakers.FriendlyName,
                output = engine.Endpoints, micPeak, sourcePeak, outputPeak, desktopPeak, outputFrames, desktopFrames,
                mediaControls = state.Available, error = error?.Message });
            return passed ? 0 : 1;
        }
        catch (Exception ex) { Diagnostics.WriteResult(args, new { passed = false, error = ex.ToString() }); return 1; }
        finally { if (restorePause) await SourcePlayback.SetPlayingAsync(spotify, false); }
    }

    private static double Magnitude(float[] samples, int rate, int frequency)
    {
        if (samples.Length == 0) return 0;
        // Use short windows to tolerate scheduler gaps while detecting only
        // the actual expected frequencies in the captured microphone stream.
        var size = rate / 10;
        double total = 0; var windows = 0;
        for (var start = 0; start + size <= samples.Length; start += size)
        {
            double re = 0, im = 0;
            for (var i = 0; i < size; i++)
            {
                var phase = 2 * Math.PI * frequency * i / rate;
                re += samples[start + i] * Math.Cos(phase); im += samples[start + i] * Math.Sin(phase);
            }
            total += 2 * Math.Sqrt(re * re + im * im) / size; windows++;
        }
        return windows == 0 ? 0 : total / windows;
    }

    private sealed class Tone(int frequency) : ISampleProvider
    {
        private long frame;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public int Read(float[] buffer, int offset, int count)
        {
            for (var i = 0; i < count; i += 2)
            {
                var sample = .12f * (float)Math.Sin(2 * Math.PI * frequency * frame++ / 48000d);
                buffer[offset + i] = sample; buffer[offset + i + 1] = sample;
            }
            return count;
        }
    }
}
