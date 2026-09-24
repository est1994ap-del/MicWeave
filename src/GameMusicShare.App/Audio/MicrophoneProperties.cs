using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace GameMusicShare.Audio;

internal static class MicrophoneProperties
{
    // MicWeave carries music as well as speech. Keep Windows voice effects
    // from removing music on this endpoint; never touch another microphone.
    public static void DisableEffects(string captureId)
    {
        var policy = (IPolicyConfig)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9"))!)!;
        try
        {
            var key = new PropertyKey { FormatId = new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"), Id = 5 };
            if (policy.GetPropertyValue(captureId, true, ref key, out var current) >= 0 && current.Type == 19 && current.UIntValue == 1) return;
            var value = new Variant { Type = 19, UIntValue = 1 };
            Marshal.ThrowExceptionForHR(policy.SetPropertyValue(captureId, true, ref key, ref value));
        }
        finally { Marshal.ReleaseComObject(policy); }
    }

    public static Dictionary<Role, string> CaptureDefaults()
    {
        using var enumerator = new MMDeviceEnumerator();
        var result = new Dictionary<Role, string>();
        foreach (var role in Enum.GetValues<Role>())
        {
            try { using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, role); result[role] = device.ID; }
            catch (COMException) { }
        }
        return result;
    }

    public static void RestoreDefaults(Dictionary<Role, string> previous)
    {
        using var enumerator = new MMDeviceEnumerator();
        var policy = (IPolicyConfig)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9"))!)!;
        try
        {
            foreach (var (role, id) in previous)
            {
                using var device = enumerator.GetDevice(id);
                if (device.State != DeviceState.Active) continue;
                using var current = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, role);
                // Only undo Windows automatically choosing the newly attached MicWeave.
                if (current.ID != id && EndpointIdentity.IsMicWeave(current))
                    Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(id, role));
            }
        }
        finally { Marshal.ReleaseComObject(policy); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid FormatId; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct Variant
    { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public uint UIntValue; }

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        void GetMixFormat(); void GetDeviceFormat(); void ResetDeviceFormat(); void SetDeviceFormat();
        void GetProcessingPeriod(); void SetProcessingPeriod(); void GetShareMode(); void SetShareMode();
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, [MarshalAs(UnmanagedType.Bool)] bool effects, ref PropertyKey key, out Variant value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, [MarshalAs(UnmanagedType.Bool)] bool effects, ref PropertyKey key, ref Variant value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, Role role);
    }
}
