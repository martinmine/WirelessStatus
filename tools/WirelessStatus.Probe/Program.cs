using WirelessStatus.Core.Devices;
using WirelessStatus.Core.Hid;
using WirelessStatus.Core.Providers.Audeze;
using WirelessStatus.Core.Providers.Bluetooth;
using WirelessStatus.Core.Providers.Razer;

// Usage:
//   Probe                 read battery from all providers
//   Probe -v              same, with protocol-level logging
//   Probe hid [vid]       list HID collections (optionally filtered by hex vendor id)

var verbose = args.Contains("-v");
Action<string>? log = verbose ? Console.WriteLine : null;

if (args.FirstOrDefault() == "hid")
{
    ushort? vid = args.Length > 1 ? Convert.ToUInt16(args[1], 16) : null;
    foreach (var info in HidDevice.Enumerate(vid).OrderBy(d => d.VendorId).ThenBy(d => d.ProductId).ThenBy(d => d.Path))
        Console.WriteLine(info);
    return;
}

IBatteryProvider[] providers =
[
    new RazerProvider(log),
    new AudezeMaxwellProvider(log),
    new BluetoothBatteryProvider(log),
];

foreach (var provider in providers)
{
    Console.WriteLine($"== {provider.Name}");
    try
    {
        var readings = await provider.ReadAsync();
        if (readings.Count == 0)
            Console.WriteLine("   (no devices)");
        foreach (var r in readings)
        {
            var level = r.Level is { } l ? $"{l,3}%" : "  –";
            var charging = r.IsCharging switch { true => " charging", false => "", null => "" };
            Console.WriteLine($"   {r.Name,-30} {level}  {r.State}{charging}   [{r.DeviceId}]");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"   failed: {ex}");
    }
}
