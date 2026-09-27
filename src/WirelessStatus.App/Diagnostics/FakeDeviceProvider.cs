#if DEBUG
using WirelessStatus.Core.Devices;

namespace WirelessStatus.App.Diagnostics;

/// <summary>
/// Debug builds only: adds N made-up devices when the environment variable <see cref="VariableName"/> is set to N,
/// for testing the UI with more devices than are at hand (e.g. the popup's scrolling).
/// </summary>
internal sealed class FakeDeviceProvider(int count) : IBatteryProvider
{
    public const string VariableName = "WIRELESSSTATUS_FAKE_DEVICES";

    private static readonly DeviceKind[] Kinds = [DeviceKind.Mouse, DeviceKind.Headset, DeviceKind.Keyboard, DeviceKind.Controller, DeviceKind.Bluetooth];

    public string Name => "Fake";

    public static FakeDeviceProvider? FromEnvironment() =>
        int.TryParse(Environment.GetEnvironmentVariable(VariableName), out var count) && count > 0
            ? new FakeDeviceProvider(count)
            : null;

    public Task<IReadOnlyList<BatteryReading>> ReadAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<BatteryReading> readings = Enumerable.Range(1, count)
            .Select(i => new BatteryReading(
                $"fake:{i}",
                $"Fake device {i}",
                Kinds[(i - 1) % Kinds.Length],
                i % 4 == 0 ? DeviceState.Unavailable : DeviceState.Connected,
                i % 4 == 0 ? null : 100 - i * 9 % 100,
                i % 3 == 0))
            .ToList();
        return Task.FromResult(readings);
    }
}
#endif
