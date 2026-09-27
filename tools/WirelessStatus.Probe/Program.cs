using WirelessStatus.Core.Devices;
using WirelessStatus.Core.Hid;
using WirelessStatus.Core.Monitoring;
using WirelessStatus.Core.Providers.Audeze;
using WirelessStatus.Core.Providers.Bluetooth;
using WirelessStatus.Core.Providers.Razer;
using WirelessStatus.Core.Settings;

// Usage:
//   Probe                              read battery from all providers once
//   Probe -v                           same, with protocol-level logging
//   Probe hid [vid]                    list HID collections (optionally filtered by hex vendor id)
//   Probe maxwell-dump                 diagnostic: print replies to all known Maxwell status requests
//   Probe maxwell-listen [SECONDS]     diagnostic: print reports the Maxwell sends unprompted
//   Probe watch [--threshold N] [--interval SECONDS] [-v]
//                                      run the real DeviceMonitor + AlertPolicy until Ctrl+C, using the app's
//                                      settings file; the options override its threshold / poll interval

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

if (args.FirstOrDefault() == "maxwell-listen")
{
    await MaxwellListen.RunAsync(TimeSpan.FromSeconds(args.Length > 1 ? int.Parse(args[1]) : 60));
    return;
}

if (args.FirstOrDefault() == "maxwell-dump")
{
    await MaxwellDump.RunAsync();
    return;
}

if (args.FirstOrDefault() == "watch")
{
    await WatchAsync();
    return;
}

foreach (var provider in providers)
{
    Console.WriteLine($"== {provider.Name}");
    try
    {
        var readings = await provider.ReadAsync();
        if (readings.Count == 0)
            Console.WriteLine("   (no devices)");
        foreach (var r in readings)
            Console.WriteLine($"   {Format(r)}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"   failed: {ex}");
    }
}

async Task WatchAsync()
{
    var settings = new SettingsStore(Console.WriteLine).Load();
    if (Option("--threshold") is { } threshold)
        settings.LowBatteryThreshold = int.Parse(threshold);
    var interval = Option("--interval") is { } seconds ? TimeSpan.FromSeconds(int.Parse(seconds)) : settings.PollInterval;

    Console.WriteLine($"Watching every {interval.TotalSeconds:0}s, low-battery threshold {settings.LowBatteryThreshold}%. Ctrl+C to stop.");

    var policy = new AlertPolicy();
    await using var monitor = new DeviceMonitor(providers, interval, log: Console.WriteLine);
    monitor.Updated += (_, snapshot) =>
    {
        Console.WriteLine($"-- {snapshot.UpdatedAt.ToLocalTime():HH:mm:ss}");
        foreach (var r in snapshot.Readings)
            Console.WriteLine($"   {Format(r)}");
        foreach (var alert in policy.Evaluate(snapshot.Readings, settings))
            Console.WriteLine($"   !! LOW BATTERY: {alert.Reading.Name} at {alert.Reading.Level}% (threshold {alert.Threshold}%)");
    };

    var stop = new TaskCompletionSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        stop.TrySetResult();
    };

    monitor.Start();
    await stop.Task;
}

string? Option(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static string Format(BatteryReading r)
{
    var level = r.Level is { } l ? $"{l,3}%" : "  –";
    var charging = r.IsCharging == true ? " charging" : "";
    return $"{r.Name,-30} {level}  {r.State}{charging}   [{r.DeviceId}]";
}
