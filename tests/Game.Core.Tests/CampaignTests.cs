using Game.Core;

namespace Game.Core.Tests;

public class CampaignTests
{
    [Fact]
    public void CampaignStartsFromZeroAndRewardsAreIdempotent()
    {
        var catalog=DomainTests.Catalog();
        var state=new CampaignState(catalog,6,[(1,PlayerLoadoutDto.Default with {WeaponLevel=5,PlayerLevel=6}),(2,PlayerLoadoutDto.Default)]);
        Assert.Equal(1,state.Players[1].Loadout.WeaponLevel);Assert.Equal(0,state.Players[1].Credits);
        Guid race=Guid.NewGuid();Assert.True(state.RecordRace(race,[1,2],new Dictionary<int,bool>{{1,true},{2,true}},new Dictionary<int,CombatScore>()));
        int credits=state.Players[1].Credits;
        Assert.False(state.RecordRace(race,[1,2],new Dictionary<int,bool>(),new Dictionary<int,CombatScore>()));
        Assert.Equal(credits,state.Players[1].Credits);Assert.True(state.Players[1].Level>=2);Assert.True(state.AvailablePoints(1)>=6);
        Assert.True(state.TryPurchase(1,"machine_gun"));Assert.Equal(2,state.Players[1].Loadout.WeaponLevel);
        Assert.False(state.TryPurchase(1,"rocket"));
    }
    [Fact]
    public void HostRejectsUnpurchasedEquipmentAndForgedSkillBudget()
    {
        var catalog=DomainTests.Catalog();var state=new CampaignState(catalog,3,[(1,PlayerLoadoutDto.Default)]);
        Assert.False(state.TrySetLoadout(1,PlayerLoadoutDto.Default with {WeaponLevel=5},LobbyBalanceRules.OpenMayhem,out _));
        Assert.False(state.TrySetLoadout(1,PlayerLoadoutDto.Default with {PlayerLevel=6,SkillRanks=new(){{"drive_accel",1}}},LobbyBalanceRules.OpenMayhem,out _));
        Assert.Empty(state.Players[1].Loadout.SkillRanks);
    }
    [Fact]
    public void FullCampaignAwardsCoinsAndNextCampaignResetsPower()
    {
        var catalog=DomainTests.Catalog();var state=new CampaignState(catalog,3,[(1,PlayerLoadoutDto.Default)]);
        for(int i=0;i<3;i++)state.RecordRace(Guid.NewGuid(),[1],new Dictionary<int,bool>{{1,true}},new Dictionary<int,CombatScore>());
        Assert.True(state.Complete);Assert.True(state.RewardCoins(1)>0);Assert.Equal(3,state.CompletedRounds);
        Assert.False(state.RecordRace(Guid.NewGuid(),[1],new Dictionary<int,bool>(),new Dictionary<int,CombatScore>()));
        var next=new CampaignState(catalog,3,[(1,state.Players[1].Loadout)]);
        Assert.Equal(1,next.Players[1].Level);Assert.Empty(next.Players[1].Loadout.SkillRanks);
    }
}
