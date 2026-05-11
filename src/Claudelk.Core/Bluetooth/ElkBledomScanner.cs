// BLE scanner for ELK-BLEDOM devices. Talks to the radio through IBluetoothHost
// so the filtering logic can be unit-tested with a fake host.

using Claudelk.Core.Bluetooth.InTheHand;

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
    /// <param name="host">Optional BLE host. Defaults to <see cref="InTheHandBluetoothHost"/>.</param>
    /// <param name="cancellationToken">Cancels the scan early.</param>
    /// <returns>The subset of nearby devices that look like ELK-BLEDOM strips.</returns>
    /// <exception cref="InvalidOperationException">Bluetooth is unavailable or disabled in Windows.</exception>
    public static async Task<IReadOnlyList<IBluetoothDevice>> ScanAsync(
        TimeSpan? duration = null,
        Action<IBluetoothDevice>? onSeen = null,
        IBluetoothHost? host = null,
        CancellationToken cancellationToken = default)
    {
        host ??= new InTheHandBluetoothHost();

        if (!await host.IsAvailableAsync())
            throw new InvalidOperationException(
                "Bluetooth is not available. Enable Bluetooth in Windows Settings and try again.");

        var all = await host.ScanForDevicesAsync(
            duration ?? TimeSpan.FromSeconds(10),
            cancellationToken);

        var matches = new List<IBluetoothDevice>();
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
    public static bool IsLikelyElkBledom(IBluetoothDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return IsLikelyElkBledomName(device.Name);
    }

    /// <summary>
    /// Returns true when <paramref name="advertisedName"/> starts with one of
    /// the known ELK-BLEDOM-family prefixes (case-insensitive). Exposed as a
    /// separate overload so it can be unit-tested without a device object.
    /// </summary>
    public static bool IsLikelyElkBledomName(string? advertisedName)
    {
        if (string.IsNullOrEmpty(advertisedName)) return false;
        foreach (var prefix in KnownPrefixes)
            if (advertisedName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
