namespace FluentFlyoutWPF.CapsFlyout;

// Owned by the polling worker. Only the atomic notification guards are read by UI.
// A foreground change starts a fixed quiet interval; observations still advance
// the baseline during that interval, so there is nothing to replay afterwards.
internal sealed class InputMethodChangeTracker
{
    internal const int WindowQuietMilliseconds = 400;
    private InputMethodSnapshot _previous;
    private long _foreground;
    private long _epoch;
    private long _quietUntil;

    internal long Epoch => Interlocked.Read(ref _epoch);
    internal long QuietUntil => Interlocked.Read(ref _quietUntil);

    internal bool Observe(nint foreground, InputMethodSnapshot current, long tick,
        bool rebaseline = false)
    {
        if (Interlocked.Read(ref _foreground) != (long)foreground)
        {
            Interlocked.Increment(ref _epoch);
            Interlocked.Exchange(ref _foreground, (long)foreground);
            Interlocked.Exchange(ref _quietUntil, tick + WindowQuietMilliseconds);
            _previous = default;
        }

        if (!current.Valid || current.Foreground != foreground)
        {
            _previous = default;
            return false;
        }

        var baseline = rebaseline || !_previous.Valid || tick < QuietUntil;
        var changed = !_previous.SameDisplay(current);
        _previous = current;
        return !baseline && changed;
    }

    internal bool IsCurrent(long epoch, nint foreground, long tick) =>
        foreground != 0 && epoch == Epoch &&
        (long)foreground == Interlocked.Read(ref _foreground) && tick >= QuietUntil;
}
