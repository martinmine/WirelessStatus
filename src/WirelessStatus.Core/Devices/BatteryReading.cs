namespace WirelessStatus.Core.Devices;

public enum DeviceKind
{
    Unknown,
    Headset,
    Mouse,
    Keyboard,
    Controller,

    /// <summary>A Bluetooth device whose specific type has not been determined.</summary>
    Bluetooth,
}

public enum DeviceState
{
    /// <summary>Device answered and reported a battery level.</summary>
    Connected,

    /// <summary>Receiver/dongle is present but the device itself did not answer (off, asleep, out of range).</summary>
    Unavailable,
}

/// <param name="DeviceId">Stable id used for settings and alert tracking, e.g. "razer:1532:00BF".</param>
/// <param name="Level">Battery level 0–100, or null when unknown.</param>
/// <param name="IsCharging">Null when the device/protocol does not report charging state.</param>
public sealed record BatteryReading(
    string DeviceId,
    string Name,
    DeviceKind Kind,
    DeviceState State,
    int? Level,
    bool? IsCharging);
