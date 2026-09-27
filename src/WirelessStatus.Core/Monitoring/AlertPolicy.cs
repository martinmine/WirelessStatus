using WirelessStatus.Core.Devices;
using WirelessStatus.Core.Settings;

namespace WirelessStatus.Core.Monitoring;

public sealed record LowBatteryAlert(BatteryReading Reading, int Threshold);

/// <summary>
/// Decides when to show a low-battery notification. Pure logic: feed it every refresh's readings.
/// <list type="bullet">
/// <item>Alerts once when a device is at or below the threshold, then stays quiet for the rest of that discharge cycle.</item>
/// <item>Re-arms when the device charges, or recovers to <see cref="RearmMargin"/> points above the threshold
/// (the margin stops a level wobbling around the threshold from alerting repeatedly).</item>
/// <item>A 0% reading only counts once it is seen twice in a row — some devices briefly report 0 while waking up.</item>
/// <item>Unavailable devices (off, asleep, disconnected) neither alert nor re-arm.</item>
/// </list>
/// </summary>
public sealed class AlertPolicy
{
    public const int RearmMargin = 5;

    private sealed class DeviceAlertState
    {
        public bool Alerted;
        public bool PendingZero;
    }

    private readonly Dictionary<string, DeviceAlertState> _states = [];

    public IReadOnlyList<LowBatteryAlert> Evaluate(IEnumerable<BatteryReading> readings, AppSettings settings)
    {
        var alerts = new List<LowBatteryAlert>();
        var threshold = settings.LowBatteryThreshold;

        foreach (var reading in readings)
        {
            if (reading.State != DeviceState.Connected || reading.Level is not { } level)
                continue;

            if (!_states.TryGetValue(reading.DeviceId, out var state))
                _states[reading.DeviceId] = state = new DeviceAlertState();

            if (reading.IsCharging == true || level >= threshold + RearmMargin)
            {
                state.Alerted = false;
                state.PendingZero = false;
                continue;
            }

            if (level > threshold || state.Alerted)
            {
                state.PendingZero = false;
                continue;
            }

            if (level == 0 && !state.PendingZero)
            {
                state.PendingZero = true;
                continue;
            }

            state.PendingZero = false;
            state.Alerted = true;

            var deviceSettings = settings.GetDevice(reading.DeviceId);
            if (deviceSettings.NotificationsEnabled && !deviceSettings.Hidden)
                alerts.Add(new LowBatteryAlert(reading, threshold));
        }

        return alerts;
    }
}
