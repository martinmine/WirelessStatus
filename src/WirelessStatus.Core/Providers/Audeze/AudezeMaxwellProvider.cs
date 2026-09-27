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
    private const int MaxAttempts = 2;

    private static readonly byte[] BatteryRequest = [0x06, 0x07, 0x80, 0x05, 0x5A, 0x03, 0x00, 0xD6, 0x0C];
    private static readonly byte[] BatteryMarker = [0xD6, 0x0C, 0x00, 0x00];

    private const byte MessageStart = 0x05;
    private const byte ReplyType = 0x5D;

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
            var buffer = new byte[Math.Max(info.InputReportLength, 62)];

            // Now and then the dongle never answers a request (seen while Audeze HQ also talks to it), so resend once
            // before reporting the headset as unavailable.
            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                device.Write(BatteryRequest);

                // The response may not be the first input report available, so poll a few times.
                for (var i = 0; i < MaxReads; i++)
                {
                    await Task.Delay(PacketDelay, cancellationToken);
                    Array.Clear(buffer);
                    buffer[0] = InputReportId;
                    device.GetInputReport(buffer.AsSpan(0, info.InputReportLength));
                    log?.Invoke($"Audeze:   <- {Convert.ToHexString(buffer, 0, info.InputReportLength)}");

                    if (TryParseBattery(buffer) is { } level)
                        return level;
                }

                log?.Invoke("Audeze:   no battery reply");
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            log?.Invoke($"Audeze:   {ex.Message}");
        }

        return null;
    }

    /// <summary>Finds the battery reply (<c>5D</c> message with payload <c>D6 0C 00 00 &lt;level&gt;</c>) in an input report.</summary>
    internal static int? TryParseBattery(ReadOnlySpan<byte> report)
    {
        foreach (var (type, payload) in ParseMessages(report))
        {
            if (type == ReplyType && payload.AsSpan().StartsWith(BatteryMarker) && payload.Length > BatteryMarker.Length)
            {
                var level = payload[BatteryMarker.Length];
                return level <= 100 ? level : null;
            }
        }

        return null;
    }

    /// <summary>
    /// Splits input report 0x07 into its messages. Layout: <c>07 &lt;length&gt; 80</c> followed by <c>length</c> bytes of
    /// messages, each <c>05 &lt;type&gt; &lt;payload length, LE16&gt; &lt;payload&gt;</c> (5B = request echo, 5D = reply).
    /// Bytes past <c>length</c> are left over from earlier reports and must be ignored — they can contain stale replies.
    /// </summary>
    internal static List<(byte Type, byte[] Payload)> ParseMessages(ReadOnlySpan<byte> report)
    {
        var messages = new List<(byte, byte[])>();
        if (report.Length < 3 || report[0] != InputReportId)
            return messages;

        var remaining = report.Slice(3, Math.Min(report[1], report.Length - 3));
        while (remaining.Length >= 4 && remaining[0] == MessageStart)
        {
            var length = remaining[2] | (remaining[3] << 8);
            if (4 + length > remaining.Length)
                break;

            messages.Add((remaining[1], remaining.Slice(4, length).ToArray()));
            remaining = remaining[(4 + length)..];
        }

        return messages;
    }
}
