using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using WirelessStatus.Core.Hid;

/// <summary>
/// Diagnostic: prints every input report the Maxwell dongle sends on its own (interrupt pipe), with timestamps,
/// e.g. to see whether plugging/unplugging the charging cable produces an event.
/// </summary>
internal static partial class MaxwellListen
{
    public static async Task RunAsync(TimeSpan duration)
    {
        var info = HidDevice.Enumerate(0x3329).FirstOrDefault(d => d.UsagePage == 0xFF13)
            ?? throw new InvalidOperationException("No Maxwell dongle found.");

        const uint genericRead = 0x80000000, shareReadWrite = 0x3, openExisting = 3, overlapped = 0x40000000;
        using var handle = CreateFile(info.Path, genericRead, shareReadWrite, 0, openExisting, overlapped, 0);
        if (handle.IsInvalid)
            throw new InvalidOperationException($"Could not open dongle: {Marshal.GetLastPInvokeError()}");
        await using var stream = new FileStream(handle, FileAccess.Read, 0, isAsync: true);

        Console.WriteLine($"Listening for {duration.TotalSeconds:0}s…");
        using var cts = new CancellationTokenSource(duration);
        var buffer = new byte[info.InputReportLength];
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cts.Token);
                Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {Convert.ToHexString(buffer, 0, read)}");
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string fileName, uint access, uint share, nint security, uint disposition, uint flags, nint template);
}
