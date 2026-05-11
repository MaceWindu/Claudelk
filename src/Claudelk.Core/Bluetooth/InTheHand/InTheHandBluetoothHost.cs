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
    public Task<bool> IsAvailableAsync() => IhBluetooth.GetAvailabilityAsync();

    /// <inheritdoc/>
    public async Task<IReadOnlyList<IBluetoothDevice>> GetPairedDevicesAsync()
    {
        var paired = await IhBluetooth.GetPairedDevicesAsync();
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
