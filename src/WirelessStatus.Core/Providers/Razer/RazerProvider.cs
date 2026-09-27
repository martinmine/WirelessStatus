using WirelessStatus.Core.Devices;
using WirelessStatus.Core.Hid;

namespace WirelessStatus.Core.Providers.Razer;

/// <summary>
/// Reads battery from Razer wireless mice via the Razer control report (protocol documented by the OpenRazer project).
/// </summary>
public sealed class RazerProvider(Action<string>? log = null) : IBatteryProvider
{
    private const ushort VendorId = 0x1532;

    // Wait between sending a request and reading the response; newer receivers need ~31 ms.
    private static readonly TimeSpan ResponseDelay = TimeSpan.FromMilliseconds(35);
    private const int MaxAttempts = 3;

    private sealed record Model(string Name, DeviceKind Kind, byte TransactionId);

    // Add new models here. Transaction ids per model come from OpenRazer's razermouse_driver.c.
    private static readonly Dictionary<ushort, Model> Models = new()
    {
        [0x00BE] = new("Razer DeathAdder V4 Pro", DeviceKind.Mouse, 0x1F), // wired
        [0x00BF] = new("Razer DeathAdder V4 Pro", DeviceKind.Mouse, 0x1F), // HyperSpeed receiver
    };

    public string Name => "Razer";

    public async Task<IReadOnlyList<BatteryReading>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var readings = new List<BatteryReading>();
        var collectionsByPid = HidDevice.Enumerate(VendorId)
            .Where(d => Models.ContainsKey(d.ProductId) && d.FeatureReportLength == RazerReport.FeatureReportLength)
            .GroupBy(d => d.ProductId);

        foreach (var group in collectionsByPid)
        {
            var model = Models[group.Key];
            var reading = new BatteryReading($"razer:{VendorId:X4}:{group.Key:X4}", model.Name, model.Kind, DeviceState.Unavailable, null, null);

            // Several collections may declare the 91-byte feature report; use the first one that answers.
            foreach (var info in group)
            {
                log?.Invoke($"Razer: trying {info}");
                var result = await TryReadAsync(info, model, cancellationToken);
                if (result is not null)
                {
                    reading = reading with { State = DeviceState.Connected, Level = result.Value.Level, IsCharging = result.Value.IsCharging };
                    break;
                }
            }

            readings.Add(reading);
        }

        return readings;
    }

    private async Task<(int Level, bool? IsCharging)?> TryReadAsync(HidDeviceInfo info, Model model, CancellationToken cancellationToken)
    {
        using var device = HidDevice.TryOpen(info);
        if (device is null)
        {
            log?.Invoke("Razer:   could not open collection");
            return null;
        }

        var battery = await SendAsync(device, RazerReport.Create(model.TransactionId, 0x07, 0x80, 0x02), cancellationToken);
        if (battery is null)
            return null;

        var charging = await SendAsync(device, RazerReport.Create(model.TransactionId, 0x07, 0x84, 0x02), cancellationToken);

        // Level is reported as 0–255.
        var level = (int)Math.Round(RazerReport.GetArgument(battery, 1) * 100 / 255.0);
        return (level, charging is null ? null : RazerReport.GetArgument(charging, 1) != 0);
    }

    private async Task<byte[]?> SendAsync(HidDevice device, byte[] request, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                device.SetFeature(request);
                await Task.Delay(ResponseDelay, cancellationToken);

                var response = new byte[RazerReport.FeatureReportLength];
                device.GetFeature(response);
                log?.Invoke($"Razer:   <- {Convert.ToHexString(response, 0, 16)}…");

                var status = RazerReport.GetStatus(response);
                if (RazerReport.Matches(request, response) && status == RazerReport.StatusSuccess)
                    return response;

                // Timeout/not supported from the receiver means the mouse is off or asleep; no point retrying.
                if (status is RazerReport.StatusTimeout or RazerReport.StatusNotSupported or RazerReport.StatusFailure)
                    return null;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                log?.Invoke($"Razer:   {ex.Message}");
                return null;
            }
        }

        return null;
    }
}
