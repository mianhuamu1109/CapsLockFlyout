using FluentFlyoutWPF.CapsFlyout;
using System.Diagnostics;

internal static partial class Program
{
    private static void RunExclusionTests()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "artifacts", "exclusions-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(directory, "ime-exclusions.json");
        var settings = new InputMethodExclusions(file);
        Check(!settings.HasEntries && settings.LoadError is null, "Missing config defaults to no exclusions");
        Check(!Directory.Exists(directory), "Reading missing config does not write to disk");
        var changes = 0;
        settings.Changed += () => changes++;
        const string game = @"D:\Games\示例游戏\Game.exe";
        const string editor = @"D:\Tools\Editor.exe";
        Check(settings.TrySave([game, game.ToUpperInvariant()], out _), "Save Unicode EXE path");
        Check(settings.Paths.Count == 1 && changes == 1, "Case-insensitive duplicate entries collapse");
        Check(settings.Contains(game.ToLowerInvariant()), "Case-insensitive full-path match");
        Check(settings.Contains(@"\\?\" + game), "Extended local path matches normal executable path");
        var network = new InputMethodExclusions(Path.Combine(directory, "network.json"));
        Check(network.TrySave([@"\\server\share\Game.exe"], out _) &&
            network.Contains(@"\\?\UNC\server\share\Game.exe"), "Extended UNC paths match normal UNC paths");
        Check(!settings.Contains(@"D:\Other\Game.exe"), "Same filename in another directory is not excluded");
        Check(!settings.Contains(null) && !settings.Contains("Game.exe"), "Unknown and relative targets are not excluded");
        Check(new InputMethodExclusions(file).Contains(game), "Exclusions survive reload");
        Check(!settings.TrySave(["relative.exe"], out var error) && error is not null, "Reject relative path with visible error");
        Check(!settings.TrySave([@"D:\Games\Game.txt"], out _), "Reject non-EXE entry");
        Check(settings.Contains(game) && changes == 1, "Failed validation retains old settings without notification");
        Check(new InputMethodExclusions(file).Contains(game), "Failed validation retains old disk configuration");
        Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "No temporary settings file left behind");

        var brokenFile = Path.Combine(directory, "broken.json");
        File.WriteAllText(brokenFile, "{ broken");
        var broken = new InputMethodExclusions(brokenFile);
        Check(broken.LoadError is not null && !broken.HasEntries, "Malformed settings do not crash startup");
        Check(!broken.TrySave([editor], out _) && File.ReadAllText(brokenFile) == "{ broken",
            "Malformed configuration is preserved, not overwritten");
        var parentFile = Path.Combine(directory, "not-a-directory");
        File.WriteAllText(parentFile, "test");
        var unwritable = new InputMethodExclusions(Path.Combine(parentFile, "settings.json"));
        Check(!unwritable.TrySave([game], out _) && !unwritable.HasEntries, "Failed disk save does not publish unsaved exclusions");

        nint window = 1;
        long tick = 10000;
        var snapshot = Chinese(window);
        var reads = 0;
        var queries = 0;
        var hideRequests = 0;
        var messages = new List<OsdMessage>();
        string? Resolve(nint target) { queries++; return target == 2 ? game : editor; }
        using var monitor = new InputMethodMonitor(settings, () => window, Resolve,
            () => { reads++; return snapshot; }, () => tick);
        monitor.MessageRaised += messages.Add;
        monitor.HideInputMethodRequested += () => hideRequests++;
        void Sample(long time, nint target, InputMethodSnapshot value)
        {
            tick = time; window = target; snapshot = value; monitor.Poll();
        }
        Sample(10000, 1, Chinese(1));
        Sample(10400, 1, Chinese(1));
        Sample(10520, 1, Chinese(1, false));
        Check(messages.Count == 1 && monitor.CanPresent(messages[0]), "Unlisted application still shows IME changes");
        var beforeExcludedReads = reads;
        Sample(10640, 2, Japanese(2));
        Check(hideRequests == 1, "Entering excluded application requests removal of visible IME card");
        Check(!monitor.CanPresent(messages[0]), "Entering excluded application rejects old queued card");
        for (var i = 0; i < 1000; i++)
            Sample(10760 + i * 120, 2, i % 2 == 0 ? Japanese(2, 3) : Chinese(2));
        Check(reads == beforeExcludedReads && messages.Count == 1, "1000 excluded samples never query IME or display changes");
        Check(hideRequests == 1, "Stable excluded application does not flood UI hide requests");
        Sample(131000, 1, Chinese(1));
        Sample(131240, 1, Chinese(1, false));
        Sample(131400, 1, Chinese(1, false));
        Check(messages.Count == 1, "Leaving exclusion restores baseline silently, without replay");
        Sample(131520, 1, Chinese(1));
        Check(messages.Count == 2, "Normal IME notifications resume in an unlisted application");

        var pending = messages[^1];
        Check(settings.TrySave([game, editor], out _), "Add current application without restarting monitor");
        Check(!monitor.CanPresent(pending), "Settings change immediately invalidates queued IME card before next poll");
        Sample(131640, 1, Chinese(1, false));
        Check(hideRequests == 2 && messages.Count == 2, "Adding current app hides card and suppresses changes");
        Check(settings.TrySave([game], out _), "Remove current app from exclusions");
        Sample(131760, 1, Chinese(1, false));
        Sample(133200, 1, Chinese(1, false));
        Check(messages.Count == 2, "Removing exclusion in same window does not replay blocked mode");
        Sample(133320, 1, Chinese(1));
        Check(messages.Count == 3 && monitor.CanPresent(messages[^1]), "Same-window notification resumes after removing exclusion");

        var stale = messages[^1];
        Sample(133440, 2, Japanese(2));
        Sample(133560, 1, Chinese(1));
        Sample(133960, 1, Chinese(1));
        Check(!monitor.CanPresent(stale), "Returning to original window cannot revive stale queued card");
        Check(settings.TrySave([], out _) && !settings.HasEntries, "Removing all entries restores default policy");
        var beforeEmptyQueries = queries;
        Sample(134080, 1, Chinese(1, false));
        Sample(135600, 1, Chinese(1, false));
        Sample(135720, 1, Chinese(1));
        Check(queries == beforeEmptyQueries, "Empty exclusion list performs no process path queries");
        Check(new InputMethodExclusions(file).Paths.Count == 0, "Empty exclusion list persists across restart");
        var beforeDispose = reads;
        monitor.Dispose();
        monitor.Poll();
        Check(reads == beforeDispose && !monitor.CanPresent(messages[^1]), "Disposed monitor cannot query or present");

        using var unknownMonitor = new InputMethodMonitor(new InputMethodExclusions(brokenFile),
            () => 1, _ => null, () => Chinese(1), () => 20000);
        unknownMonitor.Poll();
        Check(ForegroundExecutable.Read(0) is null && ForegroundExecutable.ReadProcess(0) is null,
            "Invalid HWND or inaccessible process is safely ignored");
        var ownPath = ForegroundExecutable.ReadProcess((uint)Environment.ProcessId);
        Check(string.Equals(ownPath, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase),
            "Windows API resolves actual test-process executable path");
        using var form = new Form();
        Check(string.Equals(ForegroundExecutable.Read(form.Handle), ownPath, StringComparison.OrdinalIgnoreCase),
            "Windows HWND-to-process query resolves actual owned window");
        var timer = Stopwatch.StartNew();
        for (var i = 0; i < 1000; i++) ForegroundExecutable.ReadProcess((uint)Environment.ProcessId);
        timer.Stop();
        Console.WriteLine($"Process-path query: 1000 calls in {timer.Elapsed.TotalMilliseconds:F1} ms (local diagnostic, not a cross-device benchmark).");
        Console.WriteLine("Exclusion test data: " + directory);
    }
}
