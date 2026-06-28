// Production adapter that fronts the static InTheHand.Bluetooth.Bluetooth
// class as an IBluetoothHost. The alias is required because the InTheHand
// namespace shadows our own Claudelk.Core.Bluetooth namespace.

using InTheHand.Bluetooth;
using IhBluetooth = InTheHand.Bluetooth.Bluetooth;

namespace Claudelk.Core.Bluetooth.InTheHand;

/// <summary>
/// <see cref="IBluetoothHost"/> backed by the InTheHand.Bluetooth 32feet.NET
/// implementation. This is the default host used by <see cref="ElkBledomScanner"/>
/// and <see cref="ElkBledomDevice"/> when no fake is injected.
/// </summary>
public sealed class InTheHandBluetoothHost : IBluetoothHost
{
    /// <inheritdoc/>
    // InTheHand's GetAvailabilityAsync takes no token, so we bound the await with
    // WaitAsync. Note: this only abandons the await — a wedged native call keeps
    // running. The CLI's process-level watchdog (Program.Main) is the real backstop.
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        IhBluetooth.GetAvailabilityAsync().WaitAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<IBluetoothDevice>> GetPairedDevicesAsync(CancellationToken cancellationToken = default)
    {
        var paired = await IhBluetooth.GetPairedDevicesAsync().WaitAsync(cancellationToken);
        return Wrap(paired);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<IBluetoothDevice>> ScanForDevicesAsync(
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        // Web-Bluetooth-spec property: AcceptAllDevices (NOT AcceptAllAdvertisements).
        // Name-based filters are unreliable on Windows — the radio puts the name in
        // the scan response, not the advertisement — so we accept everything and let
        // callers filter.
        var options = new RequestDeviceOptions
        {
            AcceptAllDevices = true,
            Timeout = timeout,
        };
        // ScanForDevicesAsync is the one InTheHand method that takes a token, so
        // the cancellation here genuinely propagates to the native scan.
        var devices = await IhBluetooth.ScanForDevicesAsync(options, cancellationToken);
        return Wrap(devices);
    }

    private static List<IBluetoothDevice> Wrap(IReadOnlyCollection<BluetoothDevice> devices)
    {
        var wrapped = new List<IBluetoothDevice>(devices.Count);
        foreach (var d in devices)
            wrapped.Add(new InTheHandBluetoothDevice(d));
        return wrapped;
    }
}
