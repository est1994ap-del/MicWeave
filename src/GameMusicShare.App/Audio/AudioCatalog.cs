using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace GameMusicShare.Audio;

public sealed class AudioCatalog
{
    public IReadOnlyList<AudioDevice> GetMicrophones()
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
        var result = new List<AudioDevice>();
        foreach (var device in devices)
        {
            using (device) if (!EndpointIdentity.IsMicWeave(device)) result.Add(new(device.ID, device.FriendlyName));
        }
        return result.OrderBy(d => d.Name).ToArray();
    }
    public string? GetDefaultMicrophoneId()
    {
        try { using var enumerator = new MMDeviceEnumerator(); using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications); return device.ID; }
        catch { return null; }
    }
    public IReadOnlyList<AudioSource> GetSources()
    {
        var result = new List<AudioSource>();
        var processIds = new HashSet<int>();
        using var enumerator = new MMDeviceEnumerator();
        var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        foreach (var device in endpoints)
        {
            using (device)
            {
                result.Add(new(device.ID, device.FriendlyName + " (all audio)", AudioSourceKind.Playback));
                try
                {
                    var sessions = device.AudioSessionManager.Sessions;
                    for (var i = 0; i < sessions.Count; i++)
                    {
                        using var session = sessions[i];
                        var pid = (int)session.GetProcessID;
                        if (pid > 0) processIds.Add(pid);
                    }
                }
                catch { /* A device may be unplugged during enumeration. */ }
            }
        }
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try { if (process.MainWindowHandle != IntPtr.Zero) processIds.Add(process.Id); }
                catch { }
            }
        }
        var roots = ProcessTree.GetParents();
        var seen = new HashSet<int>();
        foreach (var pid in processIds)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                if (process.Id == Environment.ProcessId) continue;
                var root = ProcessTree.FindSameApplicationRoot(pid, process.ProcessName, roots);
                if (!seen.Add(root)) continue;
                using var app = Process.GetProcessById(root);
                var title = string.IsNullOrWhiteSpace(app.MainWindowTitle) ? app.ProcessName : app.MainWindowTitle;
                if (title.Length > 70) title = title[..67] + "…";
                result.Add(new($"process:{root}", $"{app.ProcessName} — {title}", AudioSourceKind.Program, root));
            }
            catch { }
        }
        result.AddRange(GetMicrophones().Select(d => new AudioSource(d.Id, d.Name, AudioSourceKind.Input)));
        return result.OrderBy(s => s.Kind).ThenBy(s => s.Name).ToArray();
    }
}
