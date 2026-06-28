// In-memory IBluetoothDevice for unit tests. Records every WriteWithoutResponseAsync
// call so we can assert the byte stream a command produces, and tracks
// connect/disconnect/pair lifecycle to verify ElkBledomDevice behaviour.

using Claudelk.Core.Bluetooth;

namespace Claudelk.Core.Tests.Bluetooth.Fakes;

internal sealed class FakeBluetoothDevice : IBluetoothDevice
{
    public FakeBluetoothDevice(string id, string name, bool isPaired = false)
    {
        Id = id;
        Name = name;
        IsPaired = isPaired;
    }

    public string Id { get; }
    public string Name { get; }
    public bool IsPaired { get; private set; }
    public bool IsConnected { get; private set; }

    public int ConnectCount { get; private set; }
    public int DisconnectCount { get; private set; }
    public int PairCount { get; private set; }
    public List<WriteRecord> Writes { get; } = [];

    /// <summary>
    /// When true, every async operation blocks forever (honouring the token),
    /// simulating a wedged Bluetooth adapter so cancellation paths can be tested.
    /// </summary>
    public bool Hang { get; set; }

    public async Task PairAsync(CancellationToken cancellationToken = default)
    {
        if (Hang) await Task.Delay(Timeout.Infinite, cancellationToken);
        PairCount++;
        IsPaired = true;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (Hang) await Task.Delay(Timeout.Infinite, cancellationToken);
        ConnectCount++;
        IsConnected = true;
    }

    public void Disconnect()
    {
        DisconnectCount++;
        IsConnected = false;
    }

    public async Task WriteWithoutResponseAsync(Guid serviceUuid, Guid characteristicUuid, byte[] data, CancellationToken cancellationToken = default)
    {
        if (Hang) await Task.Delay(Timeout.Infinite, cancellationToken);
        Writes.Add(new WriteRecord(serviceUuid, characteristicUuid, [.. data]));
    }

    public void Dispose() => Disconnect();

    internal sealed record WriteRecord(Guid Service, Guid Characteristic, byte[] Data);
}
