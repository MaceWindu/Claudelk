// BLE scanner for ELK-BLEDOM devices using InTheHand.BluetoothLE (32feet.NET).
//
// On Windows the BLE advertisement name is delivered in the scan response and
// 32feet.NET's name-based filters are unreliable. We instead scan everything
// (`AcceptAllDevices`) and filter in code by the well-known name prefixes.

using InTheHand.Bluetooth;
using IhBluetooth = InTheHand.Bluetooth.Bluetooth;

namespace Claudelk.Core.Bluetooth;

/// <summary>
/// Discovers nearby ELK-BLEDOM-family LED strips via BLE advertisement scanning.
/// </summary>
public static class ElkBledomScanner
{
    private static readonly string[] KnownPrefixes =
        ["ELK-BLEDOM", "ELK-BLE", "ELK-BULB", "ELK-LAMPL", "LEDBLE", "MELK"];

    /// <summary>
    /// Scans for BLE devices and returns those whose advertised name matches a
    /// known ELK-BLEDOM-family prefix.
    /// </summary>
    /// <param name="duration">How long to listen for advertisements. Defaults to 10 seconds.</param>
    /// <param name="onSeen">Optional callback invoked for every BLE device the radio saw, before filtering. Useful for diagnostics.</param>
    /// <param name="cancellationToken">Cancels the scan early.</param>
    /// <returns>The subset of nearby devices that look like ELK-BLEDOM strips.</returns>
    /// <exception cref="InvalidOperationException">Bluetooth is unavailable or disabled in Windows.</exception>
    public static async Task<IReadOnlyList<BluetoothDevice>> ScanAsync(
        TimeSpan? duration = null,
        Action<BluetoothDevice>? onSeen = null,
        CancellationToken cancellationToken = default)
    {
        if (!await IhBluetooth.GetAvailabilityAsync())
            throw new InvalidOperationException(
                "Bluetooth is not available. Enable Bluetooth in Windows Settings and try again.");

        var options = new RequestDeviceOptions
        {
            AcceptAllDevices = true,
            Timeout = duration ?? TimeSpan.FromSeconds(10),
        };
        var all = await IhBluetooth.ScanForDevicesAsync(options, cancellationToken);

        var matches = new List<BluetoothDevice>();
        foreach (var d in all)
        {
            onSeen?.Invoke(d);
            if (IsLikelyElkBledom(d))
                matches.Add(d);
        }

        return matches;
    }

    /// <summary>
    /// Returns true when the device's advertised name starts with one of the
    /// known ELK-BLEDOM-family prefixes.
    /// </summary>
    public static bool IsLikelyElkBledom(BluetoothDevice device)
    {
        var name = device.Name;
        if (string.IsNullOrEmpty(name)) return false;
        foreach (var prefix in KnownPrefixes)
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
