using WirelessStatus.Core.Devices;
using WirelessStatus.Core.Hid;

namespace WirelessStatus.Core.Providers.Audeze;

/// <summary>
/// Reads battery from the Audeze Maxwell dongle (protocol documented by the HeadsetControl project).
/// Requests go out as output report 0x06; responses are polled as input report 0x07. The battery level
/// follows the byte sequence D6 0C 00 00 in the response.
/// </summary>
public sealed class AudezeMaxwellProvider(Action<string>? log = null) : IBatteryProvider
{
    private const ushort VendorId = 0x3329;
    private static readonly ushort[] ProductIds = [0x4B18 /* Xbox dongle */, 0x4B19 /* PlayStation/PC dongle */];
    private const ushort VendorUsagePage = 0xFF13;

    private const byte InputReportId = 0x07;

    // Audeze HQ spaces packets ~50–60 ms apart; the dongle misbehaves if requests come faster.
    private static readonly TimeSpan PacketDelay = TimeSpan.FromMilliseconds(60);
    private const int MaxReads = 5;

    private static readonly byte[] BatteryRequest = [0x06, 0x07, 0x80, 0x05, 0x5A, 0x03, 0x00, 0xD6, 0x0C];
    private static readonly byte[] BatteryMarker = [0xD6, 0x0C, 0x00, 0x00];

    public string Name => "Audeze";

    public async Task<IReadOnlyList<BatteryReading>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var readings = new List<BatteryReading>();
        var collections = HidDevice.Enumerate(VendorId)
            .Where(d => ProductIds.Contains(d.ProductId) && d.UsagePage == VendorUsagePage);

        foreach (var info in collections)
        {
            log?.Invoke($"Audeze: trying {info}");
            var level = await TryReadAsync(info, cancellationToken);
            readings.Add(new BatteryReading(
                $"audeze:{VendorId:X4}:{info.ProductId:X4}",
                "Audeze Maxwell",
                DeviceKind.Headset,
                level is null ? DeviceState.Unavailable : DeviceState.Connected,
                level,
                null));
        }

        return readings;
    }

    private async Task<int?> TryReadAsync(HidDeviceInfo info, CancellationToken cancellationToken)
    {
        using var device = HidDevice.TryOpen(info);
        if (device is null)
        {
            log?.Invoke("Audeze:   could not open collection");
            return null;
        }

        try
        {
            device.Write(BatteryRequest);

            // The response may not be the first input report available, so poll a few times.
            var buffer = new byte[Math.Max(info.InputReportLength, 62)];
            for (var i = 0; i < MaxReads; i++)
            {
                await Task.Delay(PacketDelay, cancellationToken);
                Array.Clear(buffer);
                buffer[0] = InputReportId;
                device.GetInputReport(buffer.AsSpan(0, info.InputReportLength));
                log?.Invoke($"Audeze:   <- {Convert.ToHexString(buffer, 0, 24)}…");

                var index = buffer.AsSpan().IndexOf(BatteryMarker);
                if (index >= 0 && index + BatteryMarker.Length < buffer.Length)
                    return buffer[index + BatteryMarker.Length];
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            log?.Invoke($"Audeze:   {ex.Message}");
        }

        return null;
    }
}
