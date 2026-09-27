using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WirelessStatus.App.Services;
using WirelessStatus.App.Tray;
using WirelessStatus.App.ViewModels;
using WirelessStatus.App.Views;
using WirelessStatus.Core.Devices;
using WirelessStatus.Core.Diagnostics;
using WirelessStatus.Core.Monitoring;
using WirelessStatus.Core.Providers.Audeze;
using WirelessStatus.Core.Providers.Bluetooth;
using WirelessStatus.Core.Providers.Razer;
using WirelessStatus.Core.Settings;

namespace WirelessStatus.App;

/// <summary>
/// Tray-only application: no main window. The tray icon opens <see cref="PopupWindow"/>; <see cref="DeviceMonitor"/>
/// polls devices in the background and its snapshots are marshalled to the UI thread, where they update the popup,
/// the tray icon and tooltip, and feed <see cref="AlertPolicy"/> for low-battery toasts.
/// </summary>
public partial class App : Application
{
    private const int MenuSettings = 1;
    private const int MenuAutostart = 2;
    private const int MenuRefresh = 3;
    private const int MenuExit = 4;

    private const string SingleInstanceMutexName = @"Local\WirelessStatus.SingleInstance";

    // A click on the tray icon while the popup is open first deactivates (hides) the popup; don't reopen it right away.
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(300);

    private static readonly string IconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");

    // Toasts need a PNG; with an .ico the shell shows a generic icon.
    private static readonly string ToastIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.png");

    private readonly SettingsStore _settingsStore = new(Log);
    private readonly PopupViewModel _viewModel = new();
    private readonly AlertPolicy _alertPolicy = new();
    private AppSettings _settings = new();
    private Mutex? _singleInstance;
    private DispatcherQueue _dispatcher = null!;
    private DeviceMonitor _monitor = null!;
    private TrayIcon _tray = null!;
    // Created on first use: until then no XAML window exists, which saves ~30 MB. It is then kept (hidden), because
    // WinUI doesn't give that memory back when a window is closed (measured), so recreating it would only add latency.
    private PopupWindow? _popup;
    private DateTimeOffset _popupHiddenAt;
    private NotificationService _notifications = null!;
    private SettingsWindow? _settingsWindow;

    public App()
    {
        InitializeComponent();
        // The app lives in the tray; hiding or closing windows must not end it.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            // Already running (e.g. launched again from the Start menu): show that instance's popup instead.
            TrayIcon.SignalExistingInstance();
            _singleInstance.Dispose();
            Exit();
            return;
        }

        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _settings = _settingsStore.Load();
        Autostart.UpdatePathIfEnabled();

        _monitor = new DeviceMonitor(
            [new RazerProvider(), new AudezeMaxwellProvider(), new BluetoothBatteryProvider()],
            _settings.PollInterval,
            log: Log);
        _monitor.Updated += (_, snapshot) => _dispatcher.TryEnqueue(() => OnSnapshot(snapshot));

        _tray = new TrayIcon(IconPath, "WirelessStatus — checking devices…");
        _tray.Selected += OnTraySelected;
        _tray.ContextMenuRequested += OnTrayContextMenu;
        _tray.DevicesChanged += _monitor.RequestRefresh;
        _tray.ThemeChanged += UpdateTrayIcon;
        _tray.ShowRequested += ShowPopup;

        _notifications = new NotificationService(ToastIconPath, Log);
        _notifications.Invoked += () => _dispatcher.TryEnqueue(ShowPopup);

        Log($"Started {Environment.ProcessPath}");
        _monitor.Start();
    }

    private void OnSnapshot(MonitorSnapshot snapshot)
    {
        ApplyToUi(snapshot);

        foreach (var alert in _alertPolicy.Evaluate(snapshot.Readings, _settings))
        {
            Log($"Low battery: {alert.Reading.DeviceId} at {alert.Reading.Level}%");
            _notifications.ShowLowBattery(alert, _settings.GetDevice(alert.Reading.DeviceId).DisplayName ?? alert.Reading.Name);
        }
    }

    private void ApplyToUi(MonitorSnapshot snapshot)
    {
        _viewModel.Apply(snapshot, _settings);
        _tray.Tooltip = _viewModel.BuildTooltip();
        UpdateTrayIcon();
    }

    private void UpdateTrayIcon()
    {
        var lowest = _viewModel.LowestDevice;
        _tray.SetIcon(BatteryIconRenderer.Render(
            lowest is null ? null : (int)lowest.Level,
            lowest?.IsLow == true,
            lowest?.IsCharging == true,
            _tray.Dpi));
    }

    private void OnTraySelected(PointInt32 point)
    {
        if (_popup?.IsOpen == true)
        {
            _popup.Hide();
            return;
        }

        if (DateTimeOffset.UtcNow - _popupHiddenAt < ReopenGuard)
            return;

        ShowPopup(new RectInt32(point.X, point.Y, 1, 1));
    }

    private void ShowPopup() => ShowPopup(null);

    private void ShowPopup(RectInt32? fallbackAnchor)
    {
        // Without a click position (toast, second instance), anchor to the icon or the bottom-right of the screen.
        var anchor = _tray.GetBounds() ?? fallbackAnchor ?? BottomRightOfPrimaryDisplay();
        GetOrCreatePopup().ShowAt(anchor);
        RefreshNow();
    }

    private PopupWindow GetOrCreatePopup()
    {
        if (_popup is null)
        {
            _popup = new PopupWindow(_viewModel);
            _popup.RefreshRequested += (_, _) => RefreshNow();
            _popup.SettingsRequested += (_, _) => OpenSettings();
            _popup.Hidden += (_, _) => _popupHiddenAt = DateTimeOffset.UtcNow;
        }

        return _popup;
    }

    private static RectInt32 BottomRightOfPrimaryDisplay()
    {
        var bounds = Microsoft.UI.Windowing.DisplayArea.Primary.OuterBounds;
        return new RectInt32(bounds.X + bounds.Width - 1, bounds.Y + bounds.Height - 1, 1, 1);
    }

    private void OnTrayContextMenu(PointInt32 point)
    {
        _popup?.Hide();
        var autostart = Autostart.IsEnabled;
        var choice = _tray.ShowMenu(
        [
            new TrayMenuItem(MenuSettings, "Settings…"),
            new TrayMenuItem(MenuAutostart, "Start with Windows", IsChecked: autostart),
            new TrayMenuItem(MenuRefresh, "Refresh"),
            TrayMenuItem.Separator,
            new TrayMenuItem(MenuExit, "Exit"),
        ], point);

        switch (choice)
        {
            case MenuSettings:
                OpenSettings();
                break;
            case MenuAutostart:
                Autostart.SetEnabled(!autostart);
                break;
            case MenuRefresh:
                RefreshNow();
                break;
            case MenuExit:
                _ = ExitAsync();
                break;
        }
    }

    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        // Everything seen now, plus devices that only exist in the settings file (e.g. hidden and currently off).
        var seen = _monitor.Current?.Readings.Select(r => (r.DeviceId, r.Name, r.Kind)).ToList() ?? [];
        var saved = _settings.Devices
            .Where(d => seen.All(s => s.DeviceId != d.Key))
            .Select(d => (d.Key, d.Value.DisplayName ?? d.Key, DeviceKind.Unknown));

        var viewModel = new SettingsViewModel(_settings, seen.Concat(saved), OnSettingsChanged);
        _settingsWindow = new SettingsWindow(viewModel, IconPath);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Activate();
    }

    private void OnSettingsChanged()
    {
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log($"Could not save settings: {ex.Message}");
        }

        if (_monitor.PollInterval != _settings.PollInterval)
            _monitor.PollInterval = _settings.PollInterval;
        if (_monitor.Current is { } snapshot)
            ApplyToUi(snapshot);
    }

    private void RefreshNow() => _ = RefreshNowAsync();

    private async Task RefreshNowAsync()
    {
        try
        {
            await _monitor.RefreshAsync();
        }
        catch (Exception ex)
        {
            Log($"Refresh failed: {ex}");
        }
    }

    private async Task ExitAsync()
    {
        _settingsWindow?.Close();
        _tray.Dispose();
        await _monitor.DisposeAsync();
        _popup?.Release();
        _singleInstance?.Dispose();
        Exit();
    }

    private static readonly FileLog s_log = new(FileLog.DefaultPath);

    private static void Log(string message)
    {
        Debug.WriteLine($"[WirelessStatus] {message}");
        s_log.Write(message);
    }
}
