// Abstraction over the host-level BLE radio: availability, the paired-devices
// list, and advertisement scanning. Hides the static InTheHand.Bluetooth
// Bluetooth class so the discovery + connect-by-id flows are unit-testable.

namespace Claudelk.Core.Bluetooth;

/// <summary>
/// Host-level BLE operations: check radio availability, enumerate paired
/// devices, and scan for nearby advertisements.
/// </summary>
public interface IBluetoothHost
{
    /// <summary>Returns true when a usable Bluetooth radio is present and enabled.</summary>
    Task<bool> IsAvailableAsync();

    /// <summary>Returns the devices currently in the OS's paired-devices list.</summary>
    Task<IReadOnlyList<IBluetoothDevice>> GetPairedDevicesAsync();

    /// <summary>
    /// Listens for BLE advertisements for <paramref name="timeout"/> and returns
    /// every device the radio observed. Filtering is the caller's job.
    /// </summary>
    Task<IReadOnlyList<IBluetoothDevice>> ScanForDevicesAsync(
        TimeSpan timeout, CancellationToken cancellationToken = default);
}
