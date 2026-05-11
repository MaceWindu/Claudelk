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

    public Task PairAsync()
    {
        PairCount++;
        IsPaired = true;
        return Task.CompletedTask;
    }

    public Task ConnectAsync()
    {
        ConnectCount++;
        IsConnected = true;
        return Task.CompletedTask;
    }

    public void Disconnect()
    {
        DisconnectCount++;
        IsConnected = false;
    }

    public Task WriteWithoutResponseAsync(Guid serviceUuid, Guid characteristicUuid, byte[] data)
    {
        Writes.Add(new WriteRecord(serviceUuid, characteristicUuid, [.. data]));
        return Task.CompletedTask;
    }

    public void Dispose() => Disconnect();

    internal sealed record WriteRecord(Guid Service, Guid Characteristic, byte[] Data);
}
