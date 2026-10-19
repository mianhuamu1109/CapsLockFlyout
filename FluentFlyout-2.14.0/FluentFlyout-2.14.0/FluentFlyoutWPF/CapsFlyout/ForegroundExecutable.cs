using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;

namespace FluentFlyoutWPF.CapsFlyout;

internal static class ForegroundExecutable
{
    internal static string? Read(nint window)
    {
        if (window == 0 || NativeMethods.GetWindowThreadProcessId(window, out var pid) == 0)
            return null;
        return ReadProcess(pid);
    }

    internal static string? ReadProcess(uint pid)
    {
        // Query-only rights also work for many elevated desktop applications.
        // No injection, keyboard interception, or enumeration of unrelated processes.
        using var process = OpenProcess(0x1000, false, pid);
        if (process.IsInvalid) return null;
        var buffer = new StringBuilder(1024);
        var size = buffer.Capacity;
        if (QueryFullProcessImageName(process, 0, buffer, ref size)) return buffer.ToString();
        if (Marshal.GetLastWin32Error() != 122) return null;
        buffer = new StringBuilder(32768);
        size = buffer.Capacity;
        return QueryFullProcessImageName(process, 0, buffer, ref size) ? buffer.ToString() : null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags,
        StringBuilder name, ref int size);
}
