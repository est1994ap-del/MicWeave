using System.IO;
using System.Text.Json;
using GameMusicShare.Audio;

namespace GameMusicShare.Services;

internal static class Diagnostics
{
    public static async Task<int> RunAsync(string[] args)
    {
        var catalog = new AudioCatalog();
        var results = new List<object>();
        try
        {
            var microphones = catalog.GetMicrophones(); var sources = catalog.GetSources();
            using var engine = new AudioEngine();
            foreach (var microphone in microphones.Where(m => !m.Name.Contains("Voicemeeter", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    engine.StartMicrophone(microphone.Id); await Task.Delay(1200);
                    var signal = engine.Mix.MicrophoneMeter.Snapshot();
                    results.Add(new { kind = "microphone", name = microphone.Name, captureStarted = true, signal.Peak, signal.HasSignal, error = (string?)null });
                }
                catch (Exception ex) { results.Add(new { kind = "microphone", name = microphone.Name, captureStarted = false, Peak = 0f, HasSignal = false, error = ex.ToString() }); }
            }
            engine.StopMicrophone();
            var program = sources.FirstOrDefault(s => s.Kind == AudioSourceKind.Program && s.Name.Contains("Spotify", StringComparison.OrdinalIgnoreCase)) ?? sources.FirstOrDefault(s => s.Kind == AudioSourceKind.Program);
            if (program != null)
            {
                try { await engine.SelectSourceAsync(program); await Task.Delay(1600); var signal = engine.Mix.SourceMeter.Snapshot(); results.Add(new { kind = "program", name = program.Name, captureStarted = true, signal.Peak, signal.HasSignal, error = (string?)null }); }
                catch (Exception ex) { results.Add(new { kind = "program", name = program.Name, captureStarted = false, Peak = 0f, HasSignal = false, error = ex.ToString() }); }
            }
            WriteResult(args, new { microphones, sources, virtualMicrophone = engine.Endpoints, captureChecks = results, operatingSystem = Environment.OSVersion.ToString() });
            return 0;
        }
        catch (Exception ex) { WriteResult(args, new { error = ex.ToString(), captureChecks = results }); return 1; }
    }
    public static void WriteResult(string[] args, object result)
    {
        var text = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        var index = Array.IndexOf(args, "--output");
        var path = index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.Combine(AppContext.BaseDirectory, "diagnostics.json");
        File.WriteAllText(path, text);
    }
}
