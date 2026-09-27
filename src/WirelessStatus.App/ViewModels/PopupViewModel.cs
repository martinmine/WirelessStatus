using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WirelessStatus.Core.Monitoring;
using WirelessStatus.Core.Settings;

namespace WirelessStatus.App.ViewModels;

public sealed partial class PopupViewModel : ObservableObject
{
    public ObservableCollection<DeviceViewModel> Devices { get; } = [];

    [ObservableProperty]
    public partial bool HasNoDevices { get; set; }

    /// <summary>Applies a snapshot, updating existing rows in place so the list doesn't flicker.</summary>
    public void Apply(MonitorSnapshot snapshot, AppSettings settings)
    {
        var visible = snapshot.Readings.Where(r => !settings.GetDevice(r.DeviceId).Hidden).ToList();

        for (var i = Devices.Count - 1; i >= 0; i--)
        {
            if (!visible.Any(r => r.DeviceId == Devices[i].DeviceId))
                Devices.RemoveAt(i);
        }

        for (var i = 0; i < visible.Count; i++)
        {
            var reading = visible[i];
            var existing = Devices.FirstOrDefault(d => d.DeviceId == reading.DeviceId);
            if (existing is null)
            {
                existing = new DeviceViewModel(reading.DeviceId);
                Devices.Insert(i, existing);
            }
            else if (Devices.IndexOf(existing) != i)
            {
                Devices.Move(Devices.IndexOf(existing), i);
            }

            existing.Update(reading, settings);
        }

        HasNoDevices = Devices.Count == 0;
    }

    /// <summary>The device the tray icon should represent: the available one with the lowest level, if any.</summary>
    public DeviceViewModel? LowestDevice => Devices.Where(d => d.IsAvailable).MinBy(d => d.Level);

    /// <summary>Text for the tray icon tooltip: one line per device.</summary>
    public string BuildTooltip()
    {
        if (Devices.Count == 0)
            return "WirelessStatus — no devices";
        return string.Join("\n", Devices.Select(d => $"{d.Name}: {d.StatusText}{(d.IsCharging ? " (charging)" : "")}"));
    }
}
