using System.Runtime.InteropServices;
using NAudio.Wave;

namespace GameMusicShare.Audio;

/// <summary>Windows process-tree loopback: copies one app's audio while its normal speakers continue playing.</summary>
public sealed class ProcessLoopbackCapture : IWaveIn
{
    private NativeAudioClient? client;
    private NativeCaptureClient? capture;
    private readonly AutoResetEvent samplesReady = new(false);
    private readonly ManualResetEvent stop = new(false);
    private Thread? thread;
    private TaskCompletionSource<bool>? started;
    private bool disposed;
    public WaveFormat WaveFormat { get; set; } = new(48000, 16, 2);
    public event EventHandler<WaveInEventArgs>? DataAvailable;
    public event EventHandler<StoppedEventArgs>? RecordingStopped;
    private ProcessLoopbackCapture() { }
    public static async Task<ProcessLoopbackCapture> CreateAsync(int processId)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348)) throw new PlatformNotSupportedException("Capturing one program needs Windows 11 or Windows build 20348 or later.");
        if (processId <= 0 || processId == Environment.ProcessId) throw new ArgumentException("Choose another running program.");
        var result = new ProcessLoopbackCapture();
        try
        {
            var activation = Task.Run(() => Activation.ActivateAsync((uint)processId));
            try { result.client = await activation.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false); }
            catch (TimeoutException)
            {
                // A late OS callback must release its client instead of retaining an orphaned capture.
                _ = activation.ContinueWith(task => { if (task.Status == TaskStatus.RanToCompletion) Marshal.ReleaseComObject(task.Result); }, TaskScheduler.Default);
                throw new TimeoutException("Windows did not open this program’s audio in time. Refresh the source list and try again.");
            }
            var format = new NativeWaveFormat { FormatTag = 1, Channels = 2, SamplesPerSecond = 48000, AverageBytesPerSecond = 192000, BlockAlign = 4, BitsPerSample = 16 };
            Check(result.client.Initialize(0, 0x00020000 | 0x00040000 | unchecked((int)0x80000000), 0, 0, ref format, IntPtr.Zero));
            var iid = typeof(NativeCaptureClient).GUID;
            Check(result.client.GetService(ref iid, out var service));
            result.capture = (NativeCaptureClient)service;
            Check(result.client.SetEventHandle(result.samplesReady.SafeWaitHandle.DangerousGetHandle()));
            return result;
        }
        catch { result.Dispose(); throw; }
    }
    public void StartRecording()
    {
        if (thread != null) throw new InvalidOperationException("Capture is already running.");
        if (client == null) throw new ObjectDisposedException(nameof(ProcessLoopbackCapture));
        stop.Reset();
        started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        thread = new Thread(CaptureLoop) { IsBackground = true, Name = "Program audio capture" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        started.Task.GetAwaiter().GetResult();
    }
    private void CaptureLoop()
    {
        Exception? failure = null;
        var bytes = new byte[192000];
        try
        {
            Check(client!.Start());
            started!.TrySetResult(true);
            var handles = new WaitHandle[] { stop, samplesReady };
            while (WaitHandle.WaitAny(handles, 100) != 0)
            {
                Check(capture!.GetNextPacketSize(out var frames));
                while (frames > 0)
                {
                    Check(capture.GetBuffer(out var data, out var count, out var flags, out _, out _));
                    try
                    {
                        var length = checked((int)count * 4);
                        if (bytes.Length < length) bytes = new byte[length];
                        if ((flags & 2) != 0) Array.Clear(bytes, 0, length); else Marshal.Copy(data, bytes, 0, length);
                        DataAvailable?.Invoke(this, new WaveInEventArgs(bytes, length));
                    }
                    finally { Check(capture.ReleaseBuffer(count)); }
                    Check(capture.GetNextPacketSize(out frames));
                }
            }
        }
        catch (Exception ex) { failure = ex; started!.TrySetException(ex); }
        finally { try { client?.Stop(); } catch { } RecordingStopped?.Invoke(this, new StoppedEventArgs(failure)); }
    }
    public void StopRecording()
    {
        stop.Set();
        if (thread != null && Thread.CurrentThread != thread) thread.Join();
        thread = null;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        StopRecording();
        if (capture != null) { Marshal.ReleaseComObject(capture); capture = null; }
        if (client != null) { Marshal.ReleaseComObject(client); client = null; }
        samplesReady.Dispose(); stop.Dispose();
    }
    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class Activation : ActivationCompletionHandler, AgileObject
    {
        private readonly TaskCompletionSource<NativeAudioClient> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private IntPtr parameters;
        private IntPtr variant;
        private GCHandle keepAlive;
        public static Task<NativeAudioClient> ActivateAsync(uint pid)
        {
            var handler = new Activation();
            handler.keepAlive = GCHandle.Alloc(handler);
            handler.parameters = Marshal.AllocHGlobal(12);
            Marshal.WriteInt32(handler.parameters, 0, 1); // AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK
            Marshal.WriteInt32(handler.parameters, 4, (int)pid);
            Marshal.WriteInt32(handler.parameters, 8, 0); // INCLUDE_TARGET_PROCESS_TREE
            handler.variant = Marshal.AllocHGlobal(24);
            Marshal.Copy(new byte[24], 0, handler.variant, 24);
            Marshal.WriteInt16(handler.variant, 0, 65); // VT_BLOB; x64 BLOB starts at offset 8.
            Marshal.WriteInt32(handler.variant, 8, 12);
            Marshal.WriteIntPtr(handler.variant, 16, handler.parameters);
            var iid = typeof(NativeAudioClient).GUID;
            var hr = ActivateAudioInterfaceAsync("VAD\\Process_Loopback", ref iid, handler.variant, handler, out var operation);
            if (operation != null) Marshal.ReleaseComObject(operation);
            if (hr < 0) { handler.Cleanup(); handler.completion.TrySetException(Marshal.GetExceptionForHR(hr)!); }
            return handler.completion.Task;
        }
        public int ActivateCompleted(ActivationOperation operation)
        {
            try { Check(operation.GetActivateResult(out var hr, out var activated)); Check(hr); completion.TrySetResult((NativeAudioClient)activated); }
            catch (Exception ex) { completion.TrySetException(ex); }
            finally { Cleanup(); }
            return 0;
        }
        private void Cleanup()
        {
            if (parameters != IntPtr.Zero) { Marshal.FreeHGlobal(parameters); parameters = IntPtr.Zero; }
            if (variant != IntPtr.Zero) { Marshal.FreeHGlobal(variant); variant = IntPtr.Zero; }
            if (keepAlive.IsAllocated) keepAlive.Free();
        }
    }
    [DllImport("Mmdevapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int ActivateAudioInterfaceAsync(string device, ref Guid iid, IntPtr parameters, ActivationCompletionHandler completion, out ActivationOperation operation);
    [ComVisible(true), Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ActivationCompletionHandler { [PreserveSig] int ActivateCompleted(ActivationOperation operation); }
    [ComImport, Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface AgileObject { }
    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ActivationOperation { [PreserveSig] int GetActivateResult(out int result, [MarshalAs(UnmanagedType.IUnknown)] out object activated); }
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    public struct NativeWaveFormat { public ushort FormatTag, Channels; public uint SamplesPerSecond, AverageBytesPerSecond; public ushort BlockAlign, BitsPerSample, ExtraSize; }
    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface NativeAudioClient
    {
        [PreserveSig] int Initialize(int mode, int flags, long duration, long periodicity, ref NativeWaveFormat format, IntPtr session);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint frames);
        [PreserveSig] int IsFormatSupported(int mode, IntPtr format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long normal, out long minimum);
        [PreserveSig] int Start(); [PreserveSig] int Stop(); [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }
    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface NativeCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong position, out ulong counter);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }
}
