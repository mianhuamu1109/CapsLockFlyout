using Microsoft.Win32.SafeHandles;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace FluentFlyoutWPF.CapsFlyout;

internal sealed record ApplicationIdentity(string? Executable, string? Family = null, string? AppId = null);

internal static class ApplicationIdentityReader
{
    private static readonly object Gate = new();
    private static readonly Dictionary<(uint Pid, long Created), (ApplicationIdentity Identity, long Until)> Cache = new();

    internal static ApplicationIdentity Read(nint window)
    {
        if (window == 0 || NativeMethods.GetWindowThreadProcessId(window, out var pid) == 0)
            return new(null);
        var owner = ReadProcess(pid);
        var host = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ApplicationFrameHost.exe");
        if (!string.Equals(owner.Executable, host, StringComparison.OrdinalIgnoreCase)) return owner;

        // ApplicationFrameHost is shared. Only visible packaged child owners may
        // identify this window; never use the shared host as an automatic rule.
        var children = new List<ApplicationIdentity>();
        var seen = new HashSet<uint>();
        EnumChildWindows(window, (child, _) =>
        {
            if (IsWindowVisible(child) &&
                NativeMethods.GetWindowThreadProcessId(child, out var childPid) != 0 &&
                childPid != pid && seen.Add(childPid))
            {
                var identity = ReadProcess(childPid);
                if (identity.Family is not null) children.Add(identity);
            }
            return true;
        }, 0);
        return ResolveHosted(children);
    }

    internal static ApplicationIdentity ResolveHosted(IEnumerable<ApplicationIdentity> children)
    {
        var candidates = children.Where(x => x.Family is not null)
            .DistinctBy(x => (x.Family!.ToUpperInvariant(), x.AppId?.ToUpperInvariant())).ToArray();
        return candidates.Length == 1 ? candidates[0] : new(null);
    }

    internal static ApplicationIdentity ReadProcess(uint pid)
    {
        using var process = OpenProcess(0x1000, false, pid);
        if (process.IsInvalid) return new(null);
        var hasTime = GetProcessTimes(process, out var created, out _, out _, out _);
        lock (Gate)
        {
            if (hasTime && Cache.TryGetValue((pid, created), out var cached) &&
                Environment.TickCount64 < cached.Until) return cached.Identity;
            var path = new StringBuilder(32768);
            var length = path.Capacity;
            var executable = QueryFullProcessImageName(process, 0, path, ref length) ? path.ToString() : null;
            var family = ReadIdentity(process, GetPackageFamilyName);
            var appId = family is null ? null : ReadIdentity(process, GetApplicationUserModelId);
            var result = new ApplicationIdentity(executable, family, appId);
            // Failed lookups are not cached indefinitely. Creation time prevents PID reuse.
            if (hasTime && executable is not null && (family is null || appId is not null))
            {
                if (Cache.Count >= 128) Cache.Clear();
                Cache[(pid, created)] = (result, Environment.TickCount64 + 5000);
            }
            return result;
        }
    }

    private delegate int IdentityQuery(SafeProcessHandle process, ref uint length, StringBuilder? value);
    private static string? ReadIdentity(SafeProcessHandle process, IdentityQuery query)
    {
        uint length = 0;
        if (query(process, ref length, null) != 122 || length is 0 or > 32768) return null;
        var value = new StringBuilder((int)length);
        return query(process, ref length, value) == 0 ? value.ToString() : null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder name, ref int length);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFamilyName(SafeProcessHandle process, ref uint length, StringBuilder? name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetApplicationUserModelId(SafeProcessHandle process, ref uint length, StringBuilder? name);
    private delegate bool EnumWindow(nint window, nint parameter);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(nint parent, EnumWindow callback, nint parameter);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
}
