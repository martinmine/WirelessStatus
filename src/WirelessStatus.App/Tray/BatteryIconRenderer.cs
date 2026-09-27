using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WirelessStatus.App.Tray;

/// <summary>
/// Draws the tray icon: a horizontal battery filled to the lowest device's level, pixel-snapped at the exact small-icon
/// size for the taskbar DPI. Colours follow the taskbar theme; red when low, green while charging.
/// </summary>
internal static unsafe class BatteryIconRenderer
{
    private const uint White = 0xFFFFFFFF;
    private const uint Black = 0xFF1B1B1B;

    public static HICON Render(int? level, bool isLow, bool isCharging, uint dpi)
    {
        var size = PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXSMICON, dpi);
        var lightTaskbar = IsTaskbarLight();
        var outline = lightTaskbar ? Black : White;
        var fill = isLow
            ? (lightTaskbar ? 0xFFC42B1Cu : 0xFFFF6B6Bu)
            : isCharging
                ? (lightTaskbar ? 0xFF0F7B0Fu : 0xFF6CCB5Fu)
                : outline;

        var pixels = new uint[size * size];
        void Rect(int x0, int y0, int x1, int y1, uint color) // inclusive bounds
        {
            for (var y = Math.Max(0, y0); y <= Math.Min(size - 1, y1); y++)
                for (var x = Math.Max(0, x0); x <= Math.Min(size - 1, x1); x++)
                    pixels[y * size + x] = color;
        }

        var stroke = Math.Max(1, size / 16);
        var nubWidth = Math.Max(1, size / 12);
        var bodyHeight = (int)Math.Round(size * 0.56);
        var top = (size - bodyHeight) / 2;
        var bottom = top + bodyHeight - 1;
        var right = size - 1 - nubWidth;

        // Outline, then the terminal nub on the right.
        Rect(0, top, right, top + stroke - 1, outline);
        Rect(0, bottom - stroke + 1, right, bottom, outline);
        Rect(0, top, stroke - 1, bottom, outline);
        Rect(right - stroke + 1, top, right, bottom, outline);
        var nubHeight = Math.Max(2, bodyHeight / 2);
        var nubTop = top + (bodyHeight - nubHeight) / 2;
        Rect(right + 1, nubTop, size - 1, nubTop + nubHeight - 1, outline);

        // Fill, inset by one stroke of padding inside the outline.
        if (level is > 0)
        {
            var inset = stroke * 2;
            var innerLeft = inset;
            var innerRight = right - inset;
            var width = Math.Max(1, (int)Math.Round((innerRight - innerLeft + 1) * Math.Min(level.Value, 100) / 100.0));
            Rect(innerLeft, top + inset, innerLeft + width - 1, bottom - inset, fill);
        }

        return CreateIcon(pixels, size);
    }

    private static HICON CreateIcon(uint[] pixels, int size)
    {
        // 32-bit colour bitmap with alpha; the monochrome mask is ignored when the colour bitmap has alpha but must exist.
        var mask = new byte[((size + 15) / 16 * 2) * size];
        fixed (uint* colorBits = pixels)
        fixed (byte* maskBits = mask)
        {
            var color = PInvoke.CreateBitmap(size, size, 1, 32, colorBits);
            var monochrome = PInvoke.CreateBitmap(size, size, 1, 1, maskBits);
            try
            {
                var info = new ICONINFO { fIcon = true, hbmColor = color, hbmMask = monochrome };
                return PInvoke.CreateIconIndirect(&info);
            }
            finally
            {
                PInvoke.DeleteObject(color);
                PInvoke.DeleteObject(monochrome);
            }
        }
    }

    /// <summary>The taskbar follows the "Windows mode" setting, separate from the app mode.</summary>
    private static bool IsTaskbarLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
    }
}
