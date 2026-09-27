using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WirelessStatus.App.Services;
using WirelessStatus.Core.Devices;
using WirelessStatus.Core.Settings;

namespace WirelessStatus.App.ViewModels;

/// <summary>Edits <see cref="AppSettings"/> in place; every change is applied and saved immediately via the callback.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public const int MinThreshold = 1;
    public const int MaxThreshold = 50;
    private static readonly int[] PollIntervals = [1, 2, 5, 10, 15, 30];

    private readonly AppSettings _settings;
    private readonly Action _changed;
    private readonly bool _initialized;

    /// <param name="knownDevices">Devices to list: everything currently seen plus anything with saved settings.</param>
    /// <param name="changed">Called after every change, to save and apply the settings.</param>
    internal SettingsViewModel(AppSettings settings, IEnumerable<(string Id, string Name, DeviceKind Kind)> knownDevices, Action changed)
    {
        _settings = settings;
        _changed = changed;

        Threshold = settings.LowBatteryThreshold;
        PollIntervalIndex = Array.IndexOf(PollIntervals, settings.PollIntervalMinutes);
        StartWithWindows = Autostart.IsEnabled;

        foreach (var (id, name, kind) in knownDevices)
            Devices.Add(new DeviceSettingsViewModel(id, name, DeviceGlyphs.For(kind), settings.GetDevice(id), OnDeviceChanged));
        HasNoDevices = Devices.Count == 0;

        _initialized = true;

        // A hand-edited file can hold a threshold or poll interval the UI can't represent; the controls would show
        // something other than what's in effect. Apply a valid value so what's shown is what's used.
        Threshold = Math.Clamp(settings.LowBatteryThreshold, MinThreshold, MaxThreshold);
        if (PollIntervalIndex < 0)
            PollIntervalIndex = Array.IndexOf(PollIntervals, AppSettings.DefaultPollIntervalMinutes);
    }

    public IReadOnlyList<string> PollIntervalOptions { get; } =
        PollIntervals.Select(m => m == 1 ? "1 minute" : $"{m} minutes").ToList();

    public ObservableCollection<DeviceSettingsViewModel> Devices { get; } = [];

    public bool HasNoDevices { get; }

    [ObservableProperty]
    public partial double Threshold { get; set; }

    [ObservableProperty]
    public partial int PollIntervalIndex { get; set; }

    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }

    partial void OnThresholdChanged(double value)
    {
        // NumberBox reports NaN while its text is empty or invalid.
        if (!_initialized || double.IsNaN(value))
            return;
        _settings.LowBatteryThreshold = (int)Math.Clamp(Math.Round(value), MinThreshold, MaxThreshold);
        _changed();
    }

    partial void OnPollIntervalIndexChanged(int value)
    {
        if (!_initialized || value < 0 || value >= PollIntervals.Length)
            return;
        _settings.PollIntervalMinutes = PollIntervals[value];
        _changed();
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_initialized)
            Autostart.SetEnabled(value);
    }

    private void OnDeviceChanged(string deviceId, DeviceSettings device)
    {
        // Keep the file tidy: only store devices that differ from the defaults.
        if (device == new DeviceSettings())
            _settings.Devices.Remove(deviceId);
        else
            _settings.Devices[deviceId] = device;
        _changed();
    }
}

public sealed partial class DeviceSettingsViewModel : ObservableObject
{
    private readonly Action<string, DeviceSettings> _changed;

    internal DeviceSettingsViewModel(string deviceId, string originalName, string glyph, DeviceSettings settings, Action<string, DeviceSettings> changed)
    {
        DeviceId = deviceId;
        OriginalName = originalName;
        Glyph = glyph;
        DisplayName = settings.DisplayName ?? "";
        ShowInList = !settings.Hidden;
        NotificationsEnabled = settings.NotificationsEnabled;
        _changed = changed;
    }

    public string DeviceId { get; }

    public string OriginalName { get; }

    public string Glyph { get; }

    [ObservableProperty]
    public partial string DisplayName { get; set; }

    [ObservableProperty]
    public partial bool ShowInList { get; set; }

    [ObservableProperty]
    public partial bool NotificationsEnabled { get; set; }

    // _changed is null while the constructor assigns the initial values.
    partial void OnDisplayNameChanged(string value) => Save();

    partial void OnShowInListChanged(bool value) => Save();

    partial void OnNotificationsEnabledChanged(bool value) => Save();

    private void Save() => _changed?.Invoke(DeviceId, new DeviceSettings
    {
        DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName.Trim(),
        Hidden = !ShowInList,
        NotificationsEnabled = NotificationsEnabled,
    });
}
