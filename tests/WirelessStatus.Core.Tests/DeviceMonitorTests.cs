using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using WirelessStatus.Core.Devices;
using WirelessStatus.Core.Monitoring;

namespace WirelessStatus.Core.Tests;

public sealed class DeviceMonitorTests : IAsyncDisposable
{
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-09-27T12:00:00Z"));
    private DeviceMonitor? _monitor;

    public async ValueTask DisposeAsync()
    {
        if (_monitor is not null)
            await _monitor.DisposeAsync();
    }

    private DeviceMonitor CreateMonitor(TimeSpan pollInterval, params IBatteryProvider[] providers) =>
        _monitor = new DeviceMonitor(providers, pollInterval, _time);

    private static BatteryReading Reading(string id, int level) =>
        new(id, id, DeviceKind.Unknown, DeviceState.Connected, level, null);

    [Fact]
    public async Task Refresh_combines_providers_in_order_and_publishes()
    {
        var monitor = CreateMonitor(Hour,
            new FakeProvider("a", () => [Reading("a1", 10), Reading("a2", 20)]),
            new FakeProvider("b", () => [Reading("b1", 30)]));
        MonitorSnapshot? published = null;
        monitor.Updated += (_, s) => published = s;

        var snapshot = await monitor.RefreshAsync();

        Assert.Equal(["a1", "a2", "b1"], snapshot.Readings.Select(r => r.DeviceId));
        Assert.Equal(_time.GetUtcNow(), snapshot.UpdatedAt);
        Assert.Same(snapshot, monitor.Current);
        Assert.Same(snapshot, published);
    }

    [Fact]
    public async Task Failing_provider_keeps_its_previous_readings()
    {
        var calls = 0;
        var flaky = new FakeProvider("flaky", () => ++calls == 1 ? [Reading("f", 50)] : throw new InvalidOperationException("boom"));
        var monitor = CreateMonitor(Hour, flaky, new FakeProvider("ok", () => [Reading("ok", 80)]));

        await monitor.RefreshAsync();
        var second = await monitor.RefreshAsync();

        Assert.Equal(["f", "ok"], second.Readings.Select(r => r.DeviceId));
    }

    [Fact]
    public async Task Hanging_provider_times_out_without_blocking_others()
    {
        var hanging = new FakeProvider("hang", async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return [];
        });
        var monitor = CreateMonitor(Hour, hanging, new FakeProvider("ok", () => [Reading("ok", 80)]));

        var refresh = monitor.RefreshAsync();
        await Eventually(() => refresh.IsCompleted, step: () => _time.Advance(monitor.ProviderTimeout));

        Assert.Equal(["ok"], (await refresh).Readings.Select(r => r.DeviceId));
        Assert.True(hanging.LastToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Polls_again_after_the_interval()
    {
        var provider = new FakeProvider("p", () => [Reading("p", 50)]);
        var monitor = CreateMonitor(TimeSpan.FromMinutes(5), provider);

        monitor.Start();
        await Eventually(() => provider.Calls == 1);
        await Eventually(() => provider.Calls >= 2, step: () => _time.Advance(TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public async Task Burst_of_refresh_requests_triggers_one_debounced_refresh()
    {
        var provider = new FakeProvider("p", () => [Reading("p", 50)]);
        var monitor = CreateMonitor(Hour, provider);
        monitor.Start();
        await Eventually(() => provider.Calls == 1);

        for (var i = 0; i < 5; i++)
            monitor.RequestRefresh();

        // Nothing happens before the debounce elapses.
        await Task.Delay(50);
        Assert.Equal(1, provider.Calls);

        await Eventually(() => provider.Calls == 2, step: () => _time.Advance(DeviceMonitor.RefreshDebounce));
        await Task.Delay(100);
        Assert.Equal(2, provider.Calls);
    }

    [Fact]
    public async Task Shorter_poll_interval_takes_effect_without_waiting_for_the_old_one()
    {
        var provider = new FakeProvider("p", () => [Reading("p", 50)]);
        var monitor = CreateMonitor(Hour, provider);
        monitor.Start();
        await Eventually(() => provider.Calls == 1);

        monitor.PollInterval = TimeSpan.FromMinutes(1);

        await Eventually(() => provider.Calls == 2, step: () => _time.Advance(TimeSpan.FromMinutes(1)));
        Assert.True(_time.GetUtcNow() - DateTimeOffset.Parse("2026-09-27T12:00:00Z") < Hour);
    }

    [Fact]
    public async Task Throwing_update_handler_does_not_stop_polling()
    {
        var provider = new FakeProvider("p", () => [Reading("p", 50)]);
        var monitor = CreateMonitor(TimeSpan.FromMinutes(1), provider);
        monitor.Updated += (_, _) => throw new InvalidOperationException("handler bug");

        monitor.Start();
        await Eventually(() => provider.Calls >= 2, step: () => _time.Advance(TimeSpan.FromMinutes(1)));
    }

    /// <summary>
    /// Waits for a condition set by the monitor's background loop. <paramref name="step"/> (typically advancing fake time)
    /// is repeated because the loop may not have registered its timer yet when the test first advances.
    /// </summary>
    private static async Task Eventually(Func<bool> condition, Action? step = null)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > TimeSpan.FromSeconds(5))
                Assert.Fail("Condition was not met within 5 seconds.");
            step?.Invoke();
            await Task.Delay(20);
        }
    }

    private sealed class FakeProvider(string name, Func<CancellationToken, Task<IReadOnlyList<BatteryReading>>> read) : IBatteryProvider
    {
        private int _calls;

        public FakeProvider(string name, Func<IReadOnlyList<BatteryReading>> read)
            : this(name, _ => Task.FromResult(read()))
        {
        }

        public string Name => name;

        public int Calls => Volatile.Read(ref _calls);

        public CancellationToken LastToken { get; private set; }

        public Task<IReadOnlyList<BatteryReading>> ReadAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            LastToken = cancellationToken;
            return read(cancellationToken);
        }
    }
}
