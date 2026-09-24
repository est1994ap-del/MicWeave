namespace GameMusicShare.Audio;

/// <summary>Capture-only USB audio format and identity. No render endpoint exists.</summary>
public static class VirtualDeviceContract
{
    public const string CaptureName = ProductInfo.MicrophoneName;
    // Unallocated development identity. See docs/DEVICE-IDENTITY.md before distribution.
    public const string UsbIdentity = "ffff:4d57";
    public const string UsbInstance = @"USB\VID_FFFF&PID_4D57\MICWEAVE-AUDIO-001";
    public const int SampleRate = 48000;
    public const int Channels = 2;
}
