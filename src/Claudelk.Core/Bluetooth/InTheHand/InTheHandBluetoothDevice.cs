// Production adapter from InTheHand.Bluetooth.BluetoothDevice + RemoteGattServer
// down to the narrow IBluetoothDevice surface used by the protocol layer.

using InTheHand.Bluetooth;

namespace Claudelk.Core.Bluetooth.InTheHand;

/// <summary>
/// <see cref="IBluetoothDevice"/> backed by an InTheHand.Bluetooth
/// <see cref="BluetoothDevice"/>. Caches the resolved
/// <see cref="GattCharacteristic"/> per <c>(service, characteristic)</c> pair
/// so repeated writes don't walk the service tree.
/// </summary>
public sealed class InTheHandBluetoothDevice : IBluetoothDevice
{
    private readonly BluetoothDevice _device;
    private readonly Dictionary<(Guid Service, Guid Characteristic), GattCharacteristic> _cache = [];

    /// <summary>Wraps an already-discovered <paramref name="device"/>.</summary>
    public InTheHandBluetoothDevice(BluetoothDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
    }

    /// <inheritdoc/>
    public string Id => _device.Id;

    /// <inheritdoc/>
    public string Name => _device.Name ?? string.Empty;

    /// <inheritdoc/>
    public bool IsPaired => _device.IsPaired;

    /// <inheritdoc/>
    public bool IsConnected => _device.Gatt.IsConnected;

    /// <inheritdoc/>
    // InTheHand's PairAsync/ConnectAsync/WriteValueWithoutResponseAsync take no
    // token, so we bound each await with WaitAsync. This abandons the await on
    // cancellation but cannot stop a wedged native call — the CLI's process-level
    // watchdog (Program.Main) is the guaranteed kill for a truly hung adapter.
    public Task PairAsync(CancellationToken cancellationToken = default) =>
        _device.IsPaired ? Task.CompletedTask : _device.PairAsync().WaitAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (!_device.Gatt.IsConnected)
            await _device.Gatt.ConnectAsync().WaitAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public void Disconnect()
    {
        if (_device.Gatt.IsConnected)
            _device.Gatt.Disconnect();
    }

    /// <inheritdoc/>
    public async Task WriteWithoutResponseAsync(Guid serviceUuid, Guid characteristicUuid, byte[] data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        var characteristic = await ResolveCharacteristicAsync(serviceUuid, characteristicUuid, cancellationToken);
        await characteristic.WriteValueWithoutResponseAsync(data).WaitAsync(cancellationToken);
    }

    private async Task<GattCharacteristic> ResolveCharacteristicAsync(Guid serviceUuid, Guid characteristicUuid, CancellationToken cancellationToken)
    {
        var key = (serviceUuid, characteristicUuid);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        if (!_device.Gatt.IsConnected)
            await _device.Gatt.ConnectAsync().WaitAsync(cancellationToken);

        var service = await _device.Gatt.GetPrimaryServiceAsync(serviceUuid).WaitAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                $"GATT service {serviceUuid} not found on device {_device.Id}.");

        var characteristic = await service.GetCharacteristicAsync(characteristicUuid).WaitAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                $"GATT characteristic {characteristicUuid} not found on device {_device.Id}.");

        _cache[key] = characteristic;
        return characteristic;
    }

    /// <inheritdoc/>
    public void Dispose() => Disconnect();
}
