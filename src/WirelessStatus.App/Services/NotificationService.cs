using System.Security;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using Windows.Win32;
using WirelessStatus.Core.Monitoring;

namespace WirelessStatus.App.Services;

/// <summary>
/// Low-battery toasts. Clicking a toast raises <see cref="Invoked"/> (while the app is running).
/// </summary>
/// <remarks>
/// Uses the classic <c>Windows.UI.Notifications</c> API with a per-user AppUserModelID registration instead of the
/// Windows App SDK's <c>AppNotificationManager</c>: its <c>Register()</c> throws 0x8007007E in self-contained unpackaged
/// apps (microsoft/WindowsAppSDK#6774, still broken in 2.5.1). Revisit once a fixed release ships.
/// </remarks>
internal sealed class NotificationService
{
    // Windows caches a toast's display name and icon per AUMID the first time it is used, so changing the icon later
    // needs a new AUMID (this one replaced "WirelessStatus", whose cached icon was the template placeholder).
    private const string AppUserModelId = "WirelessStatus.App";

    private readonly Action<string> _log;
    private readonly ToastNotifier? _notifier;

    public NotificationService(string iconPath, Action<string> log)
    {
        _log = log;
        try
        {
            // Registering the AUMID under HKCU gives unpackaged toasts their display name and icon.
            using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppUserModelId}"))
            {
                key.SetValue("DisplayName", "WirelessStatus");
                key.SetValue("IconUri", iconPath);
            }

            PInvoke.SetCurrentProcessExplicitAppUserModelID(AppUserModelId).ThrowOnFailure();
            _notifier = ToastNotificationManager.CreateToastNotifier(AppUserModelId);
        }
        catch (Exception ex)
        {
            _log($"Notifications unavailable (0x{ex.HResult:X8}): {ex.Message}");
        }
    }

    /// <summary>A toast was clicked. Raised on a background thread.</summary>
    public event Action? Invoked;

    public void ShowLowBattery(LowBatteryAlert alert, string deviceName) =>
        Show($"{deviceName} battery is low", $"{alert.Reading.Level}% remaining. Charge it soon.");

    private void Show(string title, string body)
    {
        if (_notifier is null)
            return;

        try
        {
            var xml = new XmlDocument();
            xml.LoadXml(
                "<toast><visual><binding template=\"ToastGeneric\">" +
                $"<text>{SecurityElement.Escape(title)}</text><text>{SecurityElement.Escape(body)}</text>" +
                "</binding></visual></toast>");
            var toast = new ToastNotification(xml);
            toast.Activated += (_, _) => Invoked?.Invoke();
            _notifier.Show(toast);
        }
        catch (Exception ex)
        {
            _log($"Could not show notification (0x{ex.HResult:X8}): {ex.Message}");
        }
    }
}
