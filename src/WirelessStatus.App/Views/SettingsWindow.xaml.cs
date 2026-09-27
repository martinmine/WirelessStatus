using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using Windows.Win32;
using WirelessStatus.App.ViewModels;

namespace WirelessStatus.App.Views;

public sealed partial class SettingsWindow : Window
{
    private const int WidthDip = 560;
    private const int HeightDip = 680;

    public SettingsWindow(SettingsViewModel viewModel, string iconPath)
    {
        ViewModel = viewModel;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(iconPath);

        // Size for the window's DPI and centre it on the primary display's work area.
        var scale = PInvoke.GetDpiForWindow(new(Win32Interop.GetWindowFromWindowId(AppWindow.Id))) / 96.0;
        var size = new SizeInt32((int)(WidthDip * scale), (int)(HeightDip * scale));
        var work = DisplayArea.Primary.WorkArea;
        AppWindow.MoveAndResize(new RectInt32(
            work.X + (work.Width - size.Width) / 2,
            work.Y + (work.Height - size.Height) / 2,
            size.Width,
            size.Height));
    }

    public SettingsViewModel ViewModel { get; }
}
