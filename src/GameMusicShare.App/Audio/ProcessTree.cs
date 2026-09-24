using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GameMusicShare.Audio;

internal static class ProcessTree
{
    public static Dictionary<int, int> GetParents()
    {
        var parents = new Dictionary<int, int>();
        var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == new IntPtr(-1)) return parents;
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>(), ExeFile = "" };
            if (Process32FirstW(snapshot, ref entry))
                do { parents[(int)entry.ProcessId] = (int)entry.ParentProcessId; } while (Process32NextW(snapshot, ref entry));
        }
        finally { CloseHandle(snapshot); }
        return parents;
    }
    public static int FindSameApplicationRoot(int pid, string name, Dictionary<int, int> parents)
    {
        var visited = new HashSet<int> { pid };
        while (parents.TryGetValue(pid, out var parent) && visited.Add(parent))
        {
            try { using var process = Process.GetProcessById(parent); if (!process.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase)) break; pid = parent; }
            catch { break; }
        }
        return pid;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, ProcessId;
        public UIntPtr DefaultHeapId;
        public uint ModuleId, Threads, ParentProcessId;
        public int Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
}
