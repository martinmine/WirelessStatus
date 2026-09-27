using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WirelessStatus.App.Tray;

/// <summary>
/// Draws the tray icon: a Segoe Fluent glyph of a small device with wireless arcs, at the exact small-icon size for
/// the taskbar DPI and in the taskbar's theme colour, like the stock tray icons. Deliberately not a battery, which reads
/// as the system's own battery icon; levels are in the tooltip and popup.
/// </summary>
internal static unsafe class TrayIconRenderer
{
    private const uint White = 0xFFFFFFFF;
    private const uint Black = 0xFF1B1B1B;
    private const string GlyphFont = "Segoe Fluent Icons";
    private const string Glyph = "\uE957"; // a small device with wireless arcs above it

    public static HICON Render(uint dpi)
    {
        var size = PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXSMICON, dpi);
        return CreateIcon(DrawGlyph(size, IsTaskbarLight() ? Black : White), size);
    }

    /// <summary>
    /// Draws <see cref="Glyph"/> filling the icon. GDI renders it white on black with grayscale anti-aliasing (ClearType
    /// would add colour fringes), and that coverage becomes the alpha.
    /// </summary>
    private static uint[] DrawGlyph(int size, uint color)
    {
        var pixels = new uint[size * size];
        var dc = PInvoke.CreateCompatibleDC(HDC.Null);
        var info = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = size,
                biHeight = -size, // top-down, like the pixel array
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0, // BI_RGB
            },
        };
        void* bits;
        var bitmap = PInvoke.CreateDIBSection(dc, &info, DIB_USAGE.DIB_RGB_COLORS, &bits, HANDLE.Null, 0);
        // A negative height is the em size, which Segoe Fluent Icons glyphs are drawn to fill.
        using var font = PInvoke.CreateFont(-size, 0, 0, 0, 400, 0, 0, 0, FONT_CHARSET.DEFAULT_CHARSET,
            FONT_OUTPUT_PRECISION.OUT_TT_ONLY_PRECIS, FONT_CLIP_PRECISION.CLIP_DEFAULT_PRECIS,
            FONT_QUALITY.ANTIALIASED_QUALITY, 0, GlyphFont);

        try
        {
            var previousBitmap = PInvoke.SelectObject(dc, (HGDIOBJ)bitmap.Value);
            var previousFont = PInvoke.SelectObject(dc, (HGDIOBJ)font.DangerousGetHandle());
            PInvoke.SetTextColor(dc, new COLORREF(0x00FFFFFF));
            PInvoke.SetBkMode(dc, BACKGROUND_MODE.TRANSPARENT);
            var rect = new RECT(0, 0, size, size);
            fixed (char* text = Glyph)
            {
                PInvoke.DrawText(dc, text, Glyph.Length, &rect,
                    DRAW_TEXT_FORMAT.DT_CENTER | DRAW_TEXT_FORMAT.DT_VCENTER | DRAW_TEXT_FORMAT.DT_SINGLELINE | DRAW_TEXT_FORMAT.DT_NOPREFIX);
            }

            var coverage = new ReadOnlySpan<uint>(bits, size * size);
            for (var i = 0; i < coverage.Length; i++)
            {
                // Green channel as alpha (all three are equal in grayscale); icons take straight, not premultiplied, alpha.
                pixels[i] = (color & 0x00FFFFFF) | ((coverage[i] >> 8 & 0xFF) << 24);
            }

            PInvoke.SelectObject(dc, previousFont);
            PInvoke.SelectObject(dc, previousBitmap);
        }
        finally
        {
            PInvoke.DeleteObject((HGDIOBJ)bitmap.Value);
            PInvoke.DeleteDC(dc);
        }

        return pixels;
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
