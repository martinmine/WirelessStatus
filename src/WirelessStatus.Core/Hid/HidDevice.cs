using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace WirelessStatus.Core.Hid;

/// <summary>Metadata for one HID top-level collection (one device interface path).</summary>
public sealed record HidDeviceInfo(
    string Path,
    ushort VendorId,
    ushort ProductId,
    int? InterfaceNumber,
    int? Collection,
    ushort UsagePage,
    ushort Usage,
    int InputReportLength,
    int OutputReportLength,
    int FeatureReportLength,
    string? ProductName)
{
    public override string ToString() =>
        $"{VendorId:X4}:{ProductId:X4} MI_{InterfaceNumber?.ToString("X2") ?? "--"} COL{Collection?.ToString("X2") ?? "--"} " +
        $"page=0x{UsagePage:X4} usage=0x{Usage:X2} in={InputReportLength} out={OutputReportLength} feat={FeatureReportLength} \"{ProductName}\"";
}

/// <summary>An opened HID collection. Keep instances short-lived so vendor software can also talk to the device.</summary>
public sealed unsafe partial class HidDevice : IDisposable
{
    private readonly SafeFileHandle _handle;

    public HidDeviceInfo Info { get; }

    private HidDevice(HidDeviceInfo info, SafeFileHandle handle)
    {
        Info = info;
        _handle = handle;
    }

    public static IReadOnlyList<HidDeviceInfo> Enumerate(ushort? vendorId = null, ushort? productId = null)
    {
        var result = new List<HidDeviceInfo>();
        foreach (var path in GetInterfacePaths())
        {
            // Access 0 lets us query attributes even for collections the OS holds exclusively (mice, keyboards).
            using var handle = OpenPath(path, 0);
            if (handle.IsInvalid)
                continue;

            var attributes = new HidNative.HiddAttributes { Size = (uint)sizeof(HidNative.HiddAttributes) };
            if (!HidNative.HidD_GetAttributes(handle, ref attributes))
                continue;
            if (vendorId is not null && attributes.VendorId != vendorId)
                continue;
            if (productId is not null && attributes.ProductId != productId)
                continue;

            HidNative.HidpCaps caps = default;
            if (HidNative.HidD_GetPreparsedData(handle, out var preparsed))
            {
                try
                {
                    if (HidNative.HidP_GetCaps(preparsed, out caps) != HidNative.HidpStatusSuccess)
                        caps = default;
                }
                finally
                {
                    HidNative.HidD_FreePreparsedData(preparsed);
                }
            }

            result.Add(new HidDeviceInfo(
                path,
                attributes.VendorId,
                attributes.ProductId,
                ParseHex(InterfaceRegex().Match(path)),
                ParseHex(CollectionRegex().Match(path)),
                caps.UsagePage,
                caps.Usage,
                caps.InputReportByteLength,
                caps.OutputReportByteLength,
                caps.FeatureReportByteLength,
                GetProductString(handle)));
        }

        return result;
    }

    /// <summary>
    /// Opens the collection for read/write. Mouse and keyboard collections are held exclusively by Windows, so those
    /// fall back to a zero-access handle, which still allows feature reports (HidD_Get/SetFeature) but not reads/writes.
    /// Returns null if the collection cannot be opened at all.
    /// </summary>
    public static HidDevice? TryOpen(HidDeviceInfo info)
    {
        var handle = OpenPath(info.Path, HidNative.GenericRead | HidNative.GenericWrite);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            handle = OpenPath(info.Path, 0);
        }

        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }

        return new HidDevice(info, handle);
    }

    /// <param name="report">Full report including the report id in byte 0; length must equal the feature report length.</param>
    public void SetFeature(ReadOnlySpan<byte> report)
    {
        fixed (byte* p = report)
        {
            if (!HidNative.HidD_SetFeature(_handle, p, (uint)report.Length))
                throw LastError(nameof(HidNative.HidD_SetFeature));
        }
    }

    /// <param name="buffer">Byte 0 must contain the report id to read.</param>
    public void GetFeature(Span<byte> buffer)
    {
        fixed (byte* p = buffer)
        {
            if (!HidNative.HidD_GetFeature(_handle, p, (uint)buffer.Length))
                throw LastError(nameof(HidNative.HidD_GetFeature));
        }
    }

    /// <param name="buffer">Byte 0 must contain the report id to read.</param>
    public void GetInputReport(Span<byte> buffer)
    {
        fixed (byte* p = buffer)
        {
            if (!HidNative.HidD_GetInputReport(_handle, p, (uint)buffer.Length))
                throw LastError(nameof(HidNative.HidD_GetInputReport));
        }
    }

    /// <summary>Writes an output report on the interrupt pipe. Shorter reports are zero-padded to the output report length.</summary>
    public void Write(ReadOnlySpan<byte> report)
    {
        Span<byte> buffer = stackalloc byte[Math.Max(Info.OutputReportLength, report.Length)];
        report.CopyTo(buffer);
        fixed (byte* p = buffer)
        {
            if (!HidNative.WriteFile(_handle, p, (uint)buffer.Length, out _, 0))
                throw LastError(nameof(HidNative.WriteFile));
        }
    }

    public void Dispose() => _handle.Dispose();

    private static SafeFileHandle OpenPath(string path, uint access) =>
        HidNative.CreateFile(path, access, HidNative.FileShareRead | HidNative.FileShareWrite, 0, HidNative.OpenExisting, 0, 0);

    private static List<string> GetInterfacePaths()
    {
        HidNative.HidD_GetHidGuid(out var hidGuid);
        while (true)
        {
            if (HidNative.CM_Get_Device_Interface_List_Size(out var length, ref hidGuid, 0, HidNative.CmGetDeviceInterfaceListPresent) != HidNative.CrSuccess)
                return [];

            var buffer = new char[length];
            fixed (char* p = buffer)
            {
                var status = HidNative.CM_Get_Device_Interface_List(ref hidGuid, 0, p, length, HidNative.CmGetDeviceInterfaceListPresent);
                if (status == 0x1A) // CR_BUFFER_SMALL: a device arrived between the two calls
                    continue;
                if (status != HidNative.CrSuccess)
                    return [];
            }

            return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();
        }
    }

    private static string? GetProductString(SafeFileHandle handle)
    {
        var buffer = stackalloc char[128];
        return HidNative.HidD_GetProductString(handle, buffer, 256)
            ? new string(buffer).TrimEnd('\0')
            : null;
    }

    private static int? ParseHex(Match match) =>
        match.Success ? Convert.ToInt32(match.Groups[1].Value, 16) : null;

    private static Win32Exception LastError(string function)
    {
        var error = Marshal.GetLastPInvokeError();
        return new Win32Exception(error, $"{function} failed: {new Win32Exception(error).Message} (0x{error:X})");
    }

    [GeneratedRegex(@"&mi_([0-9a-f]{2})", RegexOptions.IgnoreCase)]
    private static partial Regex InterfaceRegex();

    [GeneratedRegex(@"&col([0-9a-f]{2})", RegexOptions.IgnoreCase)]
    private static partial Regex CollectionRegex();
}
