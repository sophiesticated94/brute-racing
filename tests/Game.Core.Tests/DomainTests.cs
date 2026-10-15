using Game.Core;

namespace Game.Core.Tests;

public class DomainTests
{
    private static string DataRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../data"));
    internal static GameCatalog Catalog() => GameCatalog.Load(file => File.ReadAllText(Path.Combine(DataRoot, file)));

    [Fact]
    public void ShippedDefinitionsAreValidAndStatsComeFromTheCatalog()
    {
        var catalog = Catalog(); var loadout = PlayerLoadoutDto.Default;
        Assert.Equal(4, catalog.Weapons.Count);
        Assert.Equal(catalog.Cars["rook"], VehicleStatsCalculator.Calculate(catalog, loadout));
        Assert.NotSame(catalog.Cars["rook"], VehicleStatsCalculator.Calculate(catalog, loadout));
    }
    [Theory]
    [InlineData("unknown", "machine_gun", 1)]
    [InlineData("rook", "unknown", 1)]
    [InlineData("rook", "mine", 1)]
    [InlineData("rook", "rocket", 0)]
    [InlineData("rook", "laser", 6)]
    public void ValidatorRejectsUnavailableEquipment(string car, string weapon, int rank)
    {
        var loadout = new PlayerLoadoutDto(car, weapon, rank, new());
        Assert.False(new BuildValidator(Catalog()).IsValid(loadout));
        Assert.Throws<ArgumentException>(() => VehicleStatsCalculator.Calculate(Catalog(), loadout));
    }
    [Fact]
    public void ValidatorRejectsNullAndUnimplementedSkills()
    {
        var validator = new BuildValidator(Catalog());
        Assert.False(validator.IsValid(null));
        Assert.False(validator.IsValid(PlayerLoadoutDto.Default with { SkillRanks = new() { ["fake"] = 999 } }));
        Assert.False(validator.IsValid(PlayerLoadoutDto.Default with { SkillRanks = null! }));
    }
    [Fact]
    public void CorruptTrackFailsAtStartup()
    {
        Assert.Throws<InvalidDataException>(() => GameCatalog.Load(file => file.StartsWith("tracks") ?
            "{\"id\":\"bad\",\"laps\":3,\"radiusX\":40,\"radiusZ\":25,\"width\":10,\"segments\":64,\"checkpoints\":0}" : File.ReadAllText(Path.Combine(DataRoot, file))));
    }
    [Fact]
    public void GatesMustBeCrossedInOrderAndForward()
    {
        var race = new RaceProgress(4, 2);
        Assert.False(race.Pass(0, 10, 1)); Assert.False(race.Pass(2, 10, 1));
        Assert.False(race.Pass(1, -10, 1)); Assert.False(race.Pass(1, 0, 1));
        Assert.True(race.Pass(1, 10, 2)); Assert.False(race.Pass(1, 10, 3));
        Assert.True(race.Pass(2, 10, 3)); Assert.True(race.Pass(3, 10, 4)); Assert.True(race.Pass(0, 10, 5));
        Assert.Equal(1, race.CompletedLaps); Assert.False(race.Finished);
        foreach (int gate in new[] { 1, 2, 3, 0 }) Assert.True(race.Pass(gate, 10, 10));
        Assert.True(race.Finished); Assert.Equal(10d, race.FinishTime); Assert.Equal(8, race.PassedGates);
        Assert.False(race.Pass(1, 10, 11));
    }
    [Fact]
    public void GateGeometryRejectsReverseShortcutsWrongElevationAndTeleports()
    {
        var center = System.Numerics.Vector3.Zero; var forward = System.Numerics.Vector3.UnitZ;
        Assert.True(CheckpointGeometry.CrossesForward(new(0, 1, -1), new(0, 1, 1), center, forward, 5, 4));
        Assert.False(CheckpointGeometry.CrossesForward(new(0, 1, 1), new(0, 1, -1), center, forward, 5, 4));
        Assert.False(CheckpointGeometry.CrossesForward(new(7, 1, -1), new(7, 1, 1), center, forward, 5, 4));
        Assert.False(CheckpointGeometry.CrossesForward(new(0, 10, -1), new(0, 10, 1), center, forward, 5, 4));
        Assert.False(CheckpointGeometry.CrossesForward(new(0, 1, -30), new(0, 1, 30), center, forward, 5, 4));
    }
    [Fact]
    public void RankingPrioritizesFinishThenGatesThenDistanceAndStableId()
    {
        var order = RaceRanking.Rank([
            new(1, 12, 1, 45), new(2, 12, 0, 40), new(3, 11, 2, null),
            new(4, 10, .5f, null), new(5, 11, 1, null), new(6, 11, 1, null)]);
        Assert.Equal([2, 1, 5, 6, 3, 4], order);
    }
    [Fact]
    public void FirstFinishStartsThirtySecondDeadlineOnlyOnce()
    {
        var clock = new RaceClock(); clock.Start();
        clock.Update(3.01, false); Assert.Equal(RacePhase.Racing, clock.Phase);
        clock.Update(10, false); clock.NotifyFinish(); clock.Update(20, false);
        Assert.Equal(10, clock.Remaining, 6); clock.NotifyFinish(); clock.Update(10.01, false);
        Assert.Equal(RacePhase.Podium, clock.Phase);
    }
    [Fact]
    public void AllFinishedEndsImmediatelyAndNewRaceResetsClock()
    {
        var clock = new RaceClock(); clock.Start(); clock.Update(4, false); clock.Update(10, true);
        Assert.Equal(RacePhase.Podium, clock.Phase); clock.Start();
        Assert.Equal(RacePhase.Countdown, clock.Phase); Assert.Equal(0, clock.Elapsed); Assert.Equal(3, clock.Remaining);
    }
}
