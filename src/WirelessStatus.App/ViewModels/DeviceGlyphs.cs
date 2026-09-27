using WirelessStatus.Core.Devices;

namespace WirelessStatus.App.ViewModels;

/// <summary>Segoe Fluent Icons glyph per device type.</summary>
internal static class DeviceGlyphs
{
    public static string For(DeviceKind kind) => kind switch
    {
        DeviceKind.Headset => "\uE7F6",    // Headphone
        DeviceKind.Mouse => "\uE962",      // Mouse
        DeviceKind.Keyboard => "\uE765",   // KeyboardClassic
        DeviceKind.Controller => "\uE7FC", // Game
        DeviceKind.Bluetooth => "\uE702",  // Bluetooth
        _ => "\uE772",                     // Devices
    };
}
