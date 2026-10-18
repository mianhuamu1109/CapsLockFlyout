using FluentFlyoutWPF.CapsFlyout;

internal static partial class Program
{
    private static int _checks;
    private static void Check(bool passed, string name)
    {
        _checks++;
        if (!passed) throw new InvalidOperationException("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }

    private static InputMethodSnapshot Chinese(nint window, bool native = true, nint focus = 100) =>
        new(window, focus, 0x804, true, native ? 1u : 0u);
    private static InputMethodSnapshot English(nint window) => new(window, 100, 0x409, false, 0);
    private static InputMethodSnapshot Japanese(nint window, uint mode = 1, bool open = true) =>
        new(window, 100, 0x411, open, mode);

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            if (args.Length == 2 && args[0] == "--editor")
            {
                Application.Run(new LiveEditor(args[1]));
                return 0;
            }
            RunSequenceTests();
            if (args.Contains("--live")) RunLiveTests();
            Console.WriteLine($"All {_checks} checks passed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void RunSequenceTests()
    {
        var tracker = new InputMethodChangeTracker();
        Check(!tracker.Observe(1, Chinese(1), 0), "Startup baseline is silent");
        Check(!tracker.Observe(1, Chinese(1, false), 120), "Startup settling only updates baseline");
        Check(!tracker.Observe(1, Chinese(1, false), 400), "End of quiet period has no replay");
        Check(tracker.Observe(1, Chinese(1), 420), "Chinese change within stable window is shown");
        var epochA = tracker.Epoch;
        Check(tracker.IsCurrent(epochA, 1, 420), "Current notification is accepted");

        Check(!tracker.Observe(2, English(2), 500), "Switch to English window is silent");
        Check(!tracker.IsCurrent(epochA, 1, 501), "Old window notification is rejected");
        Check(!tracker.Observe(2, Chinese(2), 620), "Delayed automatic language restore is silent");
        Check(!tracker.Observe(2, Chinese(2, false), 740), "Delayed mode restore is also silent");
        Check(!tracker.Observe(2, Chinese(2, false), 899), "No notification just before deadline");
        Check(!tracker.Observe(2, Chinese(2, false), 900), "Deadline does not replay restored mode");
        Check(tracker.Observe(2, Chinese(2), 901), "First real mode change after deadline is shown");
        Check(tracker.Observe(2, Japanese(2), 1020), "Language change in stable window is shown");
        Check(tracker.Observe(2, Japanese(2, 3), 1140), "Japanese katakana change is shown");
        Check(tracker.Observe(2, Japanese(2, 0, false), 1260), "Japanese direct-input change is shown");
        Check(!tracker.Observe(2, Japanese(2, 0, false), 1380), "Unchanged state stays silent");

        Check(!tracker.Observe(1, Chinese(1), 1500), "Returning to earlier window is silent");
        Check(!tracker.IsCurrent(epochA, 1, 2000), "Old queued notification stays invalid after return");
        Check(!tracker.Observe(1, Chinese(1), 1900), "Returning window baseline is not replayed");
        var epochBeforeFocus = tracker.Epoch;
        Check(!tracker.Observe(1, Chinese(1, true, 200), 2020), "Child focus change alone is silent");
        Check(tracker.Epoch == epochBeforeFocus, "Child focus does not restart window quiet interval");
        Check(tracker.Observe(1, Chinese(1, false, 200), 2021), "Child focus does not suppress later mode change");

        Check(!tracker.Observe(3, default, 2100), "Window switch with invalid query is silent");
        Check(!tracker.Observe(3, Japanese(3), 2300), "First valid state during quiet period is baseline");
        Check(!tracker.Observe(3, Japanese(3, 3), 2499), "Last-moment restore before quiet deadline is silent");
        Check(!tracker.Observe(3, Japanese(3, 3), 2500), "Restored Japanese state is not replayed");
        Check(tracker.Observe(3, Japanese(3), 2501), "Japanese mode changes resume afterwards");

        Check(!tracker.Observe(3, default, 2600), "Invalid query does not display");
        Check(!tracker.Observe(3, Japanese(3, 3), 2720), "Recovery from invalid query rebuilds baseline");
        Check(tracker.Observe(3, Japanese(3), 2840), "Recovery does not permanently suppress changes");
        Check(!tracker.Observe(3, Japanese(3, 3), 2960, true), "Session reset is silent");
        Check(!tracker.Observe(3, Japanese(3, 0, false), 3080, true), "Session quiet period updates baseline");
        Check(!tracker.Observe(3, Japanese(3, 0, false), 3200), "Session resume does not replay baseline");

        Check(!tracker.Observe(4, Chinese(3), 3300), "Snapshot from another foreground is rejected");
        Check(!tracker.Observe(4, Chinese(4), 3700), "Cross-window query recovery is baseline only");
        Check(tracker.Observe(4, Chinese(4, false), 3820), "Cross-window recovery resumes notifications");
        Check(!tracker.Observe(0, default, 3900), "No foreground is silent");
        Check(!tracker.Observe(4, Chinese(4), 4020), "Foreground recovery starts new quiet period");
        Check(!tracker.IsCurrent(tracker.Epoch, 4, 4419), "Queued display blocked until deadline");
        Check(!tracker.Observe(4, Chinese(4), 4420), "Foreground recovery has no deferred popup");

        var rapid = new InputMethodChangeTracker();
        var allSilent = true;
        for (var i = 0; i < 1000; i++)
        {
            nint target = (i % 2) + 1;
            allSilent &= !rapid.Observe(target, i % 2 == 0 ? Chinese(target) : English(target), i * 120);
        }
        Check(allSilent, "1000 rapid alternating window switches stay silent");
        Check(!rapid.Observe(2, English(2), 120400), "Rapid switching stops without replay");
        Check(rapid.Observe(2, Chinese(2), 120520), "Normal switching resumes after rapid foreground changes");
    }
}
