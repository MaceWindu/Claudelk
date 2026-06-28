// In-memory IBluetoothHost. Lets tests stub the paired list and the
// scan-advertisement list independently, simulate "Bluetooth disabled", and
// observe how often ScanForDevicesAsync is invoked.

using Claudelk.Core.Bluetooth;

namespace Claudelk.Core.Tests.Bluetooth.Fakes;

internal sealed class FakeBluetoothHost : IBluetoothHost
{
    public bool Available { get; set; } = true;
    public List<IBluetoothDevice> Paired { get; } = [];
    public List<IBluetoothDevice> Advertised { get; } = [];

    public int ScanCount { get; private set; }
    public int PairedQueryCount { get; private set; }
    public TimeSpan? LastScanTimeout { get; private set; }

    /// <summary>
    /// When true, every async operation blocks forever (honouring the token),
    /// simulating a wedged Bluetooth adapter so cancellation paths can be tested.
    /// </summary>
    public bool Hang { get; set; }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (Hang) await Task.Delay(Timeout.Infinite, cancellationToken);
        return Available;
    }

    public async Task<IReadOnlyList<IBluetoothDevice>> GetPairedDevicesAsync(CancellationToken cancellationToken = default)
    {
        if (Hang) await Task.Delay(Timeout.Infinite, cancellationToken);
        PairedQueryCount++;
        return [.. Paired];
    }

    public async Task<IReadOnlyList<IBluetoothDevice>> ScanForDevicesAsync(
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (Hang) await Task.Delay(Timeout.Infinite, cancellationToken);
        ScanCount++;
        LastScanTimeout = timeout;
        return [.. Advertised];
    }
}
