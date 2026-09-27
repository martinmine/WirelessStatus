namespace WirelessStatus.Core.Providers.Razer;

/// <summary>
/// Razer's 90-byte control report, sent as HID feature report 0 (91 bytes on Windows including the report id).
/// Layout: status, transaction id, remaining packets (BE16), protocol type, data size, command class, command id,
/// 80 argument bytes, CRC (XOR of bytes 2..87), reserved.
/// </summary>
internal static class RazerReport
{
    public const int Length = 90;
    public const int FeatureReportLength = Length + 1;

    public const byte StatusBusy = 0x01;
    public const byte StatusSuccess = 0x02;
    public const byte StatusFailure = 0x03;
    public const byte StatusTimeout = 0x04;
    public const byte StatusNotSupported = 0x05;

    // Offsets within the 90-byte report (add 1 for the Windows feature buffer).
    private const int Status = 0;
    private const int TransactionId = 1;
    private const int DataSize = 5;
    private const int CommandClass = 6;
    private const int CommandId = 7;
    public const int Arguments = 8;
    private const int Crc = 88;

    /// <summary>Builds a feature report buffer (report id 0 + 90 bytes).</summary>
    public static byte[] Create(byte transactionId, byte commandClass, byte commandId, byte dataSize)
    {
        var buffer = new byte[FeatureReportLength];
        var report = buffer.AsSpan(1);
        report[TransactionId] = transactionId;
        report[DataSize] = dataSize;
        report[CommandClass] = commandClass;
        report[CommandId] = commandId;
        report[Crc] = CalculateCrc(report);
        return buffer;
    }

    public static byte CalculateCrc(ReadOnlySpan<byte> report)
    {
        byte crc = 0;
        for (var i = 2; i < Crc; i++)
            crc ^= report[i];
        return crc;
    }

    public static byte GetStatus(ReadOnlySpan<byte> featureBuffer) => featureBuffer[1 + Status];

    public static bool Matches(ReadOnlySpan<byte> request, ReadOnlySpan<byte> response) =>
        request[1 + CommandClass] == response[1 + CommandClass] &&
        request[1 + CommandId] == response[1 + CommandId];

    public static byte GetArgument(ReadOnlySpan<byte> featureBuffer, int index) => featureBuffer[1 + Arguments + index];
}
