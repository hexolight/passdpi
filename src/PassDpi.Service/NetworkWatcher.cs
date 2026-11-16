using System.Net.NetworkInformation;

namespace PassDpi.Service;

/// <summary>
/// Turns Windows network-change notifications into one debounced callback.
/// No polling: a one-shot timer is armed only when an event arrives.
/// </summary>
public sealed class NetworkWatcher : IDisposable
{
    private readonly Func<Task> _onChanged;
    private readonly Timer _debounce;
    private readonly TimeSpan _delay;
    private int _running;

    public NetworkWatcher(Func<Task> onChanged, TimeSpan delay)
    {
        _onChanged = onChanged;
        _delay = delay;
        _debounce = new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);
        NetworkChange.NetworkAddressChanged += OnEvent;
        NetworkChange.NetworkAvailabilityChanged += OnEvent;
    }

    private void OnEvent(object? sender, EventArgs e) => _debounce.Change(_delay, Timeout.InfiniteTimeSpan);

    private async void Fire()
    {
        // If a previous callback is still running, re-arm instead of overlapping.
        if (Interlocked.Exchange(ref _running, 1) == 1) { _debounce.Change(_delay, Timeout.InfiniteTimeSpan); return; }
        try { await _onChanged().ConfigureAwait(false); }
        catch { /* the controller logs its own errors */ }
        finally { Volatile.Write(ref _running, 0); }
    }

    public void Dispose()
    {
        NetworkChange.NetworkAddressChanged -= OnEvent;
        NetworkChange.NetworkAvailabilityChanged -= OnEvent;
        _debounce.Dispose();
    }
}
