using Game.Core;
using Godot;

namespace Game.Client;

public sealed class EnetTransport : INetworkTransport
{
    private ENetMultiplayerPeer? peer;
    private readonly Queue<(bool Connected, int Id)> notifications = [];
    public bool IsHost { get; private set; }
    public int LocalId => peer?.GetUniqueId() ?? 1;
    public event Action<int>? Connected;
    public event Action<int>? Disconnected;
    public event Action<int, byte[]>? Received;
    private void Initialize()
    {
        Dispose(); peer = new ENetMultiplayerPeer();
        // Never dispose a peer from inside one of its native Poll callbacks.
        peer.PeerConnected += id => notifications.Enqueue((true, (int)id));
        peer.PeerDisconnected += id => notifications.Enqueue((false, (int)id));
    }
    public void Host(int port, int maxPlayers)
    {
        Initialize(); IsHost = true;
        var error = peer!.CreateServer(port, maxPlayers - 1, 3);
        if (error != Error.Ok) throw new IOException($"Nie można otworzyć portu UDP {port}: {error}");
    }
    public void Join(string address, int port)
    {
        Initialize(); IsHost = false;
        var error = peer!.CreateClient(address, port, 3);
        if (error != Error.Ok) throw new IOException($"Nie można połączyć: {error}");
    }
    public void Poll()
    {
        if (peer is null) return;
        peer.Poll();
        while (notifications.TryDequeue(out var notification))
        {
            if (notification.Connected) Connected?.Invoke(notification.Id);
            else Disconnected?.Invoke(notification.Id);
        }
        if (peer is null) return;
        int count = Math.Min(peer.GetAvailablePacketCount(), 256);
        for (int i = 0; i < count && peer is not null; i++)
        {
            int sender = peer.GetPacketPeer(); var bytes = peer.GetPacket();
            if (bytes.Length is > 0 and <= PacketCodec.MaxPacketBytes) Received?.Invoke(sender, bytes);
        }
    }
    public void Send(int target, byte[] packet, bool reliable, int channel)
    {
        if (peer is null || peer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Connected) return;
        peer.SetTargetPeer(target); peer.TransferChannel = channel;
        peer.TransferMode = reliable ? MultiplayerPeer.TransferModeEnum.Reliable : MultiplayerPeer.TransferModeEnum.UnreliableOrdered;
        peer.PutPacket(packet);
    }
    public void Disconnect(int id) => peer?.DisconnectPeer(id);
    public void Dispose()
    {
        if (peer is null) return;
        var old = peer; peer = null; old.Close(); old.Dispose(); notifications.Clear();
    }
}
