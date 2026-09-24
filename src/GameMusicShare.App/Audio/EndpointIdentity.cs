using System.Runtime.InteropServices;
using System.Text;
using NAudio.CoreAudioApi;

namespace GameMusicShare.Audio;

internal static class EndpointIdentity
{
    // A user-editable friendly name is not sufficient to identify our device.
    // Walk the live Windows device tree and require our exact USB identity/serial.
    // This prevents accidental renames from targeting another mic; it is not
    // authentication against a privileged attacker who can emulate USB hardware.
    public static bool IsMicWeave(MMDevice device)
    {
        if (device.DataFlow != DataFlow.Capture) return false;
        if (CM_Locate_DevNodeW(out var node, @"SWD\MMDEVAPI\" + device.ID, 0) != 0) return false;
        for (var depth = 0; depth < 8; depth++)
        {
            var id = new StringBuilder(512);
            if (CM_Get_Device_IDW(node, id, id.Capacity, 0) != 0) return false;
            if (IsExpectedUsbInstance(id.ToString())) return true;
            if (CM_Get_Parent(out var parent, node, 0) != 0) return false;
            node = parent;
        }
        return false;
    }

    internal static bool IsExpectedUsbInstance(string id) =>
        string.Equals(id, VirtualDeviceContract.UsbInstance, StringComparison.OrdinalIgnoreCase);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Locate_DevNodeW(out uint node, string id, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_IDW(uint node, StringBuilder buffer, int length, uint flags);
    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    private static extern uint CM_Get_Parent(out uint parent, uint node, uint flags);
}
