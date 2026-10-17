namespace FluentFlyoutWPF.CapsFlyout;

// ReadForegroundSnapshot / Changed from the supplied Windhawk mod:
// independent 120 ms polling, with no keyboard subscription or gesture queue.
internal sealed class InputMethodMonitor : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private Task? _worker;
    private long _quietUntil;
    private int _generation;
    private volatile bool _disposed;
    internal event Action<OsdMessage>? MessageRaised;
    internal bool IsSuppressed => _disposed ||
        Environment.TickCount64 < Interlocked.Read(ref _quietUntil);

    internal void Start() => _worker ??= Task.Run(RunAsync);

    internal void Rebaseline()
    {
        Interlocked.Exchange(ref _quietUntil, Environment.TickCount64 + 1500);
        Interlocked.Increment(ref _generation);
    }

    private async Task RunAsync()
    {
        InputMethodSnapshot previous = default;
        var generation = Volatile.Read(ref _generation);
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var observedGeneration = Volatile.Read(ref _generation);
                var now = InputMethodStateReader.Read();
                if (now.Valid)
                {
                    var baseline = !previous.Valid || generation != observedGeneration ||
                        observedGeneration != Volatile.Read(ref _generation) ||
                        Environment.TickCount64 < Interlocked.Read(ref _quietUntil);
                    var changed = !previous.SameDisplay(now);
                    previous = now;
                    generation = observedGeneration;
                    if (!baseline && changed && !_disposed &&
                        generation == Volatile.Read(ref _generation))
                        MessageRaised?.Invoke(now.ToMessage());
                }
                else previous = default;
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
