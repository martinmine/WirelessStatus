using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WirelessStatus.App.Tray;

public sealed record TrayMenuItem(int Id, string Text, bool IsChecked = false, bool IsSeparator = false)
{
    public static TrayMenuItem Separator { get; } = new(0, "", IsSeparator: true);
}

/// <summary>
/// Notification-area icon. Owns a hidden top-level window that receives the icon's callback messages, the
/// "TaskbarCreated" broadcast (Explorer restarted → re-add the icon), WM_DEVICECHANGE and WM_SETTINGCHANGE broadcasts,
/// and <see cref="ShowRequestMessage"/> from a second instance of the app.
/// Must be created on the UI thread; events are raised on that thread.
/// </summary>
internal sealed unsafe class TrayIcon : IDisposable
{
    private const uint CallbackMessage = PInvoke.WM_APP + 1;
    private const uint ShowRequestMessage = PInvoke.WM_APP + 2;
    private const uint IconId = 1;
    private const uint NinKeySelect = PInvoke.NIN_SELECT | 0x1; // NIN_SELECT | NINF_KEY (not in the Win32 metadata)
    private const string WindowClassName = "WirelessStatus.TrayWindow";

    // The window procedure is a static function pointer (AOT-safe); it routes to the single live instance.
    private static TrayIcon? s_instance;

    private readonly HWND _hwnd;
    private readonly uint _taskbarCreatedMessage;
    private HICON _icon;
    private string _tooltip;

    public TrayIcon(string iconPath, string tooltip)
    {
        if (s_instance is not null)
            throw new InvalidOperationException("Only one tray icon is supported.");
        s_instance = this;
        _tooltip = tooltip;

        var instance = PInvoke.GetModuleHandle((char*)null);
        fixed (char* className = WindowClassName)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WindowProc,
                hInstance = instance,
                lpszClassName = className,
            };
            PInvoke.RegisterClassEx(windowClass);

            // A hidden top-level window rather than a message-only one: message-only windows don't get broadcasts.
            _hwnd = PInvoke.CreateWindowEx(0, className, className, 0, 0, 0, 0, 0, HWND.Null, HMENU.Null, instance, null);
        }

        if (_hwnd.IsNull)
            throw new InvalidOperationException($"Could not create tray window: {Marshal.GetLastPInvokeError()}");

        fixed (char* name = "TaskbarCreated")
            _taskbarCreatedMessage = PInvoke.RegisterWindowMessage(name);

        _icon = LoadIcon(iconPath);
        Add();
    }

    /// <summary>Left click or keyboard activation. Argument: anchor point in physical screen pixels.</summary>
    public event Action<PointInt32>? Selected;

    /// <summary>Right click / context-menu key. Argument: anchor point in physical screen pixels.</summary>
    public event Action<PointInt32>? ContextMenuRequested;

    /// <summary>A device was added or removed somewhere in the system.</summary>
    public event Action? DevicesChanged;

    /// <summary>The light/dark theme (and with it the taskbar colour) changed.</summary>
    public event Action? ThemeChanged;

    /// <summary>Another instance of the app was started and asked this one to show its popup.</summary>
    public event Action? ShowRequested;

    /// <summary>DPI of the taskbar's monitor, for sizing a rendered icon.</summary>
    public uint Dpi => PInvoke.GetDpiForWindow(_hwnd);

    /// <summary>Asks an already running instance to show its popup. Returns false if none was found.</summary>
    public static bool SignalExistingInstance()
    {
        fixed (char* className = WindowClassName)
        {
            var hwnd = PInvoke.FindWindow(className, null);
            return !hwnd.IsNull && PInvoke.PostMessage(hwnd, ShowRequestMessage, 0, 0);
        }
    }

    /// <summary>Replaces the icon. The tray icon takes ownership of <paramref name="icon"/> and destroys it later.</summary>
    public void SetIcon(HICON icon)
    {
        var previous = _icon;
        _icon = icon;
        var data = CreateData(NOTIFY_ICON_DATA_FLAGS.NIF_ICON);
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, &data);
        if (!previous.IsNull)
            PInvoke.DestroyIcon(previous);
    }

    public string Tooltip
    {
        get => _tooltip;
        set
        {
            _tooltip = value;
            var data = CreateData(NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP);
            PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, &data);
        }
    }

    /// <summary>The icon's bounds in physical screen pixels, or null if it can't be determined (e.g. in the overflow area).</summary>
    public RectInt32? GetBounds()
    {
        var identifier = new NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)sizeof(NOTIFYICONIDENTIFIER),
            hWnd = _hwnd,
            uID = IconId,
        };
        if (PInvoke.Shell_NotifyIconGetRect(identifier, out var rect).Failed)
            return null;
        return new RectInt32(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
    }

    /// <summary>Shows a native popup menu at <paramref name="point"/> and returns the chosen item id, or 0 if dismissed.</summary>
    public int ShowMenu(IEnumerable<TrayMenuItem> items, PointInt32 point)
    {
        var menu = PInvoke.CreatePopupMenu();
        try
        {
            foreach (var item in items)
            {
                if (item.IsSeparator)
                {
                    PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, (char*)null);
                    continue;
                }

                var flags = MENU_ITEM_FLAGS.MF_STRING | (item.IsChecked ? MENU_ITEM_FLAGS.MF_CHECKED : 0);
                fixed (char* text = item.Text)
                    PInvoke.AppendMenu(menu, flags, (nuint)item.Id, text);
            }

            // Required so the menu closes when the user clicks elsewhere (documented TrackPopupMenu quirk).
            PInvoke.SetForegroundWindow(_hwnd);
            var chosen = PInvoke.TrackPopupMenuEx(menu,
                (uint)(TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_NONOTIFY | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON),
                point.X, point.Y, _hwnd, null);
            PInvoke.PostMessage(_hwnd, PInvoke.WM_NULL, 0, 0);
            return chosen;
        }
        finally
        {
            PInvoke.DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (s_instance != this)
            return;

        var data = CreateData(0);
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, &data);
        PInvoke.DestroyWindow(_hwnd);
        if (!_icon.IsNull)
            PInvoke.DestroyIcon(_icon);
        _icon = HICON.Null;
        s_instance = null;
    }

    private void Add()
    {
        var data = CreateData(NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON |
                              NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP);
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, &data);

        // Version 4: click events arrive as NIN_SELECT/WM_CONTEXTMENU with the anchor point in wParam.
        data.Anonymous.uVersion = PInvoke.NOTIFYICON_VERSION_4;
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_SETVERSION, &data);
    }

    private NOTIFYICONDATAW CreateData(NOTIFY_ICON_DATA_FLAGS flags)
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = _hwnd,
            uID = IconId,
            uFlags = flags,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
        };

        // szTip holds at most 127 characters plus the terminator.
        var tip = _tooltip.Length > 127 ? _tooltip[..126] + "…" : _tooltip;
        tip.AsSpan().CopyTo(data.szTip.AsSpan());
        return data;
    }

    private static HICON LoadIcon(string path)
    {
        fixed (char* p = path)
        {
            var handle = PInvoke.LoadImage(HINSTANCE.Null, p, GDI_IMAGE_TYPE.IMAGE_ICON, 0, 0,
                IMAGE_FLAGS.LR_LOADFROMFILE | IMAGE_FLAGS.LR_DEFAULTSIZE);
            return (HICON)handle.Value;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static LRESULT WindowProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        var self = s_instance;
        if (self is not null && hwnd == self._hwnd)
        {
            try
            {
                if (self.HandleMessage(message, wParam, lParam))
                    return (LRESULT)0;
            }
            catch (Exception ex)
            {
                // Never let an exception unwind into native code.
                System.Diagnostics.Debug.WriteLine($"Tray: {ex}");
            }
        }

        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private bool HandleMessage(uint message, WPARAM wParam, LPARAM lParam)
    {
        if (message == CallbackMessage)
        {
            // Version 4: LOWORD(lParam) = event, wParam = anchor x/y (signed 16-bit each).
            var anchor = new PointInt32((short)(wParam.Value & 0xFFFF), (short)((wParam.Value >> 16) & 0xFFFF));
            switch ((uint)(lParam.Value & 0xFFFF))
            {
                case PInvoke.NIN_SELECT:
                case NinKeySelect:
                    Selected?.Invoke(anchor);
                    return true;
                case PInvoke.WM_CONTEXTMENU:
                    ContextMenuRequested?.Invoke(anchor);
                    return true;
            }

            return false;
        }

        if (message == _taskbarCreatedMessage)
        {
            Add();
            return true;
        }

        if (message == PInvoke.WM_DEVICECHANGE && wParam.Value == PInvoke.DBT_DEVNODES_CHANGED)
        {
            DevicesChanged?.Invoke();
            return true;
        }

        if (message == PInvoke.WM_SETTINGCHANGE && lParam.Value != 0 &&
            new string((char*)lParam.Value) == "ImmersiveColorSet")
        {
            ThemeChanged?.Invoke();
            return false;
        }

        if (message == ShowRequestMessage)
        {
            ShowRequested?.Invoke();
            return true;
        }

        return false;
    }
}
