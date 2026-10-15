using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Game.Core;

public enum PacketKind : byte { Control = 1, Input = 2, Snapshot = 3, Event = 4 }

public readonly record struct PlayerInputCommand(uint Sequence, uint TickNumber, float Steering,
    float Throttle, float Brake, float AimYaw, bool Handbrake, bool Fire, bool Mine)
{
    public bool IsValid => Sequence > 0 && float.IsFinite(Steering) && float.IsFinite(Throttle) &&
        float.IsFinite(Brake) && float.IsFinite(AimYaw) && Math.Abs(Steering) <= 1 &&
        Math.Abs(Throttle) <= 1 && Brake is >= 0 and <= 1 && Math.Abs(AimYaw) <= MathF.PI;
    public PlayerInputCommand Neutral => this with { Steering = 0, Throttle = 0, Brake = 1, Fire = false, Mine = false };
}

public sealed class InputGate
{
    public uint LastSequence { get; private set; }
    public PlayerInputCommand Current { get; private set; }
    private double lastReceived = double.NegativeInfinity;
    public bool Accept(PlayerInputCommand command, double now)
    {
        if (!command.IsValid || command.Sequence <= LastSequence || command.Sequence - LastSequence > 3600) return false;
        LastSequence = command.Sequence; Current = command; lastReceived = now; return true;
    }
    public PlayerInputCommand Read(double now) => now - lastReceived <= .15 ? Current : Current.Neutral;
}

public sealed record VehicleState(int Id, Vector3 Position, Quaternion Rotation, Vector3 Velocity,
    Vector3 AngularVelocity, float TurretYaw, float Health, float Energy, float Heat,
    int Ammo, int Mines, int Lap, int NextCheckpoint, int PassedGates, float Respawn,
    float Invulnerability, uint AcknowledgedSequence, double FinishTime, float SlowRemaining = 0, float SlowFactor = 1);

public sealed record WorldSnapshot(uint RaceId, uint Tick, RacePhase Phase, double Elapsed, float Remaining, VehicleState[] Vehicles);
public sealed record RaceEvent(uint RaceId, long EventId, string Kind, int Actor, int Target, Vector3 Position, Vector3 End, float Value, WeaponDefinition? Weapon = null);
public sealed record LobbyPlayer(int Id, string Name, int Color, bool Ready, bool Bot, PlayerLoadoutDto Loadout);
public sealed record SessionMessage(string Kind, int Protocol = 2, string ContentHash = "",
    string Name = "", int Color = 0, PlayerLoadoutDto? Loadout = null,
    LobbyPlayer[]? Players = null, string Message = "", int[]? Ranking = null, uint RaceId = 0, double SentTime = 0,
    LobbyBalanceRules? BalanceRules = null, GameMode Mode = GameMode.Exhibition,
    CampaignView? Campaign = null, Guid SessionId = default, RewardReceipt? Reward = null, string ItemId = "", string CosmeticId = "stock");

public interface INetworkTransport : IDisposable
{
    event Action<int>? Connected;
    event Action<int>? Disconnected;
    event Action<int, byte[]>? Received;
    bool IsHost { get; }
    int LocalId { get; }
    void Host(int port, int maxPlayers);
    void Join(string address, int port);
    void Poll();
    void Send(int target, byte[] packet, bool reliable, int channel);
    void Disconnect(int peer);
}

public static class PacketCodec
{
    public const int MaxPacketBytes = 32768;
    public static byte[] EncodeInputs(IReadOnlyList<PlayerInputCommand> commands)
    {
        if (commands.Count is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(commands));
        using var stream = new MemoryStream(96); using var w = new BinaryWriter(stream);
        w.Write((byte)PacketKind.Input); w.Write((byte)commands.Count);
        foreach (var c in commands)
        {
            w.Write(c.Sequence); w.Write(c.TickNumber); w.Write(c.Steering); w.Write(c.Throttle);
            w.Write(c.Brake); w.Write(c.AimYaw);
            w.Write((byte)((c.Handbrake ? 1 : 0) | (c.Fire ? 2 : 0) | (c.Mine ? 4 : 0)));
        }
        return stream.ToArray();
    }
    public static PlayerInputCommand[] DecodeInputs(byte[] bytes)
    {
        using var r = Reader(bytes, PacketKind.Input);
        int count = r.ReadByte();
        if (count is < 1 or > 3 || bytes.Length != 2 + count * 25) throw new InvalidDataException("Input size.");
        var result = new PlayerInputCommand[count];
        for (var i = 0; i < count; i++)
        {
            uint sequence = r.ReadUInt32(), tick = r.ReadUInt32();
            float steering = r.ReadSingle(), throttle = r.ReadSingle(), brake = r.ReadSingle(), aim = r.ReadSingle();
            byte flags = r.ReadByte();
            var c = new PlayerInputCommand(sequence, tick, steering, throttle, brake, aim, (flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0);
            if (flags > 7 || !c.IsValid) throw new InvalidDataException("Input values.");
            result[i] = c;
        }
        return result;
    }
    public static byte[] EncodeSnapshot(WorldSnapshot s)
    {
        if (s.Vehicles.Length > 8) throw new InvalidDataException("Too many vehicles.");
        using var stream = new MemoryStream(1200); using var w = new BinaryWriter(stream);
        w.Write((byte)PacketKind.Snapshot); w.Write(s.RaceId); w.Write(s.Tick); w.Write((byte)s.Phase); w.Write(s.Elapsed); w.Write(s.Remaining); w.Write((byte)s.Vehicles.Length);
        foreach (var v in s.Vehicles)
        {
            w.Write(v.Id); Write(w, v.Position); w.Write(v.Rotation.X); w.Write(v.Rotation.Y); w.Write(v.Rotation.Z); w.Write(v.Rotation.W);
            Write(w, v.Velocity); Write(w, v.AngularVelocity); w.Write(v.TurretYaw); w.Write(v.Health); w.Write(v.Energy); w.Write(v.Heat);
            w.Write(v.Ammo); w.Write(v.Mines); w.Write(v.Lap); w.Write(v.NextCheckpoint); w.Write(v.PassedGates);
            w.Write(v.Respawn); w.Write(v.Invulnerability); w.Write(v.AcknowledgedSequence); w.Write(v.FinishTime);
            w.Write(v.SlowRemaining); w.Write(v.SlowFactor);
        }
        return stream.ToArray();
    }
    public static WorldSnapshot DecodeSnapshot(byte[] bytes)
    {
        using var r = Reader(bytes, PacketKind.Snapshot);
        uint raceId = r.ReadUInt32(), tick = r.ReadUInt32(); var phase = (RacePhase)r.ReadByte(); double elapsed = r.ReadDouble(); float remaining = r.ReadSingle(); int count = r.ReadByte();
        if (count > 8 || phase > RacePhase.Podium || !double.IsFinite(elapsed) || !float.IsFinite(remaining)) throw new InvalidDataException("Snapshot header.");
        var cars = new VehicleState[count]; var ids = new HashSet<int>();
        for (int i = 0; i < count; i++)
        {
            int id = r.ReadInt32(); var position = ReadVector(r); var rotation = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            var velocity = ReadVector(r); var angular = ReadVector(r);
            var v = new VehicleState(id, position, rotation, velocity, angular, r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(),
                r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadSingle(), r.ReadSingle(), r.ReadUInt32(), r.ReadDouble(), r.ReadSingle(), r.ReadSingle());
            if (!ids.Add(id) || !float.IsFinite(rotation.LengthSquared()) || rotation.LengthSquared() < .5f || rotation.LengthSquared() > 1.5f ||
                !float.IsFinite(v.Health) || !float.IsFinite(v.Energy) || !float.IsFinite(v.Heat) || !float.IsFinite(v.TurretYaw) ||
                !float.IsFinite(v.Respawn) || !float.IsFinite(v.Invulnerability) || !double.IsFinite(v.FinishTime) || !float.IsFinite(v.SlowRemaining) || !float.IsFinite(v.SlowFactor)) throw new InvalidDataException("Vehicle values.");
            cars[i] = v;
        }
        if (r.BaseStream.Position != r.BaseStream.Length) throw new InvalidDataException("Trailing snapshot bytes.");
        return new(raceId, tick, phase, elapsed, remaining, cars);
    }
    public static byte[] EncodeJson<T>(PacketKind kind, T value)
    {
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, GameCatalog.Json);
        if (json.Length + 1 > MaxPacketBytes) throw new InvalidDataException("Packet too large.");
        var bytes = new byte[json.Length + 1]; bytes[0] = (byte)kind; json.CopyTo(bytes, 1); return bytes;
    }
    public static T DecodeJson<T>(byte[] bytes, PacketKind expected)
    {
        using var reader = Reader(bytes, expected);
        return System.Text.Json.JsonSerializer.Deserialize<T>(bytes.AsSpan(1), GameCatalog.Json) ?? throw new InvalidDataException("Empty JSON.");
    }
    public static string ContentHash(IEnumerable<string> documents)
    {
        var canonical = documents.Select(text =>
        {
            using var document = System.Text.Json.JsonDocument.Parse(text);
            return System.Text.Json.JsonSerializer.Serialize(document.RootElement);
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", canonical))));
    }
    private static BinaryReader Reader(byte[] bytes, PacketKind expected)
    {
        if (bytes.Length is < 1 or > MaxPacketBytes || bytes[0] != (byte)expected) throw new InvalidDataException("Packet header.");
        var r = new BinaryReader(new MemoryStream(bytes, false)); r.ReadByte(); return r;
    }
    private static void Write(BinaryWriter w, Vector3 v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
    private static Vector3 ReadVector(BinaryReader r)
    {
        var v = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        if (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z)) throw new InvalidDataException("Vector values.");
        return v;
    }
}
