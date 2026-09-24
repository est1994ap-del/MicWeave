using System.IO;
using System.Text.Json;
using System.Runtime.InteropServices;
using GameMusicShare.Audio;
using NAudio.Wave;

namespace GameMusicShare.Services;

internal static class SelfTests
{
    public static int Run(string[] args)
    {
        var results = new List<object>();
        var failures = 0;
        void Check(string name, Action test)
        {
            try { test(); results.Add(new { name, passed = true, error = (string?)null }); }
            catch (Exception ex) { failures++; results.Add(new { name, passed = false, error = ex.Message }); }
        }
        void Assert(bool condition, string error) { if (!condition) throw new Exception(error); }
        Check("Setup accepts native Windows 11 x64 and ARM64, rejects mismatched drivers", () =>
        {
            var windows = new Version(10, 0, 22000);
            Assert(SetupPolicy.CompatibilityProblem(windows, Architecture.X64, Architecture.X64) == null, "Intel/AMD rejected.");
            Assert(SetupPolicy.CompatibilityProblem(windows, Architecture.Arm64, Architecture.Arm64) == null, "Native ARM64 rejected.");
            Assert(SetupPolicy.CompatibilityProblem(windows, Architecture.Arm64, Architecture.X64) != null, "Emulated app would install wrong driver.");
            Assert(SetupPolicy.CompatibilityProblem(new Version(10, 0, 19045), Architecture.X64, Architecture.X64) != null, "Windows 10 accepted.");
            Assert(SetupPolicy.CompatibilityProblem(windows, Architecture.X86, Architecture.X86) != null, "Unsupported processor accepted.");
            Assert(SetupPolicy.TransportHash(Architecture.X64) != SetupPolicy.TransportHash(Architecture.Arm64), "Processor-specific installer hashes are identical.");
        });
        Check("Setup preserves restart and cancellation outcomes instead of claiming readiness", () =>
        {
            Assert(SetupPolicy.InstallerResult(0).Outcome == SetupOutcome.Attached, "Successful installer rejected.");
            foreach (var code in new[] { 3010, 1641 })
                Assert(SetupPolicy.InstallerResult(code).Outcome == SetupOutcome.RestartRequired, "Restart was lost.");
            Assert(SetupPolicy.InstallerResult(2).Outcome == SetupOutcome.Cancelled, "Cancellation looked successful.");
            try { SetupPolicy.InstallerResult(5); throw new Exception("Failed installer looked successful."); }
            catch (InvalidOperationException) { }
        });
        Check("Endpoint matching requires the exact USB identity and serial, not a friendly name", () =>
        {
            Assert(EndpointIdentity.IsExpectedUsbInstance(VirtualDeviceContract.UsbInstance.ToLowerInvariant()), "Expected identity rejected.");
            Assert(!EndpointIdentity.IsExpectedUsbInstance(ProductInfo.MicrophoneName), "Friendly name was trusted.");
            Assert(!EndpointIdentity.IsExpectedUsbInstance(VirtualDeviceContract.UsbInstance + "-OTHER"), "Different serial accepted.");
            Assert(!EndpointIdentity.IsExpectedUsbInstance(@"USB\VID_1234&PID_5678\MICWEAVE-AUDIO-001"), "Different device accepted.");
        });
        Check("Unrouted program is excluded while microphone continues", () =>
        {
            var mix = new MixProvider { Microphone = new ConstantSource(.2f), Source = new ConstantSource(.3f), SourceGain = 1 };
            var block = new float[960]; mix.Read(block, 0, block.Length); mix.Read(block, 0, block.Length);
            Assert(Math.Abs(block[^1] - .2f) < .001, "Program audio leaked into voice-only mix.");
            mix.SourceRouted = true; mix.Read(block, 0, block.Length); Assert(Math.Abs(block[^1] - .5f) < .001, "Routed sources were not summed.");
            mix.SourceRouted = false; mix.Read(block, 0, block.Length); Assert(Math.Abs(block[^1] - .2f) < .001, "Unroute interrupted the microphone or retained source audio.");
        });
        Check("Independent mute and master controls", () =>
        {
            var mix = new MixProvider { Microphone = new ConstantSource(.2f), Source = new ConstantSource(.3f), SourceGain = 1, SourceRouted = true };
            var block = new float[960]; mix.MicrophoneMuted = true; mix.Read(block, 0, block.Length); Assert(Math.Abs(block[^1] - .3f) < .001, "Mic mute changed source.");
            mix.MicrophoneMuted = false; mix.SourceMuted = true; mix.Read(block, 0, block.Length); Assert(Math.Abs(block[^1] - .2f) < .001, "Source mute changed mic.");
            mix.OutputMuted = true; mix.Read(block, 0, block.Length); Assert(block[^1] == 0, "Master mute leaked audio.");
        });
        Check("Independent gains and overload protection", () =>
        {
            var mix = new MixProvider { Microphone = new ConstantSource(.25f), Source = new ConstantSource(.25f), SourceRouted = true, MicrophoneGain = 2, SourceGain = 1, MasterGain = .5f };
            var block = new float[960]; mix.Read(block, 0, block.Length); Assert(Math.Abs(block[^1] - .375f) < .001, "Gain calculation is wrong.");
            mix.MasterGain = 4; mix.Read(block, 0, block.Length); Assert(block.All(s => Math.Abs(s) <= .98001f), "Output clipped beyond the protected peak.");
        });
        Check("Only output transport updates final meter; source meters stay independent", () =>
        {
            var mix = new MixProvider { Microphone = new SineSource(1000), Source = new ConstantSource(0) };
            var block = new float[4800]; mix.Read(block, 0, block.Length);
            Assert(mix.MicrophoneMeter.Snapshot().HasSignal, "Mic meter did not detect real mic samples.");
            Assert(!mix.SourceMeter.Snapshot().HasSignal, "Silent source inherited microphone activity.");
            Assert(!mix.OutputMeter.Snapshot().HasSignal, "Preview falsely appeared to be sent.");
            var output = new PcmOutputProvider(mix); var pcm = new byte[19200]; output.Read(pcm, 0, pcm.Length);
            Assert(mix.OutputMeter.Snapshot().HasSignal, "Final meter did not observe output PCM.");
        });
        Check("PCM output is frame aligned and signed little-endian", () =>
        {
            var mix = new MixProvider { Microphone = new ConstantSource(.5f) };
            var output = new PcmOutputProvider(mix); var bytes = new byte[1920]; var count = output.Read(bytes, 0, bytes.Length);
            Assert(count == bytes.Length && BitConverter.ToInt16(bytes, count - 2) == 16384, "PCM conversion differs from driver contract.");
        });
        Check("Missing virtual microphone refuses successful routing", () =>
        {
            using var engine = new AudioEngine(new MissingOutput());
            try { engine.StartSending(); throw new Exception("Missing driver was accepted."); }
            catch (InvalidOperationException) { Assert(!engine.IsSending, "App claimed sending without a driver."); }
        });
        Check("Silence produces no fabricated spectrum", () =>
        {
            var meter = new SpectrumAnalyzer(); meter.Add(new float[4800], 0, 4800); var snapshot = meter.Snapshot();
            Assert(!snapshot.HasSignal && snapshot.Bars.All(value => value == 0), "Silent input produced artificial activity.");
        });
        Check("Multiple sources mix independently; activation, mute, gain and removal isolate one source", () =>
        {
            var mix = new MixProvider { Microphone = new ConstantSource(.1f), Source = new ConstantSource(.2f), SourceGain = 1, SourceRouted = true };
            var second = mix.AddSource(); second.Samples = new ConstantSource(.3f); second.Gain = 1; second.Enabled = true;
            var third = mix.AddSource(); third.Samples = new ConstantSource(.1f); third.Gain = 1; third.Enabled = true;
            var block = new float[960];
            mix.Read(block, 0, block.Length); Assert(Math.Abs(block[^1] - .7f) < .001, "Sources were not summed.");
            second.Enabled = false; mix.Read(block, 0, block.Length); Assert(Math.Abs(block[^1] - .4f) < .001, "Turning off one source changed another.");
            second.Enabled = true; third.Muted = true; second.Gain = .5f; mix.Read(block, 0, block.Length);
            Assert(Math.Abs(block[^1] - .45f) < .001, "Independent source gain/mute is wrong.");
            mix.RemoveSource(second); mix.Read(block, 0, block.Length); Assert(Math.Abs(block[^1] - .3f) < .001, "Removed source remained in mix.");
            mix.OutputMuted = true; mix.Read(block, 0, block.Length); Assert(block[^1] == 0, "Multiple sources leaked through master mute.");
        });
        Check("Inactive additional sources still have independent live preview meters", () =>
        {
            var mix = new MixProvider(); var second = mix.AddSource(); second.Samples = new SineSource(660);
            var block = new float[4800]; mix.Read(block, 0, block.Length);
            Assert(second.Meter.Snapshot().HasSignal, "Inactive source preview is missing.");
            Assert(!mix.SourceMeter.Snapshot().HasSignal, "Additional source polluted another source meter.");
            Assert(block.All(s => s == 0), "Inactive source leaked to the output.");
        });
        Diagnostics.WriteResult(args, new { passed = failures == 0, failures, tests = results });
        return failures == 0 ? 0 : 1;
    }
    private sealed class ConstantSource(float value) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public int Read(float[] buffer, int offset, int count) { Array.Fill(buffer, value, offset, count); return count; }
    }
    private sealed class SineSource(float frequency) : ISampleProvider
    {
        private int position;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public int Read(float[] buffer, int offset, int count)
        {
            for (var i = 0; i < count; i += 2) { var sample = .2f * MathF.Sin(2 * MathF.PI * frequency * position++ / 48000); buffer[offset + i] = sample; buffer[offset + i + 1] = sample; }
            return count;
        }
    }
    private sealed class MissingOutput : IVirtualMicrophoneOutput
    {
        public VirtualMicrophoneEndpoints? Discover() => null;
        public bool IsSending => false;
        public event Action<Exception>? Faulted { add { } remove { } }
        public void Start(VirtualMicrophoneEndpoints endpoints, IWaveProvider samples) => throw new InvalidOperationException();
        public void Stop() { } public void Dispose() { }
    }
}
