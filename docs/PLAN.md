# WirelessStatus — Plan

A lightweight Windows 11 tray app that shows the battery level of wireless devices — Bluetooth as well as
proprietary 2.4 GHz dongles (Audeze Maxwell, Razer DeathAdder V4 Pro) — and notifies before they run out.

## Decisions

| Area | Choice |
|---|---|
| Language/runtime | C# / .NET 10, Native AOT (x64) |
| UI | WinUI 3 (Windows App SDK, self-contained, unpackaged) |
| Tray icon | Own `Shell_NotifyIcon` implementation via CsWin32 (no third-party tray lib) |
| Popup | Borderless WinUI 3 window with Mica, anchored above the tray icon (`Shell_NotifyIconGetRect`) |
| Notifications | Windows App SDK `AppNotificationManager` |
| HID | Hand-written `LibraryImport` P/Invoke (`hid.dll`, `cfgmgr32.dll`, `kernel32.dll`) in Core |
| Bluetooth | `Windows.Devices.Enumeration`, `DEVPKEY_Bluetooth_Battery` |
| MVVM | CommunityToolkit.Mvvm |
| Settings | JSON in `%LocalAppData%\WirelessStatus` (System.Text.Json source generator) |
| Low-battery threshold | Single threshold, default **10%** (configurable) |
| Distribution | Deferred — not a concern for now |
| More devices | Not now, but expected later → keep providers pluggable (PID tables) |

## UX

- **Left-click** tray icon → popup listing all devices: icon, name, %, progress bar, charging bolt;
  unavailable devices greyed out; "Updated hh:mm" + ⚙ in the footer. Closes on focus loss / Esc.
  Popup window is created once and hidden/shown for instant open.
- **Right-click** → native context menu: Refresh, Settings, Start with Windows, Exit.
- Tray icon glyph reflects the lowest connected device; tooltip lists all devices.
- Follows light/dark theme, per-monitor DPI, keyboard + screen reader accessible.

## Milestones

- **M0 – Scaffold**: solution, projects, shared build props. *(minimal version done alongside M1; WinUI app project still to add)*
- **M1 – Device spike** ✅: `tools/WirelessStatus.Probe` reads battery from Razer, Audeze and Bluetooth.
- **M2 – Core**: `DeviceMonitor` (polling, `WM_DEVICECHANGE` refresh), `AlertPolicy` (one alert per discharge
  cycle, reset on charge/recovery, ignore one-off dips/0% on wake), `SettingsStore`, unit tests.
- **M3 – Tray + popup**: WinUI app project, tray icon, popup positioning (any taskbar edge, multi-monitor), context menu.
- **M4 – Notifications + settings**: toasts, settings window, autostart (HKCU Run key), single instance, dynamic tray icon.
- **M5 – Polish**: idle CPU/RAM check, log file, AOT publish.

## Open issues from M1

- ✅ Bluetooth: stale values for disconnected devices — fixed by checking `System.Devices.Aep.IsConnected` via container id.
  Still to verify the connected case with the Xbox controller turned on.
- Keychron K3 Pro (Bluetooth) is paired but exposes no battery property — investigate later.
- ✅ Audeze % confirmed accurate against Audeze HQ. Not yet tested with Audeze HQ running.
- Razer wired (`00BE`) and wireless (`00BF`) show up as separate devices if both are connected — dedupe in M2.
