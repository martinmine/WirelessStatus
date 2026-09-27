# WirelessStatus

Lightweight Windows 11 system-tray app (C# / .NET 10 / WinUI 3, Native AOT) that shows the battery level of wireless
devices — including non-Bluetooth 2.4 GHz dongles like the Audeze Maxwell and Razer DeathAdder V4 Pro — and sends a
notification when a battery gets low. Left-clicking the tray icon opens a small popup above the tray listing devices.

The full plan, decisions and milestone status are in [docs/PLAN.md](docs/PLAN.md) — keep it updated as milestones complete.

## Layout

```
src/WirelessStatus.Core      Class library, no UI.
  Devices/                   BatteryReading, IBatteryProvider
  Hid/                       HidNative (LibraryImport P/Invoke), HidDevice (enumerate/open/feature/input/output reports)
  Providers/Razer|Audeze|Bluetooth
  Monitoring/                DeviceMonitor (polling, debounced RequestRefresh, per-provider timeout), AlertPolicy
  Settings/                  AppSettings (+ source-generated JSON context), SettingsStore (%LocalAppData%\WirelessStatus)
  Diagnostics/FileLog        Best-effort log at %LocalAppData%\WirelessStatus\log.txt (rotates at 1 MB)
tools/WirelessStatus.Probe   Console tool for exercising providers/monitor against real hardware
tests/WirelessStatus.Core.Tests  xUnit tests for Core (no hardware needed)
src/WirelessStatus.App       WinUI 3 tray app (unpackaged, self-contained), exe name WirelessStatus.exe
  Tray/TrayIcon.cs           Shell_NotifyIcon via CsWin32: hidden top-level window, click/menu/TaskbarCreated/WM_DEVICECHANGE
  Views/PopupWindow          Frameless acrylic popup anchored above the tray icon, hides on focus loss / Esc
  Tray/TrayIconRenderer      Draws the tray icon: Segoe Fluent U+E957 (device + wireless arcs) in the taskbar theme colour.
                             Not a battery shape — that reads as the system battery icon
  Views/SettingsWindow       Threshold, poll interval, autostart, per-device name/show/notify; saves on every change
  Services/                  NotificationService (toasts), Autostart (HKCU Run key)
  ViewModels/                PopupViewModel, DeviceViewModel, SettingsViewModel (CommunityToolkit.Mvvm partial properties)
```

## Commands

```bash
dotnet build WirelessStatus.slnx
dotnet test tests/WirelessStatus.Core.Tests
dotnet run --project tools/WirelessStatus.Probe            # read all providers once
dotnet run --project tools/WirelessStatus.Probe -- -v      # with raw protocol logging
dotnet run --project tools/WirelessStatus.Probe -- hid 1532  # list HID collections for a vendor id (hex)
dotnet run --project tools/WirelessStatus.Probe -- watch --threshold 60 --interval 5   # live monitor + alerts
dotnet run --project tools/WirelessStatus.Probe -- maxwell-dump     # raw replies to all known Maxwell status reads
dotnet run --project tools/WirelessStatus.Probe -- maxwell-listen 60  # unsolicited Maxwell reports
dotnet build src/WirelessStatus.App   # then run bin/Debug/net10.0-windows10.0.26100.0/win-x64/WirelessStatus.exe
powershell -File tools/publish.ps1    # Release: Native AOT folder in src/WirelessStatus.App/bin/publish (~64 MB)
powershell -File tools/make-icon.ps1  # regenerate Assets/AppIcon.ico + AppIcon.png
```

## Conventions

- Target `net10.0-windows10.0.19041.0` (set in `Directory.Build.props`), nullable on, warnings as errors, `IsAotCompatible`.
  Everything must stay trim/AOT-safe: no reflection-based serialization, use source generators.
- HID interop is hand-written `LibraryImport` in `HidNative.cs`. CsWin32 is intended for the App's Win32 tray work.
- Providers implement `IBatteryProvider`, never throw for device-level failures (return `DeviceState.Unavailable`), and
  take an optional `Action<string>? log` for protocol logging. `DeviceId`s are persisted in settings — keep them stable
  (e.g. Razer uses a model key so wired + receiver PIDs are one device).
- Open HID handles only for the duration of a read — Razer Synapse / Audeze HQ also talk to these devices.
- Protocol knowledge comes from HeadsetControl and OpenRazer, which are **GPL** — reimplement from the documented
  protocol, don't copy their code.
- Default low-battery threshold is a single **10%**. Distribution/packaging is explicitly out of scope for now.
  More device types will be added later, so keep device support table-driven.
- Time-dependent code takes a `TimeProvider`; tests use `FakeTimeProvider`. Keep pure parsing logic in `internal static`
  methods (Core has `InternalsVisibleTo` the test project) and test it with captured packets.
- Settings records use `{ get; set; }`, **not `init`**: the System.Text.Json source generator assigns all init-only
  properties in one object initializer, so properties missing from the JSON get `default(T)` instead of their defaults.
- The App is responsible for calling `DeviceMonitor.RequestRefresh()` on `WM_DEVICECHANGE`, feeding each
  `DeviceMonitor.Updated` snapshot to `AlertPolicy.Evaluate`, and marshalling to the UI thread (`Updated` fires on the
  thread pool).
- **App is unpackaged + self-contained on purpose** (`WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`):
  it starts from the HKCU Run key and runs directly from the IDE. Don't convert it to MSIX without asking.
  The App targets `net10.0-windows10.0.26100.0` (Windows App SDK needs it); Core stays on 19041.
- App Win32 interop uses CsWin32 (`NativeMethods.txt`, `allowMarshaling: false`); window procedures are
  `[UnmanagedCallersOnly]` function pointers (AOT-safe). Constants missing from the Win32 metadata are defined
  locally (e.g. `NIN_KEYSELECT`).
- The app has no main window: `DispatcherShutdownMode.OnExplicitShutdown`, the popup is created once and
  hidden/shown, Alt+F4 on it hides instead of closing. Exit goes through the tray menu.
- UI testing without a mouse: post the tray callback message (`WM_APP+1`, lParam `0x400` = NIN_SELECT or
  `0x7B` = WM_CONTEXTMENU, wParam = anchor y<<16|x) to the window of class `WirelessStatus.TrayWindow`, then screenshot.
- **Toasts use classic `Windows.UI.Notifications`**, not the Windows App SDK `AppNotificationManager`: its `Register()`
  throws 0x8007007E in self-contained unpackaged apps (microsoft/WindowsAppSDK#6774, broken through 2.5.1). The AUMID
  `WirelessStatus.App` is registered under `HKCU\Software\Classes\AppUserModelId`. Toast clicks only reach the app while
  it is running (no COM activator). Revisit when a fixed Windows App SDK ships.
- The App references `Microsoft.WindowsAppSDK.WinUI` (+ pinned InteractiveExperiences), **not** the
  `Microsoft.WindowsAppSDK` metapackage — the metapackage adds AI/ML/Widgets/Search (~60 MB) to the self-contained output.
- "Start with Windows" lives only in the Run key (`Autostart`), not in settings, so the registry is the single source
  of truth. `UpdatePathIfEnabled()` runs at startup.
- Single instance: named mutex `Local\WirelessStatus.SingleInstance`; a second launch posts `WM_APP+2` to the tray
  window (→ show popup) and exits.
- App log: `%LocalAppData%\WirelessStatus\log.txt` — check it first when something silently doesn't happen.
- More UI testing without a mouse: UI Automation (Invoke/Toggle/Value patterns by `AutomationProperties.Name`) drives
  buttons and settings; `PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT)` captures windows that are behind others.
- **Native AOT (Release publish) lessons** — each of these broke the published app while Debug worked:
  - `EnableMsixTooling` must stay `true` even though the app is unpackaged; otherwise publish omits
    `WirelessStatus.pri` (compiled XAML) and the app dies at startup with 0xC000027B in Microsoft.UI.Xaml.dll.
  - Core needs `CsWinRTAotOptimizerEnabled=true`, or WinRT calls taking collections (e.g. the property list for
    `DeviceInformation.FindAllAsync`) fail at runtime with "Failed to create a CCW". Probe/tests run JIT and won't
    catch this — always smoke-test the published build (popup, Bluetooth row, a toast, the settings window).
  - The AOT linker finds MSVC via `vswhere.exe`, which isn't on PATH by default; `tools/publish.ps1` handles it.
- Popup sizing: the content must fit the **client** area (window minus border). Size the window as content + actual
  frame (`AppWindow.Size - AppWindow.ClientSize`); `AppWindow.ResizeClient` over-allocates by a caption bar the popup
  doesn't have. The device list shows at most 5 rows (`MaxVisibleDevices`) before it scrolls.
- Debug builds: set `WIRELESSSTATUS_FAKE_DEVICES=N` to add N made-up devices (`Diagnostics/FakeDeviceProvider`,
  compiled out of Release) — for testing list layout/scrolling. UIA `ScrollPattern.VerticallyScrollable` on the popup
  tells whether the list scrolls.
- Memory (AOT, measured): ~71 MB working set / ~61 MB private idle before the popup is first opened, ~108/~90 MB
  after. The popup is created lazily and then kept: closing a WinUI window does **not** return that memory (measured),
  so releasing it only adds latency. Idle CPU ≈ 50 ms per minute.
- Windows caches a toast's name/icon per AUMID on first use; changing the toast icon needs a new AUMID. Toast icons
  must be PNG (`AppIcon.png`); an `.ico` IconUri shows the generic app icon.

## Device protocol notes (verified on the dev machine with M1 Probe)

### Razer (VID `1532`) — DeathAdder V4 Pro: `00BE` wired, `00BF` HyperSpeed receiver
- 90-byte Razer report sent as **feature report id 0** (91-byte buffer) via `HidD_SetFeature`, wait ~35 ms, then `HidD_GetFeature`.
- The feature report lives on the **mouse collection** (MI_00, usage page 1 / usage 2, feat=91). Windows holds that
  collection exclusively, so it is opened with **access 0** — feature reports still work.
- Report layout: `status, txn_id, remaining(BE16), protocol, data_size, class, cmd, args[80], crc, reserved`;
  CRC = XOR of report bytes 2..87. Transaction id `0x1F` for DeathAdder V4 Pro (per-model; see OpenRazer).
- Battery: class `0x07` cmd `0x80`, `args[1]` = 0–255. Charging: class `0x07` cmd `0x84`, `args[1]` != 0.
- Status `0x02` = success, `0x01` busy (retry), `0x04` timeout / `0x05` not supported → mouse off/asleep.
- Works while Razer Synapse is running.

### Audeze Maxwell (VID `3329`) — `4B18` Xbox dongle, `4B19` PC/PS dongle
- Vendor collection MI_00, usage page `0xFF13`, 62-byte input/output reports.
- Send output report `06 07 80 05 5A 03 00 D6 0C` (WriteFile, zero-padded to 62), wait ~60 ms, then
  `HidD_GetInputReport` with report id `0x07`. Packets must be ≥ ~50–60 ms apart.
- Input reports contain several queued sub-messages; the battery reply is `5D 05 00 D6 0C 00 00 <level>`.
  Search for `D6 0C 00 00` and take the next byte; poll a few input reports if not found.
- ~1 in 5 requests gets no reply at all (only empty `07 00 80` reports) while Audeze HQ is running; re-sending the
  request recovers it, so the provider makes 2 attempts before reporting `Unavailable`.
- **Charging state: not supported by the headset** (closed). Plugged-in vs unplugged `maxwell-dump` captures differ
  only in the battery level, no unsolicited report is sent, and Audeze HQ itself has no charging indicator.
  `IsCharging` is always null for the Maxwell; don't guess it from a rising level (decided against).
- Input report 0x07 = `07 <len> 80` + `<len>` bytes of messages `05 <type> <payload len LE16> <payload>`
  (5B = request echo, 5C = ?, 5D = reply). Bytes past `<len>` are stale leftovers — never parse them.
  Firmware version reply: request `07 1C` → payload `07 1C 00 00 09 "v1.0.1.7"`.

### Bluetooth
- Windows exposes battery as `DEVPKEY_Bluetooth_Battery` = `{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2` (byte) on
  device nodes. AQS cannot filter on this key ("Full text search is not supported"), so enumerate all device nodes
  (`DeviceInformationKind.Device`) and filter in code. Dedupe by `System.Devices.ContainerId`.
- Windows keeps the **last battery value after a device disconnects**. Connection state is on the paired Bluetooth
  association endpoints (`DeviceInformationKind.AssociationEndpoint`, `System.Devices.Aep.IsConnected`), linked to the
  battery device node via container id (`System.Devices.Aep.ContainerId` == `System.Devices.ContainerId`).
  Disconnected devices are reported as `Unavailable` with no level.
- **Performance trap:** hand-written association-endpoint AQS queries (e.g. `IsPaired AND (ProtocolId=classic OR
  ProtocolId=LE)`) take **30–60 s** to finish. Always use `BluetoothDevice/BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)`
  (~10 ms each). Likewise filter device nodes by `System.Devices.DeviceInstanceId:~<"BTHENUM\"` / `"BTHLE\"`
  instead of enumerating all nodes (~15 ms vs ~200 ms).

## Dev machine hardware
Razer DeathAdder V4 Pro (receiver `00BF`), Razer Huntsman (`0227`, wired), Razer Goliathus Chroma (`0C02`),
Audeze Maxwell (`4B18`), Xbox Wireless Controller (BLE), Keychron K3 Pro (BT, no battery property exposed).
