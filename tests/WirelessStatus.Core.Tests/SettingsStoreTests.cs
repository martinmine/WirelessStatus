using WirelessStatus.Core.Settings;

namespace WirelessStatus.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "WirelessStatusTests", Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, "nested", "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var settings = new SettingsStore(SettingsPath).Load();

        Assert.Equal(AppSettings.DefaultLowBatteryThreshold, settings.LowBatteryThreshold);
        Assert.Equal(TimeSpan.FromMinutes(AppSettings.DefaultPollIntervalMinutes), settings.PollInterval);
        Assert.Empty(settings.Devices);
    }

    [Fact]
    public void Round_trips_all_values()
    {
        var store = new SettingsStore(SettingsPath);
        store.Save(new AppSettings
        {
            LowBatteryThreshold = 15,
            PollIntervalMinutes = 2,
            Devices = { ["audeze:3329:4B18"] = new DeviceSettings { DisplayName = "Headset", Hidden = true, NotificationsEnabled = false } },
        });

        var loaded = store.Load();

        Assert.Equal(15, loaded.LowBatteryThreshold);
        Assert.Equal(2, loaded.PollIntervalMinutes);
        var device = loaded.GetDevice("audeze:3329:4B18");
        Assert.Equal("Headset", device.DisplayName);
        Assert.True(device.Hidden);
        Assert.False(device.NotificationsEnabled);
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public void Corrupt_file_gives_defaults_and_logs()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, "{ not json");
        var messages = new List<string>();

        var settings = new SettingsStore(SettingsPath, messages.Add).Load();

        Assert.Equal(AppSettings.DefaultLowBatteryThreshold, settings.LowBatteryThreshold);
        Assert.Single(messages);
    }

    [Fact]
    public void Missing_properties_keep_their_defaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """{ "pollIntervalMinutes": 1 }""");

        var settings = new SettingsStore(SettingsPath).Load();

        Assert.Equal(1, settings.PollIntervalMinutes);
        Assert.Equal(AppSettings.DefaultLowBatteryThreshold, settings.LowBatteryThreshold);
    }

    [Fact]
    public void Unknown_device_gets_default_device_settings()
    {
        var device = new AppSettings().GetDevice("bt:unknown");

        Assert.True(device.NotificationsEnabled);
        Assert.False(device.Hidden);
        Assert.Null(device.DisplayName);
    }

    [Fact]
    public void Poll_interval_is_at_least_one_minute()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), new AppSettings { PollIntervalMinutes = 0 }.PollInterval);
    }
}
