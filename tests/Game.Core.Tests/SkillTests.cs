using Game.Core;

namespace Game.Core.Tests;

public class SkillTests
{
    [Fact]
    public void ShippedGraphHasFiveBranchesAndThirtyNodes()
    {
        var graph = DomainTests.Catalog().Skills;
        Assert.Equal(30,graph.Nodes.Count); Assert.Equal(5,graph.Nodes.Values.Select(n => n.Branch).Distinct().Count());
        Assert.All(graph.Nodes.Values.GroupBy(n => n.Branch),g => Assert.True(g.Count() >= 6));
        foreach (var node in graph.Nodes.Values)
            foreach (var prerequisite in node.Prerequisites)
                Assert.True(Array.IndexOf(graph.TopologicalOrder,prerequisite.NodeId) < Array.IndexOf(graph.TopologicalOrder,node.Id));
    }
    [Fact]
    public void GraphRejectsCyclesMissingReferencesAndRankCostMismatch()
    {
        var nodes = DomainTests.Catalog().Skills.Nodes.Values.ToArray();
        var cycle = nodes.Select(n => n.Id == "drive_accel" ? n with { Prerequisites = [new("drive_speed",1)] } : n);
        Assert.Throws<InvalidDataException>(() => new SkillGraph(cycle));
        Assert.Throws<InvalidDataException>(() => new SkillGraph(nodes.Select(n => n.Id=="drive_accel" ? n with { Prerequisites=[new("missing",1)] } : n)));
        Assert.Throws<InvalidDataException>(() => new SkillGraph(nodes.Select(n => n.Id=="drive_accel" ? n with { RankCosts=[] } : n)));
    }
    [Fact]
    public void ValidatorRejectsForgedRanksMissingPrerequisitesAndLevel()
    {
        var validator = new BuildValidator(DomainTests.Catalog());
        var baseline = LoadoutDeclaration.FromLegacy(PlayerLoadoutDto.Default) with {PlayerLevel=6};
        foreach (var ranks in new[] { new Dictionary<string,int>{{"drive_accel",-1}},new(){{"drive_accel",99}},new(){{"drive_speed",1}},new(){{"fake",1}} })
            Assert.False(validator.Validate(baseline with {SkillRanks=ranks},LobbyBalanceRules.FairCap).Valid);
        Assert.False(validator.Validate(baseline with {PlayerLevel=1,SkillRanks=new(){{"drive_accel",1}}},LobbyBalanceRules.OpenMayhem).Valid);
    }
    [Fact]
    public void FairCapNormalizesCopyWithoutBreakingDependencies()
    {
        var catalog = DomainTests.Catalog(); var validator = new BuildValidator(catalog);
        var ranks = catalog.Skills.Nodes.Values.Where(n => n.Branch=="Drive").ToDictionary(n=>n.Id,n=>n.MaxRank);
        var declaration = new LoadoutDeclaration("rook",2,"rocket",5,5,ranks,6);
        var result = validator.Validate(declaration,LobbyBalanceRules.FairCap);
        Assert.True(result.Valid); Assert.True(result.Adjusted); Assert.True(result.SpentPoints<=15);
        Assert.Equal(3,result.Effective!.PrimaryLevel); Assert.Equal(5,declaration.PrimaryLevel);
        Assert.Equal(1,declaration.SkillRanks["drive_boost"]);
        Assert.True(validator.Validate(result.Effective,LobbyBalanceRules.OpenMayhem).Valid);
        var stock = validator.Validate(declaration,LobbyBalanceRules.Stock);
        Assert.Empty(stock.Effective!.SkillRanks); Assert.Equal(1,stock.Effective.PrimaryLevel);
    }
    [Fact]
    public void ExclusiveNodesAreRejectedAndSkillsAffectComputedStats()
    {
        var catalog = DomainTests.Catalog(); var validator = new BuildValidator(catalog);
        var ranks = catalog.Skills.Nodes.Values.Where(n=>n.Branch is "Drive" or "Sabotage").ToDictionary(n=>n.Id,n=>1);
        Assert.False(validator.Validate(new("rook",0,"laser",1,1,ranks,6),LobbyBalanceRules.OpenMayhem).Valid);
        var build = new PlayerLoadoutDto("rook","machine_gun",1,new(){{"drive_accel",3}},1,2);
        Assert.True(VehicleStatsCalculator.CalculateBuild(catalog,build).Vehicle.Acceleration > catalog.Cars["rook"].Acceleration);
    }
}
