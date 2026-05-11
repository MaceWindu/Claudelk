// Abstraction over a single BLE peripheral. Hides InTheHand.Bluetooth's
// BluetoothDevice (which has no public constructor) so the protocol-level
// command layer is unit-testable with a fake.

namespace Claudelk.Core.Bluetooth;

/// <summary>
/// A discoverable BLE peripheral plus the narrow surface ElkBledomDevice needs
/// to talk to it: connect, pair, and write-without-response to a GATT
/// characteristic. Implementations are expected to cache the GATT
/// characteristic per (service, characteristic) pair so repeat writes don't
/// re-walk the service tree.
/// </summary>
public interface IBluetoothDevice : IDisposable
{
    /// <summary>Opaque BLE device id (MAC address on Windows).</summary>
    string Id { get; }

    /// <summary>Advertised device name. May be empty if the device hasn't published one.</summary>
    string Name { get; }

    /// <summary>True if the device is in Windows' paired-devices list.</summary>
    bool IsPaired { get; }

    /// <summary>True while the GATT connection is open.</summary>
    bool IsConnected { get; }

    /// <summary>Adds the device to the OS-level paired list. No-op if already paired.</summary>
    Task PairAsync();

    /// <summary>Opens the GATT connection if it isn't already open.</summary>
    Task ConnectAsync();

    /// <summary>Closes the GATT connection if it's currently open.</summary>
    void Disconnect();

    /// <summary>
    /// Writes <paramref name="data"/> to the given GATT characteristic without
    /// waiting for an acknowledgement. Implementations may discover and cache
    /// the characteristic lazily on first call.
    /// </summary>
    Task WriteWithoutResponseAsync(Guid serviceUuid, Guid characteristicUuid, byte[] data);
}
