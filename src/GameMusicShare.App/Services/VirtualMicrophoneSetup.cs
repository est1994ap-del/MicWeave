using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using GameMusicShare.Audio;

namespace GameMusicShare.Services;

internal static class VirtualMicrophoneSetup
{
    private const string BundledTransportHash = "81f426741f7ee2ed991febe24a22daca8400b6ae2f171054e3fb404897e15d39";

    public static string? FindUsbip()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "USBip", "usbip.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "usbip-win2", "usbip.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "USBip", "usbip.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    public static async Task<string> InstallAndAttachAsync()
    {
        var usbip = FindUsbip();
        if (usbip == null)
        {
            var installer = Path.Combine(AppContext.BaseDirectory, "Assets", "MicWeave.UsbTransport.Setup.exe");
            if (!File.Exists(installer)) throw new FileNotFoundException("The MicWeave USB transport installer is missing.", installer);
            await using var installerStream = File.OpenRead(installer);
            var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(installerStream)).ToLowerInvariant();
            if (!actualHash.Equals(BundledTransportHash, StringComparison.Ordinal))
                throw new InvalidDataException("The bundled USB transport installer failed its security check.");

            using var setup = Process.Start(new ProcessStartInfo(installer)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RESTARTEXITCODE=3010",
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = AppContext.BaseDirectory,
            }) ?? throw new InvalidOperationException("The USB transport installer did not start.");
            await setup.WaitForExitAsync();
            if (setup.ExitCode is not (0 or 3010 or 1641))
                throw new InvalidOperationException($"The USB transport setup ended with code {setup.ExitCode}.");
            if (setup.ExitCode is 3010 or 1641)
                return "Save your work and restart Windows, then reopen MicWeave to finish connecting the microphone.";
            usbip = FindUsbip() ?? throw new InvalidOperationException("USB transport setup completed, but usbip.exe was not installed.");
        }

        return await AttachExistingAsync();
    }

    public static async Task<string> AttachExistingAsync()
    {
        var usbip = FindUsbip() ?? throw new InvalidOperationException("Click the gear button to install MicWeave Microphone.");
        // Only attach our known loopback device, and never duplicate a port.
        var list = await RunAsync(usbip, "list -r 127.0.0.1", elevated: false);
        if (!list.Contains(VirtualDeviceContract.UsbIdentity, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("MicWeave's local virtual microphone is not responding yet. Restart MicWeave and try the gear button again.");
        var ports = await RunAsync(usbip, "port", elevated: false);
        if (!ports.Contains("usbip://127.0.0.1:3240/1-1", StringComparison.OrdinalIgnoreCase))
        {
            var defaults = MicrophoneProperties.CaptureDefaults();
            await RunAsync(usbip, "attach -r 127.0.0.1 -b 1-1", elevated: false);
            // Endpoint creation is asynchronous. Restore any existing hardware defaults.
            for (var i = 0; i < 12; i++)
            {
                await Task.Delay(200);
                MicrophoneProperties.RestoreDefaults(defaults);
            }
        }
        return "Windows attached MicWeave Microphone. It is now available in microphone lists.";
    }

    private static async Task<string> RunAsync(string executable, string arguments, bool elevated)
    {
        if (elevated) throw new ArgumentException("Use RunElevatedAsync for elevated commands.");
        using var process = Process.Start(new ProcessStartInfo(executable, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
        }) ?? throw new InvalidOperationException($"Could not start {Path.GetFileName(executable)}.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var text = (await stdout) + Environment.NewLine + (await stderr);
        if (process.ExitCode != 0) throw new InvalidOperationException(text.Trim());
        return text;
    }

    private static async Task<int> RunElevatedAsync(string executable, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(executable, arguments)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
        }) ?? throw new InvalidOperationException($"Could not start {Path.GetFileName(executable)} as administrator.");
        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}
