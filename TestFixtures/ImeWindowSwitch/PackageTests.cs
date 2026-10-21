using FluentFlyoutWPF.CapsFlyout;
using System.Diagnostics;
using System.Text.Json;
using ApplicationIdentity = FluentFlyoutWPF.CapsFlyout.ApplicationIdentity;

internal static partial class Program
{
    private const string PhotosFamily = "Microsoft.Windows.Photos_8wekyb3d8bbwe";
    private const string PhotosId = PhotosFamily + "!App";
    private static void RunPackageTests()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "artifacts", "packages-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "ime-exclusions.json");
        var original = JsonSerializer.Serialize(new[] { @"D:\Tools\Editor.exe" });
        File.WriteAllText(file, original);
        var settings = new InputMethodExclusions(file);
        var photos = new ApplicationExclusion("照片", PhotosFamily, PhotosId);
        Check(settings.Contains(@"D:\Tools\Editor.exe") && !settings.HasApplications, "1.1.2 path-array settings load unchanged");
        Check(File.ReadAllText(file) == original && !File.Exists(file + ".v1-backup"), "Reading legacy settings never rewrites them");
        Check(settings.TrySave(settings.Paths, [photos], out _), "Save mixed EXE and application rules");
        Check(File.ReadAllText(file + ".v1-backup") == original, "First v2 edit preserves exact legacy backup");
        Check(settings.Contains(@"D:\Tools\Editor.exe"), "Adding a package preserves existing EXE rules");
        Check(new InputMethodExclusions(file).Applications.Single() == photos, "Application identity survives reload");
        var before = new ApplicationIdentity(@"C:\WindowsApps\Photos_1\Photos.exe", PhotosFamily, PhotosId);
        var after = new ApplicationIdentity(@"D:\WindowsApps\Photos_2\NewPhotos.exe", PhotosFamily, PhotosId);
        Check(settings.Matches(before) && settings.Matches(after), "Version, volume and executable changes do not break app identity");
        Check(settings.Matches(after with { Family = PhotosFamily.ToUpperInvariant(), AppId = PhotosId.ToUpperInvariant() }),
            "Application identities match case-insensitively");
        Check(!settings.Matches(after with { AppId = PhotosFamily + "!Other" }), "Different app in same package is not excluded");
        Check(!settings.Matches(after with { Family = "Other.Package_8wekyb3d8bbwe" }), "Different package is not excluded");
        Check(!settings.Matches(after with { AppId = null }), "Unknown app identity does not widen to whole package");
        Check(!settings.Matches(new(@"C:\Other\Photos.exe")), "Same EXE name without package identity is not excluded");
        Check(settings.TrySave(settings.Paths, [photos, photos], out _) && settings.Applications.Count == 1, "Duplicate application rules collapse");
        Check(!settings.TrySave(settings.Paths, [photos with { Family = "Other" }], out _) && settings.Applications.Single() == photos,
            "Invalid package rule fails without changing saved rules");
        Check(settings.TrySave([], out _) && settings.HasApplications, "Editing path rules retains application rules");
        Check(File.ReadAllText(file + ".v1-backup") == original, "Repeated edits preserve original backup");
        Check(settings.TrySave([], [], out _) && !settings.HasEntries, "Both rule kinds can be removed");
        var futureFile = Path.Combine(root, "future.json");
        const string future = """{"Version":99,"Paths":[],"Applications":[]}""";
        File.WriteAllText(futureFile, future);
        var unsupported = new InputMethodExclusions(futureFile);
        Check(unsupported.LoadError is not null && !unsupported.TrySave([], out _) &&
            File.ReadAllText(futureFile) == future, "Unknown future schema is preserved with visible error");

        var candidate = new InstalledApplication("照片", PhotosFamily, PhotosId, [before.Executable!]);
        Check(InstalledApplications.FindConversion(before.Executable!, [candidate]) == candidate, "Exact registered manifest path may be converted");
        Check(InstalledApplications.FindConversion(after.Executable!, [candidate]) is null, "Unverified or obsolete path is not guessed");
        Check(InstalledApplications.FindConversion(before.Executable!, [candidate, candidate with { AppId = PhotosFamily + "!Other" }]) is null,
            "Shared executable with multiple app identities is not auto-converted");
        Check(InstalledApplications.TrySplitId(PhotosId, out var family) && family == PhotosFamily, "Installed-app identity parsing");
        Check(!InstalledApplications.TrySplitId("Photos.exe", out _) && !InstalledApplications.TrySplitId(PhotosId + "!extra", out _),
            "Desktop IDs and malformed package IDs are rejected");
        Check(ApplicationIdentityReader.ResolveHosted([before, before]) == before, "Hosted window resolves a unique packaged child");
        Check(ApplicationIdentityReader.ResolveHosted([before, after with { AppId = PhotosFamily + "!Other" }]).Family is null,
            "Ambiguous hosted window does not disable unrelated apps");
        Check(ApplicationIdentityReader.ResolveHosted([]).Executable is null, "Shared host alone is not an application rule");
        var current = ApplicationIdentityReader.ReadProcess((uint)Environment.ProcessId);
        Check(string.Equals(current.Executable, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase),
            "Real identity API resolves nonpackaged test process");
        Check(ApplicationIdentityReader.ReadProcess(0).Executable is null, "Inaccessible process safely produces unknown identity");

        Check(settings.TrySave([], [photos], out _), "Configure package-only monitor");
        nint window = 1;
        long tick = 10000;
        var identity = new ApplicationIdentity(@"D:\Tools\Editor.exe");
        var snapshot = Chinese(1);
        var messages = new List<OsdMessage>();
        var hides = 0;
        var queries = 0;
        var pathQueries = 0;
        using var monitor = new InputMethodMonitor(settings, () => window,
            _ => { pathQueries++; return identity.Executable; },
            () => { queries++; return snapshot; }, () => tick, _ => identity);
        monitor.MessageRaised += messages.Add;
        monitor.HideInputMethodRequested += () => hides++;
        monitor.Poll();
        tick = 10400; monitor.Poll();
        tick = 10520; snapshot = Chinese(1, false); monitor.Poll();
        Check(messages.Count == 1, "Nonexcluded app displays with package rules enabled");
        var queued = messages[0];
        window = 2; identity = before; snapshot = Japanese(2); tick = 10640;
        Check(!monitor.CanPresent(queued), "Foreground change rejects queued IME before package poll");
        var oldQueries = queries;
        monitor.Poll();
        for (var i = 0; i < 1000; i++) { tick += 120; identity = i % 2 == 0 ? before : after; monitor.Poll(); }
        Check(messages.Count == 1 && queries == oldQueries && hides == 1,
            "1000 package/version switches remain excluded without IME queries or hide floods");
        Check(pathQueries == 0, "Package-only rules do not perform separate EXE-path queries");
        window = 3; identity = after with { AppId = PhotosFamily + "!Other" }; snapshot = Chinese(3); tick += 120; monitor.Poll();
        tick += 400; monitor.Poll();
        Check(messages.Count == 1, "Leaving excluded app does not replay");
        tick += 120; snapshot = Chinese(3, false); monitor.Poll();
        Check(messages.Count == 2, "Other application in same package still displays");
        Check(settings.TrySave([], [], out _), "Remove application rule during monitoring");
        window = 2; identity = after; snapshot = Chinese(2); tick += 120; monitor.Poll();
        tick += 1600; monitor.Poll();
        tick += 120; snapshot = Chinese(2, false); monitor.Poll();
        Check(messages.Count == 3, "Removing package rule restores subsequent notifications");
        Console.WriteLine("Package test data: " + root);
    }

    private static void RunCatalogTests(bool requirePhotos = false)
    {
        var apps = InstalledApplications.LoadAsync().WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
        Check(apps.Count > 0, "Real installed-app catalog is available");
        var photos = apps.SingleOrDefault(app => app.AppId == PhotosId);
        Check(photos is not null, "Photos is selectable by stable identity");
        Console.WriteLine($"Photos catalog: {photos!.Name}; {photos.AppId}; manifest paths: {photos.Executables.Length}");
        var verifiedWindows = 0;
        foreach (var process in Process.GetProcessesByName("Photos"))
        {
            using (process)
            {
                var identity = ApplicationIdentityReader.ReadProcess((uint)process.Id);
                Check(identity.Family == PhotosFamily && identity.AppId == PhotosId, "Actual running Photos process identity agrees with catalog");
                // Photos can also keep a background process with no visible window.
                // Such a process cannot be the foreground target under test.
                if (process.MainWindowHandle == 0)
                {
                    Console.WriteLine("Background Photos process has no foreground window; process identity verified.");
                    continue;
                }
                Check(ApplicationIdentityReader.Read(process.MainWindowHandle) == identity,
                    "Actual Photos window resolves to the same stable identity");
                Check(InstalledApplications.FindConversion(identity.Executable!, apps)?.AppId == PhotosId,
                    "Actual Photos executable can be explicitly converted to identity rule");
                var testFile = Path.Combine(AppContext.BaseDirectory, "artifacts", "photos-live-" + Guid.NewGuid().ToString("N") + ".json");
                var settings = new InputMethodExclusions(testFile);
                Check(settings.TrySave([], [photos.ToRule()], out _) && settings.Matches(identity),
                    "Saved Photos picker rule matches the actual running process");
                var queries = 0;
                var displayed = 0;
                using var monitor = new InputMethodMonitor(settings, () => process.MainWindowHandle,
                    read: () => { queries++; return Chinese(process.MainWindowHandle); });
                monitor.MessageRaised += _ => displayed++;
                for (var i = 0; i < 10; i++) monitor.Poll();
                Check(queries == 0 && displayed == 0,
                    "Real Photos HWND is excluded through production monitor without querying its IME");
                verifiedWindows++;
                Console.WriteLine("Running Photos: " + identity);
            }
        }
        if (requirePhotos) Check(verifiedWindows > 0, "At least one real Photos window was verified");
    }
}
