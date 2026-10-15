using System.Numerics;
using Game.Core;

namespace Game.Core.Tests;

public class NetworkTests
{
    private static PlayerInputCommand Command(uint seq = 1) => new(seq, seq, -.5f, 1, .2f, -1.4f, true, true, false);
    private static VehicleState Vehicle(int id = 1) => new(id, new(1, 2, 3), Quaternion.Identity, new(4, 5, 6), Vector3.UnitY,
        1.4f, 80, 45, 20, 12, 5, 1, 3, 15, 0, 1, 101, -1);

    [Fact]
    public void RedundantInputsRoundTripExactly()
    {
        var inputs = new[] { Command(1), Command(2), Command(3) };
        Assert.Equal(inputs, PacketCodec.DecodeInputs(PacketCodec.EncodeInputs(inputs)));
    }
    [Fact]
    public void SnapshotForEightCarsFitsOneSmallPacketAndPreservesRaceEpoch()
    {
        var snapshot = new WorldSnapshot(7, 600, RacePhase.Racing, 10.5, 24.5f, Enumerable.Range(1, 8).Select(Vehicle).ToArray());
        var bytes = PacketCodec.EncodeSnapshot(snapshot); Assert.True(bytes.Length <= 1200);
        var decoded = PacketCodec.DecodeSnapshot(bytes);
        Assert.Equal(snapshot.RaceId, decoded.RaceId); Assert.Equal(snapshot.Tick, decoded.Tick);
        Assert.Equal(snapshot.Vehicles, decoded.Vehicles); Assert.Equal(snapshot.Elapsed, decoded.Elapsed);
    }
    [Fact]
    public void EventsPreserveVectorFieldsAndEpochInJson()
    {
        var e = new RaceEvent(9, 100, "rocket", 1, 2, new(8, 2, 1), new(12, 0, 4), 7);
        Assert.Equal(e, PacketCodec.DecodeJson<RaceEvent>(PacketCodec.EncodeJson(PacketKind.Event, e), PacketKind.Event));
    }
    [Fact]
    public void InputGateRejectsDuplicatesReorderingNaNAndSequenceFloods()
    {
        var gate = new InputGate(); Assert.True(gate.Accept(Command(2), 1));
        Assert.False(gate.Accept(Command(2), 1)); Assert.False(gate.Accept(Command(1), 1));
        Assert.False(gate.Accept(Command(3) with { Steering = float.NaN }, 1));
        Assert.False(gate.Accept(Command(3) with { Throttle = 100 }, 1));
        Assert.False(gate.Accept(Command(10000), 1)); Assert.Equal(2u, gate.LastSequence);
        Assert.True(gate.Read(1.05).Fire); var stale = gate.Read(1.2);
        Assert.False(stale.Fire); Assert.False(stale.Mine); Assert.Equal(0, stale.Throttle); Assert.Equal(1, stale.Brake);
    }
    [Fact]
    public void LostInputRecoveredWithoutExecutingMoreThanOneCurrentCommand()
    {
        var gate = new InputGate();
        foreach (var c in PacketCodec.DecodeInputs(PacketCodec.EncodeInputs([Command(1), Command(2), Command(3)]))) gate.Accept(c, 0);
        Assert.Equal(3u, gate.Current.Sequence);
        foreach (var c in PacketCodec.DecodeInputs(PacketCodec.EncodeInputs([Command(2), Command(3), Command(4)]))) gate.Accept(c, .01);
        Assert.Equal(4u, gate.Current.Sequence);
    }
    [Fact]
    public void MalformedAndOversizePacketsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => PacketCodec.DecodeInputs([2, 4]));
        Assert.Throws<InvalidDataException>(() => PacketCodec.DecodeInputs([2, 1]));
        Assert.Throws<InvalidDataException>(() => PacketCodec.DecodeSnapshot(new byte[17000]));
        var input = PacketCodec.EncodeInputs([Command()]); input[^1] = 255;
        Assert.Throws<InvalidDataException>(() => PacketCodec.DecodeInputs(input));
        var snapshot = PacketCodec.EncodeSnapshot(new(1, 1, RacePhase.Racing, 1, 0, [Vehicle(), Vehicle()]));
        Assert.Throws<InvalidDataException>(() => PacketCodec.DecodeSnapshot(snapshot));
        var valid = PacketCodec.EncodeSnapshot(new(1, 1, RacePhase.Racing, 1, 0, [Vehicle()]));
        Assert.Throws<InvalidDataException>(() => PacketCodec.DecodeSnapshot([.. valid, 0]));
    }
    [Fact]
    public void PacketFuzzNeverEscapesExpectedParseFailures()
    {
        var random = new Random(99);
        for (int i = 0; i < 500; i++)
        {
            byte[] data = new byte[random.Next(1, 160)]; random.NextBytes(data);
            data[0] = i % 2 == 0 ? (byte)PacketKind.Input : (byte)PacketKind.Snapshot;
            try { if (i % 2 == 0) PacketCodec.DecodeInputs(data); else PacketCodec.DecodeSnapshot(data); }
            catch (Exception e) { Assert.True(e is InvalidDataException or EndOfStreamException, e.GetType().Name); }
        }
    }
    [Fact]
    public void DataHashIgnoresPlatformLineEndingsButDetectsGameplayChanges()
    {
        Assert.Equal(PacketCodec.ContentHash(["{\"speed\": 20}\r\n"]), PacketCodec.ContentHash(["{\n\"speed\":20\n}"]));
        Assert.NotEqual(PacketCodec.ContentHash(["{\"speed\":20}"]), PacketCodec.ContentHash(["{\"speed\":21}"]));
    }
    [Fact]
    public void PredictionIsRepeatableAndAuthorityResetDoesNotAccumulateError()
    {
        var car = DomainTests.Catalog().Cars["rook"];
        var authority = new PredictionState(new(0, .98f, 0), Vector3.Zero, 0);
        var input = new PredictedInput(Command() with { Steering = .2f }, true, 1, .2f);
        var a = authority; var b = authority;
        for (int i = 0; i < 120; i++) { a = MovementPrediction.Step(a, input, car); b = MovementPrediction.Step(b, input, car); }
        Assert.Equal(a, b); Assert.True(a.Position.Length() > 10);
        var replay1 = MovementPrediction.Step(authority, input, car);
        var replay2 = MovementPrediction.Step(authority, input, car);
        Assert.Equal(replay1, replay2);
    }
}
