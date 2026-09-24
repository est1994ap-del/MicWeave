using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace GameMusicShare.Audio;

/// <summary>Owns a helper process and authenticates it before sending audio.</summary>
internal sealed class HelperSession : IDisposable
{
    private Process? process;
    private byte[] secret = [];
    private const string Magic = "MICWEAVE_PCM_V2\n";

    public void EnsureStarted()
    {
        if (process is { HasExited: false }) return;
        Dispose();
        var executable = Path.Combine(AppContext.BaseDirectory, "Assets", "MicWeave.VirtualUsb.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("MicWeave's microphone helper is missing. Reinstall MicWeave.", executable);
        secret = RandomNumberGenerator.GetBytes(32);
        try
        {
            process = Process.Start(new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = AppContext.BaseDirectory, Arguments = $"--parent-pid {Environment.ProcessId}",
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            }) ?? throw new InvalidOperationException("MicWeave could not start its microphone helper.");
            _ = process.StandardError.ReadToEndAsync();
            process.StandardInput.BaseStream.Write(secret);
            process.StandardInput.Close();
            var ready = process.StandardOutput.ReadLineAsync();
            if (!ready.Wait(TimeSpan.FromSeconds(8)) || ready.Result != "MICWEAVE_READY_V2" || process.HasExited)
                throw new InvalidOperationException("MicWeave could not open its private audio connection. Close another MicWeave instance or a program using local ports 3240/32194, then retry.");
        }
        catch { Dispose(); throw; }
    }

    public TcpClient Connect()
    {
        EnsureStarted();
        var client = new TcpClient { NoDelay = true, ReceiveTimeout = 2000, SendTimeout = 2000 };
        try
        {
            client.ConnectAsync("127.0.0.1", 32194).WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            var stream = client.GetStream();
            var hello = new byte[Encoding.ASCII.GetByteCount(Magic) + 32];
            stream.ReadExactly(hello);
            if (!hello.AsSpan(0, hello.Length - 32).SequenceEqual(Encoding.ASCII.GetBytes(Magic)))
                throw new InvalidDataException("Unexpected microphone helper protocol. No audio was sent.");
            var serverNonce = hello[^32..];
            var clientNonce = RandomNumberGenerator.GetBytes(32);
            stream.Write(clientNonce);
            stream.Write(Proof("client", serverNonce, clientNonce));
            var serverProof = new byte[32]; stream.ReadExactly(serverProof);
            if (!CryptographicOperations.FixedTimeEquals(serverProof, Proof("server", serverNonce, clientNonce)))
                throw new InvalidDataException("Microphone helper authentication failed. No audio was sent.");
            client.ReceiveTimeout = 0; client.SendTimeout = 0;
            return client;
        }
        catch { client.Dispose(); throw; }
    }

    private byte[] Proof(string role, byte[] serverNonce, byte[] clientNonce)
    {
        var message = Encoding.ASCII.GetBytes("MicWeave/2/" + role).Concat(serverNonce).Concat(clientNonce).ToArray();
        return HMACSHA256.HashData(secret, message);
    }

    public void Dispose()
    {
        if (process != null)
        {
            try { if (!process.HasExited) { process.Kill(); process.WaitForExit(2000); } }
            catch (InvalidOperationException) { }
            finally { process.Dispose(); process = null; }
        }
        CryptographicOperations.ZeroMemory(secret); secret = [];
    }
}
