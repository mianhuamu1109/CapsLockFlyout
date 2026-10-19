namespace FluentFlyoutWPF.CapsFlyout;

// ReadForegroundSnapshot / Changed from the supplied Windhawk mod:
// independent 120 ms polling, with no keyboard subscription or gesture queue.
internal sealed class InputMethodMonitor : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly InputMethodChangeTracker _changes = new();
    private readonly InputMethodExclusions _exclusions;
    private readonly Func<nint> _foreground;
    private readonly Func<nint, string?> _executable;
    private readonly Func<InputMethodSnapshot> _read;
    private readonly Func<long> _tick;
    private int _observedGeneration;
    private bool _wasExcluded;
    private Task? _worker;
    private long _quietUntil;
    private int _generation;
    private volatile bool _disposed;
    internal event Action<OsdMessage>? MessageRaised;
    internal event Action? HideInputMethodRequested;
    internal bool IsSuppressed => _disposed ||
        _tick() < Interlocked.Read(ref _quietUntil) ||
        _tick() < _changes.QuietUntil;

    internal InputMethodMonitor(InputMethodExclusions? exclusions = null,
        Func<nint>? foreground = null, Func<nint, string?>? executable = null,
        Func<InputMethodSnapshot>? read = null, Func<long>? tick = null)
    {
        _exclusions = exclusions ?? InputMethodExclusions.Current;
        _foreground = foreground ?? ImeNative.GetForegroundWindow;
        _executable = executable ?? ForegroundExecutable.Read;
        _read = read ?? InputMethodStateReader.Read;
        _tick = tick ?? (() => Environment.TickCount64);
        _exclusions.Changed += Rebaseline;
    }

    private bool IsExcluded(nint foreground) =>
        _exclusions.HasEntries && _exclusions.Contains(_executable(foreground));

    internal bool CanPresent(OsdMessage message) => !IsSuppressed &&
        message.InputMethodSession == Volatile.Read(ref _generation) &&
        _changes.IsCurrent(message.InputMethodEpoch, message.InputMethodForeground,
            _tick()) &&
        _foreground() == message.InputMethodForeground &&
        !IsExcluded(message.InputMethodForeground) &&
        _foreground() == message.InputMethodForeground;

    internal void Start() => _worker ??= Task.Run(RunAsync);

    internal void Rebaseline()
    {
        Interlocked.Exchange(ref _quietUntil, _tick() + 1500);
        Interlocked.Increment(ref _generation);
    }

    private async Task RunAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                Poll();
                await Task.Delay(120, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }

    // Also driven deterministically by regression tests, using the same production path.
    internal void Poll()
    {
        if (_disposed) return;
        var generation = Volatile.Read(ref _generation);
        var foreground = _foreground();
        var excluded = IsExcluded(foreground);
        if (excluded && !_wasExcluded)
        {
            generation = Interlocked.Increment(ref _generation);
            HideInputMethodRequested?.Invoke();
        }
        _wasExcluded = excluded;
        // Do not contact the IME of excluded applications. An invalid baseline
        // also ensures that leaving the list or the app never replays a mode.
        var now = excluded ? default : _read();
        var tick = _tick();
        var baseline = excluded || _observedGeneration != generation ||
            generation != Volatile.Read(ref _generation) ||
            tick < Interlocked.Read(ref _quietUntil);
        var changed = _changes.Observe(foreground, now, tick, baseline);
        _observedGeneration = generation;
        if (changed && !_disposed && generation == Volatile.Read(ref _generation))
        {
            var message = now.ToMessage() with
            {
                InputMethodForeground = foreground,
                InputMethodEpoch = _changes.Epoch,
                InputMethodSession = generation
            };
            if (CanPresent(message)) MessageRaised?.Invoke(message);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _exclusions.Changed -= Rebaseline;
        _stop.Cancel();
        if (_worker is not null)
            _ = _worker.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
        else _stop.Dispose();
    }
}
