using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
namespace IntoChat.LocalFiles;

// Keep Windows directory ancestors immovable while a local file operation is in progress.
internal sealed class LocalPathLease : IDisposable
{
    private readonly List<SafeFileHandle> _handles = [];
    public static LocalPathLease Acquire(string directory)
    {
        var lease = new LocalPathLease();
        if (!OperatingSystem.IsWindows()) { return lease; }
        try
        {
            var chain = new Stack<string>();
            for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent) { chain.Push(current.FullName); }
            foreach (var path in chain)
            {
                var handle = CreateFileW(path, 0, 3, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
                if (handle.IsInvalid) { handle.Dispose(); throw new IOException("The local folder cannot be locked for this operation.", new Win32Exception(Marshal.GetLastWin32Error())); }
                lease._handles.Add(handle);
                Verify(handle, path);
            }
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }
    public static void Verify(SafeFileHandle handle, string expected)
    {
        if (!OperatingSystem.IsWindows()) { return; }
        var result = new StringBuilder(32768);
        var count = GetFinalPathNameByHandleW(handle, result, (uint)result.Capacity, 0);
        if (count == 0 || count >= result.Capacity) { throw new IOException("Could not verify the local file location."); }
        var actual = result.ToString();
        if (actual.StartsWith("\\\\?\\UNC\\", StringComparison.Ordinal)) { actual = "\\\\" + actual[8..]; }
        else if (actual.StartsWith("\\\\?\\", StringComparison.Ordinal)) { actual = actual[4..]; }
        if (!Path.TrimEndingDirectorySeparator(actual).Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(expected)), StringComparison.OrdinalIgnoreCase))
        { throw new UnauthorizedAccessException("Linked or redirected local paths are not supported."); }
    }
    public void Dispose() { foreach (var handle in _handles) { handle.Dispose(); } }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
}
