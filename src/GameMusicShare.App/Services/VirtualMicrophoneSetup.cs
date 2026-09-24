using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using GameMusicShare.Audio;

namespace GameMusicShare.Services;

internal static class VirtualMicrophoneSetup
{
    private static readonly SemaphoreSlim SetupLock = new(1, 1);

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

    public static async Task<SetupResult> InstallAndAttachAsync(CancellationToken cancellation = default)
    {
        await SetupLock.WaitAsync(cancellation);
        try { return await InstallCoreAsync(cancellation); }
        finally { SetupLock.Release(); }
    }

    private static async Task<SetupResult> InstallCoreAsync(CancellationToken cancellation)
    {
        var problem = SetupPolicy.CompatibilityProblem(Environment.OSVersion.Version,
            RuntimeInformation.OSArchitecture, RuntimeInformation.ProcessArchitecture);
        if (problem != null) throw new PlatformNotSupportedException(problem);
        var usbip = FindUsbip();
        if (usbip == null)
        {
            var installer = Path.Combine(AppContext.BaseDirectory, "Assets", "MicWeave.UsbTransport.Setup.exe");
            if (!File.Exists(installer)) throw new FileNotFoundException("The MicWeave USB transport installer is missing.", installer);
            await using var installerStream = File.OpenRead(installer);
            var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(installerStream, cancellation)).ToLowerInvariant();
            if (!actualHash.Equals(SetupPolicy.TransportHash(RuntimeInformation.OSArchitecture), StringComparison.Ordinal))
                throw new InvalidDataException("The bundled USB transport installer failed its security check.");

            Process? setup;
            try { setup = Process.Start(new ProcessStartInfo(installer)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RESTARTEXITCODE=3010",
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = AppContext.BaseDirectory,
            }); }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return new(SetupOutcome.Cancelled, "Administrator approval was cancelled. Your sources can still be previewed; use the setup button when you are ready.");
            }
            using (setup)
            {
                if (setup == null) throw new InvalidOperationException("The USB transport installer did not start.");
                // Never forcibly terminate a driver installer midway through system changes.
                await setup.WaitForExitAsync();
                var result = SetupPolicy.InstallerResult(setup.ExitCode);
                if (result.Outcome != SetupOutcome.Attached) return result;
            }
            cancellation.ThrowIfCancellationRequested();
            usbip = FindUsbip() ?? throw new InvalidOperationException("USB transport setup completed, but usbip.exe was not installed.");
        }

        return await AttachCoreAsync(cancellation);
    }

    public static async Task<SetupResult> AttachExistingAsync(CancellationToken cancellation = default)
    {
        await SetupLock.WaitAsync(cancellation);
        try { return await AttachCoreAsync(cancellation); }
        finally { SetupLock.Release(); }
    }

    private static async Task<SetupResult> AttachCoreAsync(CancellationToken cancellation)
    {
        var usbip = FindUsbip() ?? throw new InvalidOperationException("Click the gear button to install MicWeave Microphone.");
        // Only attach our known loopback device, and never duplicate a port.
        var list = await RunAsync(usbip, "list -r 127.0.0.1", cancellation);
        if (!list.Contains(VirtualDeviceContract.UsbIdentity, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("MicWeave's local virtual microphone is not responding yet. Restart MicWeave and try the gear button again.");
        var ports = await RunAsync(usbip, "port", cancellation);
        if (!ports.Contains("usbip://127.0.0.1:3240/1-1", StringComparison.OrdinalIgnoreCase))
        {
            var defaults = MicrophoneProperties.CaptureDefaults();
            await RunAsync(usbip, "attach -r 127.0.0.1 -b 1-1", cancellation);
            // Endpoint creation is asynchronous. Restore any existing hardware defaults.
            for (var i = 0; i < 12; i++)
            {
                await Task.Delay(200, cancellation);
                // A disconnected old microphone must not make setup fail.
                try { MicrophoneProperties.RestoreDefaults(defaults); }
                catch (COMException ex) { AppLog.Write(ex); }
            }
        }
        return new(SetupOutcome.Attached, "Microphone connection requested. Waiting for Windows to make it available…");
    }

    private static async Task<string> RunAsync(string executable, string arguments, CancellationToken cancellation)
    {
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
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            // This is only our short-lived command-line client, never the shared driver.
            try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
            if (cancellation.IsCancellationRequested) throw;
            throw new TimeoutException("Windows did not finish connecting the microphone. Restart Windows and reopen MicWeave. The shared microphone component was not reinstalled.");
        }
        var text = (await stdout) + Environment.NewLine + (await stderr);
        if (process.ExitCode != 0)
        {
            AppLog.Write(new InvalidOperationException(text.Trim()));
            throw new InvalidOperationException("Windows could not connect MicWeave Microphone. Restart Windows and try again. If this is a work-managed PC, your administrator may need to allow the USBip component. Windows security settings were not changed.");
        }
        return text;
    }
}
