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

    public Task<bool> IsAvailableAsync() => Task.FromResult(Available);

    public Task<IReadOnlyList<IBluetoothDevice>> GetPairedDevicesAsync()
    {
        PairedQueryCount++;
        return Task.FromResult<IReadOnlyList<IBluetoothDevice>>([.. Paired]);
    }

    public Task<IReadOnlyList<IBluetoothDevice>> ScanForDevicesAsync(
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ScanCount++;
        LastScanTimeout = timeout;
        return Task.FromResult<IReadOnlyList<IBluetoothDevice>>([.. Advertised]);
    }
}
