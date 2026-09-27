using WirelessStatus.Core.Devices;
using WirelessStatus.Core.Monitoring;
using WirelessStatus.Core.Settings;

namespace WirelessStatus.Core.Tests;

public class AlertPolicyTests
{
    private const string Mouse = "razer:deathadder-v4-pro";
    private readonly AlertPolicy _policy = new();
    private readonly AppSettings _settings = new() { LowBatteryThreshold = 10 };

    private static BatteryReading Reading(int? level, bool? charging = false, DeviceState state = DeviceState.Connected, string id = Mouse) =>
        new(id, "Mouse", DeviceKind.Mouse, state, level, charging);

    private IReadOnlyList<LowBatteryAlert> Feed(params BatteryReading[] readings) => _policy.Evaluate(readings, _settings);

    [Fact]
    public void Alerts_once_when_level_reaches_threshold()
    {
        Assert.Empty(Feed(Reading(12)));
        var alert = Assert.Single(Feed(Reading(10)));
        Assert.Equal(10, alert.Threshold);
        Assert.Equal(10, alert.Reading.Level);

        Assert.Empty(Feed(Reading(8)));
        Assert.Empty(Feed(Reading(3)));
    }

    [Fact]
    public void Alerts_immediately_when_first_reading_is_already_low()
    {
        Assert.Single(Feed(Reading(7)));
    }

    [Fact]
    public void Does_not_alert_above_threshold()
    {
        Assert.Empty(Feed(Reading(100)));
        Assert.Empty(Feed(Reading(11)));
    }

    [Fact]
    public void Charging_rearms_the_alert()
    {
        Assert.Single(Feed(Reading(9)));
        Assert.Empty(Feed(Reading(9, charging: true)));
        Assert.Single(Feed(Reading(9)));
    }

    [Fact]
    public void Does_not_alert_while_charging()
    {
        Assert.Empty(Feed(Reading(5, charging: true)));
    }

    [Fact]
    public void Rearms_only_after_recovering_past_the_margin()
    {
        Assert.Single(Feed(Reading(10)));

        // Wobbling just above the threshold must not produce another alert.
        Assert.Empty(Feed(Reading(11)));
        Assert.Empty(Feed(Reading(10)));

        Assert.Empty(Feed(Reading(10 + AlertPolicy.RearmMargin)));
        Assert.Single(Feed(Reading(10)));
    }

    [Fact]
    public void Single_zero_reading_is_ignored()
    {
        Assert.Empty(Feed(Reading(0)));
        Assert.Empty(Feed(Reading(50)));
    }

    [Fact]
    public void Zero_seen_twice_in_a_row_alerts()
    {
        Assert.Empty(Feed(Reading(0)));
        Assert.Single(Feed(Reading(0)));
    }

    [Fact]
    public void Unavailable_device_neither_alerts_nor_rearms()
    {
        Assert.Single(Feed(Reading(8)));
        Assert.Empty(Feed(Reading(null, charging: null, state: DeviceState.Unavailable)));
        Assert.Empty(Feed(Reading(8)));
    }

    [Fact]
    public void Tracks_devices_independently()
    {
        var alerts = Feed(Reading(5, id: "a"), Reading(50, id: "b"));
        Assert.Equal("a", Assert.Single(alerts).Reading.DeviceId);

        alerts = Feed(Reading(5, id: "a"), Reading(5, id: "b"));
        Assert.Equal("b", Assert.Single(alerts).Reading.DeviceId);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Respects_per_device_settings(bool notificationsEnabled, bool hidden)
    {
        var settings = _settings with
        {
            Devices = new() { [Mouse] = new DeviceSettings { NotificationsEnabled = notificationsEnabled, Hidden = hidden } },
        };

        Assert.Empty(_policy.Evaluate([Reading(5)], settings));
    }

    [Fact]
    public void Uses_threshold_from_settings()
    {
        var settings = _settings with { LowBatteryThreshold = 20 };
        Assert.Single(_policy.Evaluate([Reading(18)], settings));
    }
}
