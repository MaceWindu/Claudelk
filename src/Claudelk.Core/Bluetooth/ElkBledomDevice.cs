// Protocol-level wrapper around a connected BLE strip. Talks to the radio
// through IBluetoothDevice / IBluetoothHost, so commands can be unit-tested
// with a fake that records every WriteWithoutResponseAsync call.

using Claudelk.Core.Bluetooth.InTheHand;
using Claudelk.Core.Protocol;

namespace Claudelk.Core.Bluetooth;

/// <summary>
/// A connected ELK-BLEDOM strip. Obtain instances via <see cref="ConnectAsync"/>
/// or <see cref="ConnectByIdAsync"/>; dispose to drop the GATT connection.
/// </summary>
public sealed class ElkBledomDevice : IDisposable
{
    private readonly IBluetoothDevice _device;

    private ElkBledomDevice(IBluetoothDevice device) => _device = device;

    /// <summary>Opaque BLE device id (corresponds to the strip's MAC address on Windows).</summary>
    public string Id => _device.Id;

    /// <summary>Advertised device name, e.g. <c>ELK-BLEDOM</c>.</summary>
    public string Name => _device.Name;

    /// <summary>True while the GATT connection is open.</summary>
    public bool IsConnected => _device.IsConnected;

    /// <summary>Connects to an already-discovered <paramref name="device"/>.</summary>
    public static async Task<ElkBledomDevice> ConnectAsync(IBluetoothDevice device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        var wrapper = new ElkBledomDevice(device);
        await device.ConnectAsync(cancellationToken);
        return wrapper;
    }

    /// <summary>
    /// Connects to the strip with the given <paramref name="id"/>. Tries the
    /// host's paired-devices list first (no advertisement scan) and falls back
    /// to a brief scan if the device is not yet paired.
    /// </summary>
    /// <param name="id">BLE device id from <see cref="Id"/> / <see cref="ElkBledomScanner.ScanAsync"/>.</param>
    /// <param name="scanTimeout">How long to scan for as a fallback. Defaults to 3 seconds.</param>
    /// <param name="host">Optional BLE host. Defaults to <see cref="InTheHandBluetoothHost"/>.</param>
    /// <param name="cancellationToken">Cancels the paired-list query, fallback scan, and connect.</param>
    /// <exception cref="InvalidOperationException">No matching device was found.</exception>
    public static async Task<ElkBledomDevice> ConnectByIdAsync(
        string id,
        TimeSpan? scanTimeout = null,
        IBluetoothHost? host = null,
        CancellationToken cancellationToken = default)
    {
        host ??= new InTheHandBluetoothHost();

        // Fast path: device already paired in Windows → no advertisement scan needed.
        var paired = await host.GetPairedDevicesAsync(cancellationToken);
        var match = paired.FirstOrDefault(d =>
            string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            // Slow path: scan briefly in case the strip isn't paired yet.
            var devices = await ElkBledomScanner.ScanAsync(
                duration: scanTimeout ?? TimeSpan.FromSeconds(3),
                host: host,
                cancellationToken: cancellationToken);
            match = devices.FirstOrDefault(d =>
                string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        if (match is null)
            throw new InvalidOperationException(
                $"No ELK-BLEDOM device with id '{id}' found. " +
                "Pair it once in Windows Bluetooth settings for fast reconnects.");

        return await ConnectAsync(match, cancellationToken);
    }

    /// <summary>
    /// Records the strip in Windows' paired-devices list so future
    /// <see cref="ConnectByIdAsync"/> calls hit the fast path.
    /// No-op if the device is already paired.
    /// </summary>
    public Task PairWithWindowsAsync(CancellationToken cancellationToken = default) => _device.PairAsync(cancellationToken);

    /// <summary>
    /// Pulses a colour on/off for <paramref name="pulses"/> cycles, then holds
    /// either the same colour or <paramref name="endColor"/> if given.
    /// </summary>
    /// <remarks>
    /// Alternates between the target colour and <c>(0,0,0)</c> via SetColor
    /// writes; never power-cycles the strip. On this firmware a SetColor that
    /// follows a Power(off) is silently dropped, so toggling power would break
    /// the blink.
    /// </remarks>
    /// <param name="r">Red channel.</param>
    /// <param name="g">Green channel.</param>
    /// <param name="b">Blue channel.</param>
    /// <param name="pulses">How many on/off cycles to perform.</param>
    /// <param name="pulseMs">Milliseconds the strip stays on (and off) per cycle.</param>
    /// <param name="endColor">Optional colour to hold after the final pulse. Defaults to the pulse colour.</param>
    /// <param name="ct">Cancels the loop.</param>
    public async Task BlinkAsync(
        byte r, byte g, byte b,
        int pulses, int pulseMs,
        (byte r, byte g, byte b)? endColor = null,
        CancellationToken ct = default)
    {
        await TurnOnAsync(ct);
        for (var i = 0; i < pulses; i++)
        {
            await SetColorAsync(r, g, b, ct);
            await Task.Delay(pulseMs, ct);
            await SetColorAsync(0, 0, 0, ct);
            await Task.Delay(pulseMs, ct);
        }

        var (er, eg, eb) = endColor ?? (r, g, b);
        await SetColorAsync(er, eg, eb, ct);
    }

    /// <summary>Powers the strip on.</summary>
    public Task TurnOnAsync(CancellationToken cancellationToken = default) => WriteAsync(ElkBledomProtocol.Power(on: true), cancellationToken);

    /// <summary>Powers the strip off.</summary>
    public Task TurnOffAsync(CancellationToken cancellationToken = default) => WriteAsync(ElkBledomProtocol.Power(on: false), cancellationToken);

    /// <summary>Sets a solid RGB colour (channels 0-255).</summary>
    public Task SetColorAsync(byte r, byte g, byte b, CancellationToken cancellationToken = default) => WriteAsync(ElkBledomProtocol.Color(r, g, b), cancellationToken);

    /// <summary>Sets overall brightness in percent (0-100). Honoured only in solid-RGB mode.</summary>
    public Task SetBrightnessAsync(int percent, CancellationToken cancellationToken = default) => WriteAsync(ElkBledomProtocol.Brightness(percent), cancellationToken);

    /// <summary>Sets the animation speed of the active built-in effect (0-100).</summary>
    public Task SetEffectSpeedAsync(int percent, CancellationToken cancellationToken = default) => WriteAsync(ElkBledomProtocol.EffectSpeed(percent), cancellationToken);

    /// <summary>Engages a built-in animation effect by code (0x80–0x9f).</summary>
    public Task SetEffectAsync(int effectCode, CancellationToken cancellationToken = default) => WriteAsync(ElkBledomProtocol.BuiltInEffect(effectCode), cancellationToken);

    /// <summary>Sets warm/cold colour temperature (0 = warmest, 100 = coldest).</summary>
    public Task SetColorTemperatureAsync(int value, CancellationToken cancellationToken = default) => WriteAsync(ElkBledomProtocol.ColorTemperature(value), cancellationToken);

    private Task WriteAsync(byte[] payload, CancellationToken cancellationToken) =>
        _device.WriteWithoutResponseAsync(
            ElkBledomProtocol.ServiceUuid,
            ElkBledomProtocol.WriteCharacteristicUuid,
            payload,
            cancellationToken);

    /// <summary>Disconnects from the strip if currently connected.</summary>
    public void Dispose() => _device.Dispose();
}
