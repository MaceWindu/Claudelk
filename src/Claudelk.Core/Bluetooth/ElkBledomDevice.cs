// BLE connection wrapper around InTheHand.BluetoothLE (32feet.NET) targeted at
// ELK-BLEDOM strips. The Web-Bluetooth-style API surface (GATT) is documented at
// https://github.com/inthehand/32feet.

using Claudelk.Core.Protocol;
using InTheHand.Bluetooth;
using IhBluetooth = InTheHand.Bluetooth.Bluetooth;

namespace Claudelk.Core.Bluetooth;

/// <summary>
/// A connected ELK-BLEDOM strip. Obtain instances via <see cref="ConnectAsync"/>
/// or <see cref="ConnectByIdAsync"/>; dispose to drop the GATT connection.
/// </summary>
public sealed class ElkBledomDevice : IDisposable
{
    private readonly BluetoothDevice _device;
    private GattCharacteristic? _writeCharacteristic;

    private ElkBledomDevice(BluetoothDevice device) => _device = device;

    /// <summary>Opaque BLE device id (corresponds to the strip's MAC address on Windows).</summary>
    public string Id => _device.Id;

    /// <summary>Advertised device name, e.g. <c>ELK-BLEDOM</c>.</summary>
    public string Name => _device.Name ?? string.Empty;

    /// <summary>True while the GATT connection is open.</summary>
    public bool IsConnected => _device.Gatt.IsConnected;

    /// <summary>Connects to an already-discovered <paramref name="device"/> and resolves the write characteristic.</summary>
    public static async Task<ElkBledomDevice> ConnectAsync(BluetoothDevice device)
    {
        var wrapper = new ElkBledomDevice(device);
        await wrapper.EnsureConnectedAsync();
        return wrapper;
    }

    /// <summary>
    /// Connects to the strip with the given <paramref name="id"/>. Tries the
    /// Windows paired-devices list first (no advertisement scan) and falls back
    /// to a brief scan if the device is not yet paired.
    /// </summary>
    /// <param name="id">BLE device id from <see cref="Id"/> / <see cref="ElkBledomScanner.ScanAsync"/>.</param>
    /// <param name="scanTimeout">How long to scan for as a fallback. Defaults to 3 seconds.</param>
    /// <exception cref="InvalidOperationException">No matching device was found.</exception>
    public static async Task<ElkBledomDevice> ConnectByIdAsync(string id, TimeSpan? scanTimeout = null)
    {
        // Fast path: device already paired in Windows → no advertisement scan needed.
        var paired = await IhBluetooth.GetPairedDevicesAsync();
        var match = paired.FirstOrDefault(d =>
            string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            // Slow path: scan briefly in case the strip isn't paired yet.
            var devices = await ElkBledomScanner.ScanAsync(scanTimeout ?? TimeSpan.FromSeconds(3));
            match = devices.FirstOrDefault(d =>
                string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        if (match is null)
            throw new InvalidOperationException(
                $"No ELK-BLEDOM device with id '{id}' found. " +
                "Pair it once in Windows Bluetooth settings for fast reconnects.");

        return await ConnectAsync(match);
    }

    private async Task EnsureConnectedAsync()
    {
        if (!_device.Gatt.IsConnected)
            await _device.Gatt.ConnectAsync();

        var service = await _device.Gatt.GetPrimaryServiceAsync(ElkBledomProtocol.ServiceUuid)
            ?? throw new InvalidOperationException(
                $"ELK-BLEDOM service {ElkBledomProtocol.ServiceUuid} not found on device.");

        _writeCharacteristic = await service.GetCharacteristicAsync(ElkBledomProtocol.WriteCharacteristicUuid)
            ?? throw new InvalidOperationException(
                $"Write characteristic {ElkBledomProtocol.WriteCharacteristicUuid} not found.");
    }

    /// <summary>
    /// Records the strip in Windows' paired-devices list so future
    /// <see cref="ConnectByIdAsync"/> calls hit the fast path.
    /// No-op if the device is already paired.
    /// </summary>
    public async Task PairWithWindowsAsync()
    {
        if (!_device.IsPaired)
            await _device.PairAsync();
    }

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
        // Keep the strip powered on the whole time and just toggle the colour
        // between bright and black. SetColor after a Power(off) is silently
        // dropped on this firmware, which broke the previous implementation.
        await TurnOnAsync();
        for (var i = 0; i < pulses; i++)
        {
            await SetColorAsync(r, g, b);
            await Task.Delay(pulseMs, ct);
            await SetColorAsync(0, 0, 0);
            await Task.Delay(pulseMs, ct);
        }

        var (er, eg, eb) = endColor ?? (r, g, b);
        await SetColorAsync(er, eg, eb);
    }

    /// <summary>Powers the strip on.</summary>
    public Task TurnOnAsync() => WriteAsync(ElkBledomProtocol.Power(on: true));

    /// <summary>Powers the strip off.</summary>
    public Task TurnOffAsync() => WriteAsync(ElkBledomProtocol.Power(on: false));

    /// <summary>Sets a solid RGB colour (channels 0-255).</summary>
    public Task SetColorAsync(byte r, byte g, byte b) => WriteAsync(ElkBledomProtocol.Color(r, g, b));

    /// <summary>Sets overall brightness in percent (0-100). Honoured only in solid-RGB mode.</summary>
    public Task SetBrightnessAsync(int percent) => WriteAsync(ElkBledomProtocol.Brightness(percent));

    /// <summary>Sets the animation speed of the active built-in effect (0-100).</summary>
    public Task SetEffectSpeedAsync(int percent) => WriteAsync(ElkBledomProtocol.EffectSpeed(percent));

    /// <summary>Engages a built-in animation effect by code (0x80–0x9f).</summary>
    public Task SetEffectAsync(int effectCode) => WriteAsync(ElkBledomProtocol.BuiltInEffect(effectCode));

    /// <summary>Sets warm/cold colour temperature (0 = warmest, 100 = coldest).</summary>
    public Task SetColorTemperatureAsync(int value) => WriteAsync(ElkBledomProtocol.ColorTemperature(value));

    private Task WriteAsync(byte[] payload)
    {
        if (_writeCharacteristic is null)
            throw new InvalidOperationException("Device is not connected.");
        return _writeCharacteristic.WriteValueWithoutResponseAsync(payload);
    }

    /// <summary>Disconnects from the strip if currently connected.</summary>
    public void Dispose()
    {
        if (_device.Gatt.IsConnected)
            _device.Gatt.Disconnect();
    }
}
