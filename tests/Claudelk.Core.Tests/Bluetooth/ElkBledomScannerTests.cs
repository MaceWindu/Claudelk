// Exercises the scanner's name-prefix matching, its onSeen + filter behaviour,
// and its propagation of the "Bluetooth disabled" error from the host.

using Claudelk.Core.Bluetooth;
using Claudelk.Core.Tests.Bluetooth.Fakes;

namespace Claudelk.Core.Tests.Bluetooth;

[TestFixture]
public sealed class ElkBledomScannerTests
{
    [TestCase("ELK-BLEDOM")]
    [TestCase("ELK-BLEDOM-XYZ")]
    [TestCase("elk-bledom")]                       // case-insensitive
    [TestCase("ELK-BLE")]
    [TestCase("ELK-BULB-42")]
    [TestCase("ELK-LAMPL-Bedroom")]
    [TestCase("LEDBLE-something")]
    [TestCase("MELK-Strip")]
    public void IsLikelyElkBledomName_AcceptsKnownPrefixes(string name)
    {
        Assert.That(ElkBledomScanner.IsLikelyElkBledomName(name), Is.True);
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase("Apple AirPods")]
    [TestCase("ELK")]                              // prefix shorter than any known
    [TestCase("BLEDOM")]                           // missing ELK- prefix
    [TestCase("MyELK-BLEDOM")]                     // prefix not at start
    public void IsLikelyElkBledomName_RejectsEverythingElse(string? name)
    {
        Assert.That(ElkBledomScanner.IsLikelyElkBledomName(name), Is.False);
    }

    [Test]
    public void IsLikelyElkBledom_DelegatesToNameOverload()
    {
        var matching = new FakeBluetoothDevice("id-1", "ELK-BLEDOM");
        var nonMatching = new FakeBluetoothDevice("id-2", "Random Device");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ElkBledomScanner.IsLikelyElkBledom(matching), Is.True);
            Assert.That(ElkBledomScanner.IsLikelyElkBledom(nonMatching), Is.False);
        }
    }

    [Test]
    public async Task ScanAsync_FiltersAdvertisedDevicesByKnownPrefix()
    {
        var host = new FakeBluetoothHost();
        host.Advertised.Add(new FakeBluetoothDevice("id-1", "ELK-BLEDOM"));
        host.Advertised.Add(new FakeBluetoothDevice("id-2", "Apple AirPods"));
        host.Advertised.Add(new FakeBluetoothDevice("id-3", "MELK-Strip"));
        host.Advertised.Add(new FakeBluetoothDevice("id-4", ""));

        var matches = await ElkBledomScanner.ScanAsync(host: host);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(matches, Has.Count.EqualTo(2));
            Assert.That(matches.Select(d => d.Id), Is.EquivalentTo(new[] { "id-1", "id-3" }));
        }
    }

    [Test]
    public async Task ScanAsync_InvokesOnSeenForEveryAdvertisedDevice()
    {
        var host = new FakeBluetoothHost();
        host.Advertised.Add(new FakeBluetoothDevice("id-1", "ELK-BLEDOM"));
        host.Advertised.Add(new FakeBluetoothDevice("id-2", "Random"));

        var seen = new List<string>();

        _ = await ElkBledomScanner.ScanAsync(
            onSeen: d => seen.Add(d.Id),
            host: host);

        Assert.That(seen, Is.EquivalentTo(new[] { "id-1", "id-2" }));
    }

    [Test]
    public async Task ScanAsync_DefaultsToTenSecondTimeout()
    {
        var host = new FakeBluetoothHost();

        _ = await ElkBledomScanner.ScanAsync(host: host);

        Assert.That(host.LastScanTimeout, Is.EqualTo(TimeSpan.FromSeconds(10)));
    }

    [Test]
    public async Task ScanAsync_ForwardsCustomDurationToHost()
    {
        var host = new FakeBluetoothHost();

        _ = await ElkBledomScanner.ScanAsync(duration: TimeSpan.FromSeconds(2), host: host);

        Assert.That(host.LastScanTimeout, Is.EqualTo(TimeSpan.FromSeconds(2)));
    }

    [Test]
    public void ScanAsync_ThrowsWhenBluetoothIsUnavailable()
    {
        var host = new FakeBluetoothHost { Available = false };

        Assert.That(
            async () => await ElkBledomScanner.ScanAsync(host: host),
            Throws.InvalidOperationException);
    }

    [Test]
    public void ScanAsync_CancelsWhenHostHangs()
    {
        var host = new FakeBluetoothHost { Hang = true };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // A wedged adapter blocks the availability check forever; the token must
        // surface as a cancellation rather than hanging the scan.
        Assert.That(
            async () => await ElkBledomScanner.ScanAsync(host: host, cancellationToken: cts.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }
}
