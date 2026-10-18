namespace FluentFlyoutWPF.CapsFlyout;

// ReadForegroundSnapshot / Changed from the supplied Windhawk mod:
// independent 120 ms polling, with no keyboard subscription or gesture queue.
internal sealed class InputMethodMonitor : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly InputMethodChangeTracker _changes = new();
    private Task? _worker;
    private long _quietUntil;
    private int _generation;
    private volatile bool _disposed;
    internal event Action<OsdMessage>? MessageRaised;
    internal bool IsSuppressed => _disposed ||
        Environment.TickCount64 < Interlocked.Read(ref _quietUntil) ||
        Environment.TickCount64 < _changes.QuietUntil;

    internal bool CanPresent(OsdMessage message) => !IsSuppressed &&
        message.InputMethodSession == Volatile.Read(ref _generation) &&
        _changes.IsCurrent(message.InputMethodEpoch, message.InputMethodForeground,
            Environment.TickCount64) &&
        ImeNative.GetForegroundWindow() == message.InputMethodForeground;

    internal void Start() => _worker ??= Task.Run(RunAsync);

    internal void Rebaseline()
    {
        Interlocked.Exchange(ref _quietUntil, Environment.TickCount64 + 1500);
        Interlocked.Increment(ref _generation);
    }

    private async Task RunAsync()
    {
        var generation = Volatile.Read(ref _generation);
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var observedGeneration = Volatile.Read(ref _generation);
                // Track the top-level foreground even if this IME sample is invalid.
                // Child-control focus changes alone do not start a quiet interval.
                var foreground = ImeNative.GetForegroundWindow();
                var now = InputMethodStateReader.Read();
                var tick = Environment.TickCount64;
                var baseline = generation != observedGeneration ||
                    observedGeneration != Volatile.Read(ref _generation) ||
                    tick < Interlocked.Read(ref _quietUntil);
                var changed = _changes.Observe(foreground, now, tick, baseline);
                generation = observedGeneration;
                if (changed && !_disposed &&
                    generation == Volatile.Read(ref _generation))
                {
                    var message = now.ToMessage() with
                    {
                        InputMethodForeground = foreground,
                        InputMethodEpoch = _changes.Epoch,
                        InputMethodSession = generation
                    };
                    if (CanPresent(message)) MessageRaised?.Invoke(message);
                }
                await Task.Delay(120, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        if (_worker is not null)
            _ = _worker.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
        else _stop.Dispose();
    }
}
