using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;

namespace FluentFlyoutWPF.CapsFlyout;

internal sealed record InstalledApplication(string Name, string Family, string AppId, string[] Executables)
{
    public string DisplayName => Name;
    public string Identity => AppId;
    internal ApplicationExclusion ToRule() => new(Name, Family, AppId);
}

internal static class InstalledApplications
{
    internal static Task<IReadOnlyList<InstalledApplication>> LoadAsync()
    {
        var completion = new TaskCompletionSource<IReadOnlyList<InstalledApplication>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(Read()); }
            catch (Exception e) { completion.SetException(e); }
        }) { IsBackground = true, Name = "Installed applications" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static IReadOnlyList<InstalledApplication> Read()
    {
        object? shell = null, folder = null, items = null;
        var result = new List<InstalledApplication>();
        var packages = new Dictionary<string, Dictionary<string, string[]>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", throwOnError: true)!);
            folder = ((dynamic)shell!).NameSpace("shell:AppsFolder");
            if (folder is null) throw new InvalidOperationException("无法读取 Windows 已安装应用列表。");
            items = ((dynamic)folder).Items();
            int count = ((dynamic)items).Count;
            for (var i = 0; i < count; i++)
            {
                object? item = null;
                try
                {
                    item = ((dynamic)items).Item(i);
                    string? appId = ((dynamic)item!).ExtendedProperty("System.AppUserModel.ID") as string;
                    if (!TrySplitId(appId, out var family)) continue;
                    if (!packages.TryGetValue(family!, out var executables))
                    {
                        var names = PackageNames(family!);
                        if (names.Length == 0) continue; // Not a registered packaged app.
                        executables = ReadManifestExecutables(names, family!);
                        packages[family!] = executables;
                    }
                    string name = ((dynamic)item!).Name;
                    result.Add(new(name, family!, appId!, executables.GetValueOrDefault(appId!) ?? []));
                }
                catch (COMException) { } // One stale Shell entry must not lose the whole list.
                finally { Release(item); }
            }
        }
        finally { Release(items); Release(folder); Release(shell); }
        return result.DistinctBy(x => x.AppId, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    internal static bool TrySplitId(string? appId, out string? family)
    {
        family = null;
        if (string.IsNullOrWhiteSpace(appId)) return false;
        var split = appId.IndexOf('!');
        if (split <= 0 || split == appId.Length - 1 || appId.IndexOf('!', split + 1) >= 0) return false;
        var candidate = appId[..split];
        var underscore = candidate.LastIndexOf('_');
        if (underscore <= 0 || candidate.Length - underscore - 1 != 13 ||
            candidate.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'))) return false;
        family = candidate;
        return true;
    }

    // Conversion is explicit and requires an exact path from a current registered
    // manifest. Old, absent or ambiguous paths remain unchanged.
    internal static InstalledApplication? FindConversion(string path, IEnumerable<InstalledApplication> apps)
    {
        var matches = apps.Where(app => app.Executables.Any(executable =>
            StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(path), Path.GetFullPath(executable)))).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static Dictionary<string, string[]> ReadManifestExecutables(string[] names, string family)
    {
        var paths = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            uint length = 0;
            if (GetPackagePathByFullName(name, ref length, null) != 122 || length is 0 or > 32768) continue;
            var buffer = new StringBuilder((int)length);
            if (GetPackagePathByFullName(name, ref length, buffer) != 0) continue;
            var root = buffer.ToString();
            try
            {
                var document = XDocument.Load(Path.Combine(root, "AppxManifest.xml"));
                foreach (var app in document.Descendants().Where(e => e.Name.LocalName == "Application"))
                {
                    var id = app.Attribute("Id")?.Value;
                    var exe = app.Attribute("Executable")?.Value;
                    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(exe) || Path.IsPathRooted(exe)) continue;
                    var full = Path.GetFullPath(Path.Combine(root, exe));
                    if (!full.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                    var key = family + "!" + id;
                    if (!paths.TryGetValue(key, out var list)) paths[key] = list = [];
                    list.Add(full);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Xml.XmlException) { }
        }
        return paths.ToDictionary(x => x.Key, x => x.Value.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static string[] PackageNames(string family)
    {
        uint count = 0, length = 0;
        if (GetPackagesByPackageFamily(family, ref count, 0, ref length, 0) != 122 ||
            count is 0 or > 4096 || length is 0 or > 1048576) return [];
        var pointers = Marshal.AllocHGlobal(checked((int)count * nint.Size));
        var buffer = Marshal.AllocHGlobal(checked((int)length * 2));
        try
        {
            if (GetPackagesByPackageFamily(family, ref count, pointers, ref length, buffer) != 0) return [];
            return Enumerable.Range(0, (int)count).Select(i =>
                Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointers, i * nint.Size))!).ToArray();
        }
        finally { Marshal.FreeHGlobal(buffer); Marshal.FreeHGlobal(pointers); }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackagesByPackageFamily(string family, ref uint count, nint names, ref uint length, nint buffer);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackagePathByFullName(string name, ref uint length, StringBuilder? path);
}
