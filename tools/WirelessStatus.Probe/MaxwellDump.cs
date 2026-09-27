using WirelessStatus.Core.Hid;

/// <summary>
/// Diagnostic: sends the Maxwell's known status *read* requests (the ones Audeze HQ itself sends, as documented by
/// HeadsetControl) and prints each reply, so captures taken in different headset states can be diffed.
/// </summary>
internal static class MaxwellDump
{
    private static readonly string[] Requests =
    [
        "0608 80055A04000109 20", "0608 80055A04000109 25", "0608 80055A04000109 28", "0608 80055A04000109 2D",
        "0608 80055A04000109 2C", "0608 80055A04000109 00", "0608 80055A04000109 24", "0608 80055A04000109 2F",
        "0608 80055A04000109 22",
        "0608 80055A0400832C 01", "0608 80055A0400832C 07", "0608 80055A0400832C 0B",
        "0607 80055A0300071C", "0607 80055A0300D60C",
    ];

    public static async Task RunAsync()
    {
        var info = HidDevice.Enumerate(0x3329).FirstOrDefault(d => d.UsagePage == 0xFF13)
            ?? throw new InvalidOperationException("No Maxwell dongle found.");
        using var device = HidDevice.TryOpen(info) ?? throw new InvalidOperationException("Could not open the Maxwell dongle.");

        var buffer = new byte[info.InputReportLength];
        foreach (var hex in Requests)
        {
            var request = Convert.FromHexString(hex.Replace(" ", ""));
            device.Write(request);
            await Task.Delay(60);
            Array.Clear(buffer);
            buffer[0] = 0x07;
            device.GetInputReport(buffer);
            Console.WriteLine($"{Convert.ToHexString(request[2..]),-22} -> {Convert.ToHexString(buffer)}");
        }
    }
}
