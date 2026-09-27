using WirelessStatus.Core.Devices;

namespace WirelessStatus.Core.Monitoring;

public sealed record MonitorSnapshot(IReadOnlyList<BatteryReading> Readings, DateTimeOffset UpdatedAt);

/// <summary>
/// Polls all providers on an interval and publishes the combined readings.
/// Providers run in parallel on the thread pool (HID calls block), each with a timeout; a provider that fails or times
/// out keeps its previous readings. <see cref="Updated"/> is raised on a thread-pool thread.
/// </summary>
public sealed class DeviceMonitor : IAsyncDisposable
{
    /// <summary>Hot-plug notifications arrive in bursts; wait this long after the first before refreshing.</summary>
    public static readonly TimeSpan RefreshDebounce = TimeSpan.FromSeconds(2);

    private enum WakeReason { None, IntervalChanged, RefreshRequested }

    private readonly IReadOnlyList<IBatteryProvider> _providers;
    private readonly TimeProvider _time;
    private readonly Action<string>? _log;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly Dictionary<IBatteryProvider, IReadOnlyList<BatteryReading>> _lastByProvider = [];
    private readonly Lock _wakeLock = new();
    private TaskCompletionSource<WakeReason> _wake = NewWakeSignal();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private TimeSpan _pollInterval;

    public DeviceMonitor(IEnumerable<IBatteryProvider> providers, TimeSpan pollInterval, TimeProvider? timeProvider = null, Action<string>? log = null)
    {
        _providers = providers.ToList();
        _pollInterval = pollInterval;
        _time = timeProvider ?? TimeProvider.System;
        _log = log;
    }

    public event EventHandler<MonitorSnapshot>? Updated;

    public MonitorSnapshot? Current { get; private set; }

    public TimeSpan ProviderTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan PollInterval
    {
        get => _pollInterval;
        set
        {
            if (value <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(value));
            _pollInterval = value;
            Wake(WakeReason.IntervalChanged);
        }
    }

    /// <summary>Starts polling. The first refresh happens immediately.</summary>
    public void Start()
    {
        if (_loop is not null)
            throw new InvalidOperationException("Already started.");
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    /// <summary>Asks for a refresh soon (debounced), e.g. after a device was plugged in or removed.</summary>
    public void RequestRefresh() => Wake(WakeReason.RefreshRequested);

    /// <summary>Reads all providers now and publishes the result. Concurrent calls are serialized.</summary>
    public async Task<MonitorSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            var results = await Task.WhenAll(_providers.Select(p => ReadProviderAsync(p, cancellationToken)));
            for (var i = 0; i < _providers.Count; i++)
            {
                if (results[i] is { } readings)
                    _lastByProvider[_providers[i]] = readings;
            }

            var snapshot = new MonitorSnapshot(
                _providers.SelectMany(p => _lastByProvider.GetValueOrDefault(p) ?? []).ToList(),
                _time.GetUtcNow());
            Current = snapshot;
            Updated?.Invoke(this, snapshot);
            return snapshot;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is null)
            return;
        await _cts.CancelAsync();
        try
        {
            await _loop!;
        }
        catch (OperationCanceledException)
        {
        }
        _cts.Dispose();
        _cts = null;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            // Requests that arrived before this point (e.g. during the debounce) are served by this refresh.
            lock (_wakeLock)
            {
                if (_wake.Task.IsCompleted)
                    _wake = NewWakeSignal();
            }

            try
            {
                await RefreshAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Most likely a throwing Updated handler; keep polling.
                _log?.Invoke($"Monitor: refresh failed: {ex}");
            }

            var reason = await WaitForWakeAsync(cancellationToken);
            if (reason == WakeReason.RefreshRequested)
                await Task.Delay(RefreshDebounce, _time, cancellationToken);
        }
    }

    private async Task<WakeReason> WaitForWakeAsync(CancellationToken cancellationToken)
    {
        Task<WakeReason> wake;
        lock (_wakeLock)
            wake = _wake.Task;

        // Wait for the poll interval, but let Wake() cut it short.
        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delay = Task.Delay(_pollInterval, _time, delayCts.Token);
        var completed = await Task.WhenAny(delay, wake);
        await delayCts.CancelAsync();
        cancellationToken.ThrowIfCancellationRequested();

        if (completed != wake)
            return WakeReason.None;

        lock (_wakeLock)
            _wake = NewWakeSignal();
        var reason = await wake;

        // An interval change only restarts the wait; it doesn't need a refresh.
        return reason == WakeReason.IntervalChanged
            ? await WaitForWakeAsync(cancellationToken)
            : reason;
    }

    private void Wake(WakeReason reason)
    {
        lock (_wakeLock)
            _wake.TrySetResult(reason);
    }

    private async Task<IReadOnlyList<BatteryReading>?> ReadProviderAsync(IBatteryProvider provider, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            return await Task.Run(() => provider.ReadAsync(timeoutCts.Token), timeoutCts.Token)
                .WaitAsync(ProviderTimeout, _time, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException)
        {
            await timeoutCts.CancelAsync();
            _log?.Invoke($"Monitor: {provider.Name} timed out after {ProviderTimeout.TotalSeconds:0}s");
            return null;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Monitor: {provider.Name} failed: {ex.Message}");
            return null;
        }
    }

    private static TaskCompletionSource<WakeReason> NewWakeSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
