using CommunityToolkit.Mvvm.ComponentModel;
using WirelessStatus.Core.Devices;
using WirelessStatus.Core.Settings;

namespace WirelessStatus.App.ViewModels;

public sealed partial class DeviceViewModel(string deviceId) : ObservableObject
{
    public string DeviceId { get; } = deviceId;

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    /// <summary>Segoe Fluent Icons glyph for the device type.</summary>
    [ObservableProperty]
    public partial string Glyph { get; set; } = "";

    [ObservableProperty]
    public partial double Level { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsAvailable { get; set; }

    [ObservableProperty]
    public partial bool IsCharging { get; set; }

    /// <summary>At or below the low-battery threshold and not charging.</summary>
    [ObservableProperty]
    public partial bool IsLow { get; set; }

    [ObservableProperty]
    public partial double ContentOpacity { get; set; } = 1;

    public string AutomationName => IsAvailable
        ? $"{Name}, {StatusText}{(IsCharging ? ", charging" : "")}"
        : $"{Name}, {StatusText}";

    public void Update(BatteryReading reading, AppSettings settings)
    {
        Name = settings.GetDevice(reading.DeviceId).DisplayName ?? reading.Name;
        Glyph = DeviceGlyphs.For(reading.Kind);
        IsAvailable = reading.State == DeviceState.Connected && reading.Level is not null;
        IsCharging = IsAvailable && reading.IsCharging == true;
        Level = reading.Level ?? 0;
        IsLow = IsAvailable && !IsCharging && reading.Level <= settings.LowBatteryThreshold;
        StatusText = IsAvailable ? $"{reading.Level}%" : "Unavailable";
        ContentOpacity = IsAvailable ? 1 : 0.5;
        OnPropertyChanged(nameof(AutomationName));
    }
}
