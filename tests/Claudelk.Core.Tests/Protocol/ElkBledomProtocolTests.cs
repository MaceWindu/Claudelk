// Verifies the exact 9-byte command packets produced by ElkBledomProtocol.
// These bytes are the ground truth of the public protocol API — changing them
// without a firmware reason would silently break every paired strip.

using Claudelk.Core.Protocol;

namespace Claudelk.Core.Tests.Protocol;

[TestFixture]
public sealed class ElkBledomProtocolTests
{
    [Test]
    public void ServiceUuid_MatchesPublicSpec()
    {
        Assert.That(
            ElkBledomProtocol.ServiceUuid,
            Is.EqualTo(Guid.Parse("0000fff0-0000-1000-8000-00805f9b34fb")));
    }

    [Test]
    public void WriteCharacteristicUuid_MatchesPublicSpec()
    {
        Assert.That(
            ElkBledomProtocol.WriteCharacteristicUuid,
            Is.EqualTo(Guid.Parse("0000fff3-0000-1000-8000-00805f9b34fb")));
    }

    [TestCase(true, (byte)0x01)]
    [TestCase(false, (byte)0x00)]
    public void Power_EncodesOnOffFlag(bool on, byte expectedFlag)
    {
        var packet = ElkBledomProtocol.Power(on);

        Assert.That(packet, Is.EqualTo(new byte[]
        {
            0x7e, 0x00, 0x04, expectedFlag, 0x00, 0x00, 0x00, 0x00, 0xef,
        }));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(50)]
    [TestCase(99)]
    [TestCase(100)]
    public void Brightness_EncodesPercentInByte3(int percent)
    {
        var packet = ElkBledomProtocol.Brightness(percent);

        Assert.That(packet, Is.EqualTo(new byte[]
        {
            0x7e, 0x00, 0x01, (byte)percent, 0x00, 0x00, 0x00, 0x00, 0xef,
        }));
    }

    [TestCase(-1)]
    [TestCase(101)]
    [TestCase(int.MinValue)]
    [TestCase(int.MaxValue)]
    public void Brightness_RejectsValuesOutsideZeroToHundred(int percent)
    {
        Assert.That(
            () => ElkBledomProtocol.Brightness(percent),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [TestCase((byte)0x00, (byte)0x00, (byte)0x00)]
    [TestCase((byte)0xff, (byte)0xff, (byte)0xff)]
    [TestCase((byte)0xff, (byte)0x88, (byte)0x00)]
    [TestCase((byte)0x12, (byte)0x34, (byte)0x56)]
    public void Color_EmbedsRgbAtBytes4Through6(byte r, byte g, byte b)
    {
        var packet = ElkBledomProtocol.Color(r, g, b);

        Assert.That(packet, Is.EqualTo(new byte[]
        {
            0x7e, 0x00, 0x05, 0x03, r, g, b, 0x00, 0xef,
        }));
    }

    [TestCase(0)]
    [TestCase(50)]
    [TestCase(100)]
    public void EffectSpeed_EncodesPercentInByte3(int percent)
    {
        var packet = ElkBledomProtocol.EffectSpeed(percent);

        Assert.That(packet, Is.EqualTo(new byte[]
        {
            0x7e, 0x00, 0x02, (byte)percent, 0x00, 0x00, 0x00, 0x00, 0xef,
        }));
    }

    [TestCase(-1)]
    [TestCase(101)]
    public void EffectSpeed_RejectsValuesOutsideZeroToHundred(int percent)
    {
        Assert.That(
            () => ElkBledomProtocol.EffectSpeed(percent),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [TestCase(0x80)]
    [TestCase(0x87)]
    [TestCase(0x9f)]
    public void BuiltInEffect_EncodesCodeAndKeepsSelectorByte(int effectCode)
    {
        var packet = ElkBledomProtocol.BuiltInEffect(effectCode);

        Assert.That(packet, Is.EqualTo(new byte[]
        {
            0x7e, 0x00, 0x03, (byte)effectCode, 0x03, 0x00, 0x00, 0x00, 0xef,
        }));
    }

    [TestCase(0x7f)]
    [TestCase(0xa0)]
    [TestCase(-1)]
    [TestCase(0x100)]
    public void BuiltInEffect_RejectsCodesOutsideAllowedRange(int effectCode)
    {
        Assert.That(
            () => ElkBledomProtocol.BuiltInEffect(effectCode),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    // 0–100 maps linearly onto firmware bytes 128 (warmest) through 138 (coldest).
    [TestCase(0, (byte)128)]
    [TestCase(10, (byte)129)]
    [TestCase(50, (byte)133)]
    [TestCase(100, (byte)138)]
    public void ColorTemperature_MapsZeroToHundredOnto128Through138(int value, byte expectedByte)
    {
        var packet = ElkBledomProtocol.ColorTemperature(value);

        Assert.That(packet, Is.EqualTo(new byte[]
        {
            0x7e, 0x00, 0x03, expectedByte, 0x02, 0x00, 0x00, 0x00, 0xef,
        }));
    }

    [TestCase(-1)]
    [TestCase(101)]
    public void ColorTemperature_RejectsValuesOutsideZeroToHundred(int value)
    {
        Assert.That(
            () => ElkBledomProtocol.ColorTemperature(value),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    // Every command in the protocol is a fixed-length 9-byte packet framed by
    // 0x7e .. 0xef. A regression in framing would brick every command.
    [Test]
    public void AllPackets_AreNineBytesAndProperlyFramed()
    {
        var packets = new[]
        {
            ElkBledomProtocol.Power(on: true),
            ElkBledomProtocol.Power(on: false),
            ElkBledomProtocol.Brightness(50),
            ElkBledomProtocol.Color(1, 2, 3),
            ElkBledomProtocol.EffectSpeed(50),
            ElkBledomProtocol.BuiltInEffect(0x87),
            ElkBledomProtocol.ColorTemperature(50),
        };

        using (Assert.EnterMultipleScope())
        {
            foreach (var packet in packets)
            {
                Assert.That(packet, Has.Length.EqualTo(9));
                Assert.That(packet[0], Is.EqualTo((byte)0x7e));
                Assert.That(packet[^1], Is.EqualTo((byte)0xef));
            }
        }
    }
}
