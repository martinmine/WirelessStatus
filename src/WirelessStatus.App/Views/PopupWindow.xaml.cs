using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using WirelessStatus.App.ViewModels;

namespace WirelessStatus.App.Views;

/// <summary>
/// Frameless popup listing devices, anchored to the tray icon like the system flyouts. Created once and then
/// hidden/shown so it opens instantly; hides itself when it loses focus or on Esc.
/// </summary>
public sealed partial class PopupWindow : Window
{
    private const double WidthDip = 360;
    private const double MarginDip = 12;
    private const double MaxHeightFraction = 0.8;

    private RectInt32 _anchor;
    private bool _allowClose;

    public PopupWindow(PopupViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.Closing += OnClosing;
        Activated += OnActivated;

        // The list can grow or shrink after a refresh while the popup is open.
        ViewModel.Devices.CollectionChanged += (_, _) =>
        {
            if (AppWindow.IsVisible)
                DispatcherQueue.TryEnqueue(Reposition);
        };
    }

    public PopupViewModel ViewModel { get; }

    public bool IsOpen => AppWindow.IsVisible;

    /// <summary>When the popup was last hidden; used to ignore the tray click that caused it to lose focus.</summary>
    public DateTimeOffset LastHiddenAt { get; private set; }

    public event EventHandler? RefreshRequested;

    public event EventHandler? SettingsRequested;

    /// <param name="anchor">Tray icon bounds (or the click point) in physical screen pixels.</param>
    public void ShowAt(RectInt32 anchor)
    {
        _anchor = anchor;
        Reposition();
        AppWindow.Show();
        Activate();
        PInvoke.SetForegroundWindow(new(Win32Interop.GetWindowFromWindowId(AppWindow.Id)));

        // The first time, the content isn't laid out until the window is shown; size it again once it is.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, Reposition);
    }

    public void Hide()
    {
        if (!AppWindow.IsVisible)
            return;
        AppWindow.Hide();
        LastHiddenAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Closes the window for real (app exit).</summary>
    public void CloseForExit()
    {
        _allowClose = true;
        Close();
    }

    private void Reposition()
    {
        var display = DisplayArea.GetFromPoint(new PointInt32(_anchor.X + _anchor.Width / 2, _anchor.Y + _anchor.Height / 2), DisplayAreaFallback.Nearest);
        var work = display.WorkArea;
        var scale = GetScale(display);

        Root.Measure(new Size(WidthDip, double.PositiveInfinity));
        var heightDip = Root.DesiredSize.Height > 0 ? Root.DesiredSize.Height : 200;

        var margin = (int)Math.Round(MarginDip * scale);
        var width = (int)Math.Round(WidthDip * scale);
        var height = Math.Min((int)Math.Ceiling(heightDip * scale), (int)(work.Height * MaxHeightFraction));

        var (x, y) = GetTaskbarEdge(display) switch
        {
            TaskbarEdge.Top => (CenterOn(_anchor.X, _anchor.Width, width), Math.Max(_anchor.Y + _anchor.Height, work.Y) + margin),
            TaskbarEdge.Left => (Math.Max(_anchor.X + _anchor.Width, work.X) + margin, CenterOn(_anchor.Y, _anchor.Height, height)),
            TaskbarEdge.Right => (Math.Min(_anchor.X, work.X + work.Width) - width - margin, CenterOn(_anchor.Y, _anchor.Height, height)),
            _ => (CenterOn(_anchor.X, _anchor.Width, width), Math.Min(_anchor.Y, work.Y + work.Height) - height - margin),
        };

        x = Math.Clamp(x, work.X + margin, Math.Max(work.X + margin, work.X + work.Width - width - margin));
        y = Math.Clamp(y, work.Y + margin, Math.Max(work.Y + margin, work.Y + work.Height - height - margin));
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    private static int CenterOn(int start, int length, int size) => start + length / 2 - size / 2;

    private enum TaskbarEdge { Bottom, Top, Left, Right }

    /// <summary>The taskbar sits on the side where the work area is smaller than the monitor.</summary>
    private TaskbarEdge GetTaskbarEdge(DisplayArea display)
    {
        var outer = display.OuterBounds;
        var work = display.WorkArea;
        (TaskbarEdge Edge, int Gap)[] gaps =
        [
            (TaskbarEdge.Bottom, outer.Y + outer.Height - (work.Y + work.Height)),
            (TaskbarEdge.Top, work.Y - outer.Y),
            (TaskbarEdge.Left, work.X - outer.X),
            (TaskbarEdge.Right, outer.X + outer.Width - (work.X + work.Width)),
        ];
        var largest = gaps.MaxBy(g => g.Gap);
        if (largest.Gap > 0)
            return largest.Edge;

        // Auto-hidden taskbar: use whichever monitor edge the tray icon is closest to.
        var cx = _anchor.X + _anchor.Width / 2 - outer.X;
        var cy = _anchor.Y + _anchor.Height / 2 - outer.Y;
        (TaskbarEdge Edge, int Distance)[] distances =
        [
            (TaskbarEdge.Bottom, outer.Height - cy),
            (TaskbarEdge.Top, cy),
            (TaskbarEdge.Left, cx),
            (TaskbarEdge.Right, outer.Width - cx),
        ];
        return distances.MinBy(d => d.Distance).Edge;
    }

    private static double GetScale(DisplayArea display)
    {
        var bounds = display.OuterBounds;
        var monitor = PInvoke.MonitorFromPoint(new(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2),
            MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        return PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _).Succeeded
            ? dpiX / 96.0
            : 1.0;
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
            Hide();
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        // Alt+F4 hides the popup instead of destroying it.
        if (_allowClose)
            return;
        args.Cancel = true;
        Hide();
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke(this, EventArgs.Empty);

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }
}
