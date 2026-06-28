// Byte-level command builders for ELK-BLEDOM BLE LED strips.
//
// Protocol details derived from these prior works:
//   - b1scoito/elk-led-controller  (MIT) — https://github.com/b1scoito/elk-led-controller
//   - TheSylex's original reverse-engineering notes
//   - arduino12/ble_rgb_led_strip_controller  (GPL-3.0, reference only) —
//     https://github.com/arduino12/ble_rgb_led_strip_controller
//
// See NOTICE.md at the repo root for full attribution.

namespace Claudelk.Core.Protocol;

/// <summary>
/// Pure byte-level command builders for the ELK-BLEDOM 9-byte BLE protocol.
/// Every method returns the raw <c>7e … ef</c> packet ready to be written to
/// the GATT characteristic identified by <see cref="WriteCharacteristicUuid"/>.
/// </summary>
public static class ElkBledomProtocol
{
    // Component constructors (not Guid.Parse) so the UUIDs are built at compile
    // time without runtime string parsing (MA0176). These are the standard
    // 16-bit Bluetooth UUIDs 0xFFF0 / 0xFFF3 in the BLE base-UUID layout
    // (xxxxxxxx-0000-1000-8000-00805f9b34fb).

    /// <summary>GATT service UUID exposed by an ELK-BLEDOM strip (<c>0000fff0-…</c>).</summary>
    public static readonly Guid ServiceUuid =
        new(0x0000fff0, 0x0000, 0x1000, 0x80, 0x00, 0x00, 0x80, 0x5f, 0x9b, 0x34, 0xfb);

    /// <summary>GATT characteristic UUID that accepts command packets (write-without-response, <c>0000fff3-…</c>).</summary>
    public static readonly Guid WriteCharacteristicUuid =
        new(0x0000fff3, 0x0000, 0x1000, 0x80, 0x00, 0x00, 0x80, 0x5f, 0x9b, 0x34, 0xfb);

    private const byte Prefix = 0x7e;
    private const byte Suffix = 0xef;

    /// <summary>Packet that switches the strip on or off.</summary>
    public static byte[] Power(bool on) =>
        new byte[] { Prefix, 0x00, 0x04, (byte)(on ? 0x01 : 0x00), 0x00, 0x00, 0x00, 0x00, Suffix };

    /// <summary>Packet that sets overall brightness in <paramref name="percent"/> (0-100). Honoured only in solid-RGB mode.</summary>
    public static byte[] Brightness(int percent)
    {
        if (percent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(percent), "Brightness must be 0-100.");
        return new byte[] { Prefix, 0x00, 0x01, (byte)percent, 0x00, 0x00, 0x00, 0x00, Suffix };
    }

    /// <summary>Packet that sets a solid RGB colour. Each channel is 0-255.</summary>
    public static byte[] Color(byte r, byte g, byte b) =>
        new byte[] { Prefix, 0x00, 0x05, 0x03, r, g, b, 0x00, Suffix };

    /// <summary>Packet that sets the animation speed of the active built-in effect (0-100).</summary>
    public static byte[] EffectSpeed(int percent)
    {
        if (percent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(percent), "Speed must be 0-100.");
        return new byte[] { Prefix, 0x00, 0x02, (byte)percent, 0x00, 0x00, 0x00, 0x00, Suffix };
    }

    /// <summary>Packet that engages a built-in effect identified by <paramref name="effectCode"/> (0x80–0x9f).</summary>
    public static byte[] BuiltInEffect(int effectCode)
    {
        if (effectCode is < 0x80 or > 0x9f)
            throw new ArgumentOutOfRangeException(nameof(effectCode), "Effect code must be 0x80-0x9f.");
        return new byte[] { Prefix, 0x00, 0x03, (byte)effectCode, 0x03, 0x00, 0x00, 0x00, Suffix };
    }

    /// <summary>Packet that sets warm/cold colour temperature (0 = warmest, 100 = coldest).</summary>
    public static byte[] ColorTemperature(int value)
    {
        if (value is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(value), "Color temperature must be 0-100.");
        // Firmware accepts colour-temperature bytes in the range 128–138 (warmest → coldest);
        // map our 0–100 input linearly onto that span.
        // ToEven preserves the original default-rounding behaviour; specified
        // explicitly to satisfy MA0193 (require a MidpointRounding argument).
        var byteValue = (byte)(128 + (int)Math.Round(value * 10.0 / 100, MidpointRounding.ToEven));
        return new byte[] { Prefix, 0x00, 0x03, byteValue, 0x02, 0x00, 0x00, 0x00, Suffix };
    }
}
