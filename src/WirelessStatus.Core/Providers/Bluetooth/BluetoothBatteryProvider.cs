using Windows.Devices.Enumeration;
using WirelessStatus.Core.Devices;

namespace WirelessStatus.Core.Providers.Bluetooth;

/// <summary>
/// Reads the battery level Windows itself collects from Bluetooth devices (BLE Battery Service and
/// classic Hands-Free battery reporting) — the same value shown in Settings → Bluetooth &amp; devices.
/// </summary>
public sealed class BluetoothBatteryProvider(Action<string>? log = null) : IBatteryProvider
{
    // DEVPKEY_Bluetooth_Battery
    private const string BatteryProperty = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    // DEVPKEY_Device_ContainerId: groups the several device nodes that make up one physical device.
    private const string ContainerIdProperty = "System.Devices.ContainerId";

    // AQS can't filter on the battery DEVPKEY itself, so narrow to Bluetooth device nodes and filter in code.
    private const string BluetoothNodesFilter =
        "System.Devices.DeviceInstanceId:~<\"BTHENUM\\\" OR System.Devices.DeviceInstanceId:~<\"BTHLE\\\"";

    private const string AepContainerIdProperty = "System.Devices.Aep.ContainerId";
    private const string AepIsConnectedProperty = "System.Devices.Aep.IsConnected";

    // Use the platform's per-protocol selectors: a hand-written AQS that ORs the classic and LE protocol ids
    // takes ~30 s to complete (the query waits on other AEP providers to time out), while these take ~10 ms each.
    private static readonly string[] PairedEndpointSelectors =
    [
        Windows.Devices.Bluetooth.BluetoothDevice.GetDeviceSelectorFromPairingState(true),
        Windows.Devices.Bluetooth.BluetoothLEDevice.GetDeviceSelectorFromPairingState(true),
    ];

    public string Name => "Bluetooth";

    public async Task<IReadOnlyList<BatteryReading>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var nodesTask = DeviceInformation
            .FindAllAsync(BluetoothNodesFilter, [BatteryProperty, ContainerIdProperty], DeviceInformationKind.Device)
            .AsTask(cancellationToken);
        var connectedTask = GetConnectedContainersAsync(cancellationToken);
        var nodes = await nodesTask;
        var connectedContainers = await connectedTask;

        var readings = new List<BatteryReading>();
        foreach (var node in nodes)
        {
            if (node.Properties.GetValueOrDefault(BatteryProperty) is not byte level)
                continue;

            var containerId = node.Properties.GetValueOrDefault(ContainerIdProperty) as Guid?;

            // Windows keeps the last reported level after a device disconnects, so only trust it while connected.
            var isConnected = containerId is { } id && connectedContainers.Contains(id);
            log?.Invoke($"Bluetooth: {node.Name} = {level}% connected={isConnected} ({node.Id})");

            readings.Add(new BatteryReading(
                $"bt:{containerId?.ToString() ?? node.Id}",
                node.Name,
                DeviceKind.Unknown,
                isConnected ? DeviceState.Connected : DeviceState.Unavailable,
                isConnected ? level : null,
                null));
        }

        // A physical device can expose the property on more than one node; keep one per container.
        return readings.DistinctBy(r => r.DeviceId).ToList();
    }

    /// <summary>
    /// Container ids of paired Bluetooth devices (classic and LE) that are currently connected. Connection state lives on
    /// the association endpoint, not on the device nodes that carry the battery value; the container id links the two.
    /// </summary>
    private async Task<HashSet<Guid>> GetConnectedContainersAsync(CancellationToken cancellationToken)
    {
        var results = await Task.WhenAll(PairedEndpointSelectors.Select(selector => DeviceInformation
            .FindAllAsync(selector, [AepContainerIdProperty, AepIsConnectedProperty], DeviceInformationKind.AssociationEndpoint)
            .AsTask(cancellationToken)));

        var connected = new HashSet<Guid>();
        foreach (var endpoint in results.SelectMany(r => r))
        {
            log?.Invoke($"Bluetooth: paired endpoint {endpoint.Name} connected={endpoint.Properties.GetValueOrDefault(AepIsConnectedProperty)} " +
                        $"container={endpoint.Properties.GetValueOrDefault(AepContainerIdProperty)}");
            if (endpoint.Properties.GetValueOrDefault(AepIsConnectedProperty) is true &&
                endpoint.Properties.GetValueOrDefault(AepContainerIdProperty) is Guid containerId)
            {
                connected.Add(containerId);
            }
        }

        return connected;
    }
}
