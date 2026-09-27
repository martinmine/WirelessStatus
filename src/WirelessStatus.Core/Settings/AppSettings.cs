using System.Text.Json.Serialization;

namespace WirelessStatus.Core.Settings;

// Properties use `set`, not `init`: the System.Text.Json source generator assigns every init-only property in one
// object initializer, so a property missing from the file would get default(T) instead of its initializer value.
public sealed record AppSettings
{
    public const int DefaultLowBatteryThreshold = 10;
    public const int DefaultPollIntervalMinutes = 5;

    /// <summary>Notify when a device's battery is at or below this percentage.</summary>
    public int LowBatteryThreshold { get; set; } = DefaultLowBatteryThreshold;

    public int PollIntervalMinutes { get; set; } = DefaultPollIntervalMinutes;

    /// <summary>Per-device overrides keyed by <see cref="Devices.BatteryReading.DeviceId"/>.</summary>
    public Dictionary<string, DeviceSettings> Devices { get; set; } = [];

    [JsonIgnore]
    public TimeSpan PollInterval => TimeSpan.FromMinutes(Math.Max(1, PollIntervalMinutes));

    public DeviceSettings GetDevice(string deviceId) =>
        Devices.GetValueOrDefault(deviceId) ?? new DeviceSettings();
}

public sealed record DeviceSettings
{
    /// <summary>User-chosen name shown instead of the device's own name.</summary>
    public string? DisplayName { get; set; }

    public bool Hidden { get; set; }

    public bool NotificationsEnabled { get; set; } = true;
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
