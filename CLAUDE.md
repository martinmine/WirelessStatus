# WirelessStatus

Lightweight Windows 11 system-tray app (C# / .NET 10 / WinUI 3, Native AOT) that shows the battery level of wireless
devices — including non-Bluetooth 2.4 GHz dongles like the Audeze Maxwell and Razer DeathAdder V4 Pro — and sends a
notification when a battery gets low. Left-clicking the tray icon opens a small popup above the tray listing devices.

The full plan, decisions and milestone status are in [docs/PLAN.md](docs/PLAN.md) — keep it updated as milestones complete.

## Layout

```
src/WirelessStatus.Core      Class library, no UI. Device providers, HID interop, (later) monitor/alerts/settings.
  Devices/                   BatteryReading, IBatteryProvider
  Hid/                       HidNative (LibraryImport P/Invoke), HidDevice (enumerate/open/feature/input/output reports)
  Providers/Razer|Audeze|Bluetooth
tools/WirelessStatus.Probe   Console tool for exercising providers against real hardware
src/WirelessStatus.App       (planned, M3) WinUI 3 tray app
tests/                       (planned, M2) xUnit tests for Core
```

## Commands

```bash
dotnet build WirelessStatus.slnx
dotnet run --project tools/WirelessStatus.Probe            # read all providers
dotnet run --project tools/WirelessStatus.Probe -- -v      # with raw protocol logging
dotnet run --project tools/WirelessStatus.Probe -- hid 1532  # list HID collections for a vendor id (hex)
```

## Conventions

- Target `net10.0-windows10.0.19041.0` (set in `Directory.Build.props`), nullable on, warnings as errors, `IsAotCompatible`.
  Everything must stay trim/AOT-safe: no reflection-based serialization, use source generators.
- HID interop is hand-written `LibraryImport` in `HidNative.cs`. CsWin32 is intended for the App's Win32 tray work.
- Providers implement `IBatteryProvider`, never throw for device-level failures (return `DeviceState.Unavailable`), and
  take an optional `Action<string>? log` for protocol logging.
- Open HID handles only for the duration of a read — Razer Synapse / Audeze HQ also talk to these devices.
- Protocol knowledge comes from HeadsetControl and OpenRazer, which are **GPL** — reimplement from the documented
  protocol, don't copy their code.
- Default low-battery threshold is a single **10%**. Distribution/packaging is explicitly out of scope for now.
  More device types will be added later, so keep device support table-driven.

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
- Charging state not yet known.

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
