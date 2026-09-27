namespace WirelessStatus.Core.Devices;

/// <summary>
/// Discovers one family of devices (e.g. Razer mice) and reads their battery levels.
/// Implementations must not throw for device-level failures; report them as <see cref="DeviceState.Unavailable"/>.
/// </summary>
public interface IBatteryProvider
{
    string Name { get; }

    Task<IReadOnlyList<BatteryReading>> ReadAsync(CancellationToken cancellationToken = default);
}
