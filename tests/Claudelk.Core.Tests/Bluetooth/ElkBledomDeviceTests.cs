// Drives ElkBledomDevice through a FakeBluetoothDevice / FakeBluetoothHost so
// we can assert the exact byte streams each command produces and the
// resolution order ConnectByIdAsync uses (paired list first, scan fallback).

using Claudelk.Core.Bluetooth;
using Claudelk.Core.Protocol;
using Claudelk.Core.Tests.Bluetooth.Fakes;

namespace Claudelk.Core.Tests.Bluetooth;

[TestFixture]
public sealed class ElkBledomDeviceTests
{
    [Test]
    public async Task ConnectAsync_OpensTheGattConnection()
    {
        var fake = new FakeBluetoothDevice("be:ff:f0:01:04:a8", "ELK-BLEDOM");

        using var device = await ElkBledomDevice.ConnectAsync(fake);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fake.ConnectCount, Is.EqualTo(1));
            Assert.That(device.IsConnected, Is.True);
            Assert.That(device.Id, Is.EqualTo("be:ff:f0:01:04:a8"));
            Assert.That(device.Name, Is.EqualTo("ELK-BLEDOM"));
        }
    }

    [Test]
    public async Task Dispose_DisconnectsTheUnderlyingDevice()
    {
        var fake = new FakeBluetoothDevice("id", "ELK-BLEDOM");

        var device = await ElkBledomDevice.ConnectAsync(fake);
        device.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fake.DisconnectCount, Is.EqualTo(1));
            Assert.That(fake.IsConnected, Is.False);
        }
    }

    [Test]
    public async Task PairWithWindowsAsync_DelegatesToUnderlyingDevice()
    {
        var fake = new FakeBluetoothDevice("id", "ELK-BLEDOM");
        using var device = await ElkBledomDevice.ConnectAsync(fake);

        await device.PairWithWindowsAsync();

        Assert.That(fake.PairCount, Is.EqualTo(1));
    }

    [Test]
    public async Task TurnOnAsync_WritesPowerOnPacketToTheElkBledomCharacteristic()
    {
        var fake = new FakeBluetoothDevice("id", "ELK-BLEDOM");
        using var device = await ElkBledomDevice.ConnectAsync(fake);

        await device.TurnOnAsync();

        AssertSingleWrite(fake, ElkBledomProtocol.Power(on: true));
    }

    [Test]
    public async Task TurnOffAsync_WritesPowerOffPacket()
    {
        var fake = new FakeBluetoothDevice("id", "ELK-BLEDOM");
        using var device = await ElkBledomDevice.ConnectAsync(fake);

        await device.TurnOffAsync();

        AssertSingleWrite(fake, ElkBledomProtocol.Power(on: false));
    }

    [Test]
    public async Task SetColorAsync_WritesExpectedRgbPacket()
    {
        var fake = new FakeBluetoothDevice("id", "ELK-BLEDOM");
        using var device = await ElkBledomDevice.ConnectAsync(fake);

        await device.SetColorAsync(0xff, 0x88, 0x00);

        AssertSingleWrite(fake, ElkBledomProtocol.Color(0xff, 0x88, 0x00));
    }

    [Test]
    public async Task SetBrightnessAsync_WritesExpectedBrightnessPacket()
    {
        var fake = new FakeBluetoothDevice("id", "ELK-BLEDOM");
        using var device = await ElkBledomDevice.ConnectAsync(fake);

        await device.SetBrightnessAsync(75);

        AssertSingleWrite(fake, ElkBledomProtocol.Brightness(75));
    }

    [Test]
    public async Task SetEffectSpeedAsync_WritesExpectedSpeedPacket()
    {
        var fake = new FakeBluetoothDevice("id", "ELK-BLEDOM");
        using var device = await ElkBledomDevice.ConnectAsync(fake);

        await device.SetEffectSpeedAsync(40);

        AssertSingleWrite(fake, ElkBledomProtocol.EffectSpeed(40));
    }

    [Test]
    public async Task SetEffectAsync_WritesExpectedBuiltInEffectPacket()
    {
        var fake = new FakeBluetoothDevice("id", "ELK-BLEDOM");
        using var device = await ElkBledomDevice.ConnectAsync(fake);

        await device.SetEffectAsync(0x87);

        AssertSingleWrite(fake, ElkBledomProtocol.BuiltInEffect(0x87));
    }

    [Test]
    public async Task SetColorTemperatureAsync_WritesExpectedTempPacket()
    {
        var fake = new FakeBluetoothDevice("id", "ELK-BLEDOM");
        using var device = await ElkBledomDevice.ConnectAsync(fake);

        await device.SetColorTemperatureAsync(50);

        AssertSingleWrite(fake, ElkBledomProtocol.ColorTemperature(50));
    }

    [Test]
    public async Task BlinkAsync_WritesPowerOnThenAlternatingColorPackets()
    {
        var fake = new FakeBluetoothDevice("id", "ELK-BLEDOM");
        using var device = await ElkBledomDevice.ConnectAsync(fake);

        // pulseMs = 0 keeps the test fast; behaviour we care about is the byte
        // sequence, not the timing.
        await device.BlinkAsync(0xff, 0x00, 0x00, pulses: 2, pulseMs: 0);

        var expected = new[]
        {
            ElkBledomProtocol.Power(on: true),
            ElkBledomProtocol.Color(0xff, 0x00, 0x00),
            ElkBledomProtocol.Color(0, 0, 0),
            ElkBledomProtocol.Color(0xff, 0x00, 0x00),
            ElkBledomProtocol.Color(0, 0, 0),
            ElkBledomProtocol.Color(0xff, 0x00, 0x00),    // final hold (defaults to pulse colour)
        };
        AssertWriteSequence(fake, expected);
    }

    [Test]
    public async Task BlinkAsync_HoldsEndColorWhenProvided()
    {
        var fake = new FakeBluetoothDevice("id", "ELK-BLEDOM");
        using var device = await ElkBledomDevice.ConnectAsync(fake);

        await device.BlinkAsync(
            0xff, 0x00, 0x00,
            pulses: 1, pulseMs: 0,
            endColor: (0x00, 0xff, 0x00));

        // Last write should be the explicit end colour, not the pulse colour.
        Assert.That(fake.Writes[^1].Data, Is.EqualTo(ElkBledomProtocol.Color(0x00, 0xff, 0x00)));
    }

    [Test]
    public async Task ConnectByIdAsync_FindsDeviceInPairedListWithoutScanning()
    {
        var host = new FakeBluetoothHost();
        host.Paired.Add(new FakeBluetoothDevice("be:ff:f0:01:04:a8", "ELK-BLEDOM", isPaired: true));

        using var device = await ElkBledomDevice.ConnectByIdAsync(
            "be:ff:f0:01:04:a8", host: host);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(device.Id, Is.EqualTo("be:ff:f0:01:04:a8"));
            Assert.That(host.PairedQueryCount, Is.EqualTo(1));
            Assert.That(host.ScanCount, Is.Zero, "scan should be skipped when device is already paired");
        }
    }

    [Test]
    public async Task ConnectByIdAsync_FallsBackToScanWhenNotInPairedList()
    {
        var host = new FakeBluetoothHost();
        host.Advertised.Add(new FakeBluetoothDevice("be:ff:f0:01:04:a8", "ELK-BLEDOM"));

        using var device = await ElkBledomDevice.ConnectByIdAsync(
            "be:ff:f0:01:04:a8", host: host);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(device.Id, Is.EqualTo("be:ff:f0:01:04:a8"));
            Assert.That(host.PairedQueryCount, Is.EqualTo(1));
            Assert.That(host.ScanCount, Is.EqualTo(1));
            Assert.That(host.LastScanTimeout, Is.EqualTo(TimeSpan.FromSeconds(3)),
                "default fallback scan should be 3 seconds");
        }
    }

    [Test]
    public async Task ConnectByIdAsync_ForwardsCustomScanTimeoutToHost()
    {
        var host = new FakeBluetoothHost();
        host.Advertised.Add(new FakeBluetoothDevice("id", "ELK-BLEDOM"));

        using var _ = await ElkBledomDevice.ConnectByIdAsync(
            "id", scanTimeout: TimeSpan.FromSeconds(7), host: host);

        Assert.That(host.LastScanTimeout, Is.EqualTo(TimeSpan.FromSeconds(7)));
    }

    [Test]
    public void ConnectByIdAsync_ThrowsWhenIdNotFoundAnywhere()
    {
        var host = new FakeBluetoothHost();

        Assert.That(
            async () => await ElkBledomDevice.ConnectByIdAsync("missing-id", host: host),
            Throws.InvalidOperationException.With.Message.Contains("missing-id"));
    }

    [Test]
    public async Task ConnectByIdAsync_MatchesIdCaseInsensitively()
    {
        var host = new FakeBluetoothHost();
        host.Paired.Add(new FakeBluetoothDevice("BE:FF:F0:01:04:A8", "ELK-BLEDOM", isPaired: true));

        using var device = await ElkBledomDevice.ConnectByIdAsync(
            "be:ff:f0:01:04:a8", host: host);

        Assert.That(device.Id, Is.EqualTo("BE:FF:F0:01:04:A8"));
    }

    private static void AssertSingleWrite(FakeBluetoothDevice fake, byte[] expectedPayload)
    {
        Assert.That(fake.Writes, Has.Count.EqualTo(1));
        var record = fake.Writes[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(record.Service, Is.EqualTo(ElkBledomProtocol.ServiceUuid));
            Assert.That(record.Characteristic, Is.EqualTo(ElkBledomProtocol.WriteCharacteristicUuid));
            Assert.That(record.Data, Is.EqualTo(expectedPayload));
        }
    }

    private static void AssertWriteSequence(FakeBluetoothDevice fake, byte[][] expectedPayloads)
    {
        Assert.That(fake.Writes, Has.Count.EqualTo(expectedPayloads.Length));

        using (Assert.EnterMultipleScope())
        {
            for (var i = 0; i < expectedPayloads.Length; i++)
            {
                Assert.That(fake.Writes[i].Service, Is.EqualTo(ElkBledomProtocol.ServiceUuid));
                Assert.That(fake.Writes[i].Characteristic, Is.EqualTo(ElkBledomProtocol.WriteCharacteristicUuid));
                Assert.That(fake.Writes[i].Data, Is.EqualTo(expectedPayloads[i]),
                    $"write #{i} payload mismatch");
            }
        }
    }
}
