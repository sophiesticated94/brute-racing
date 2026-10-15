using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Game.Core;

namespace Game.Core.Tests;
public class CampaignPersistenceTests
{
    private static CampaignState Campaign(GameCatalog catalog)
    {
        var state=new CampaignState(catalog,3,[(1,PlayerLoadoutDto.Default),(2,PlayerLoadoutDto.Default)]);
        state.Configure(Guid.NewGuid(),LobbyBalanceRules.OpenMayhem);
        foreach(int peer in new[]{1,2}){state.Describe(peer,"Driver"+peer,peer,false);state.Register(peer,Guid.NewGuid());state.Confirm(peer);}
        return state;
    }
    private static bool Finish(CampaignState state,Guid race)
    {bool applied=state.RecordRace(race,[1,2],new Dictionary<int,bool>{{1,true},{2,true}},new Dictionary<int,CombatScore>());if(applied)state.QueueRewards(race);return applied;}
    [Fact]
    public void SnapshotRestoresStablePlayersAndUnacknowledgedRewards()
    {
        var catalog=DomainTests.Catalog();var state=Campaign(catalog);Guid race=Guid.NewGuid();Finish(state,race);
        Assert.True(state.TryPurchase(1,"machine_gun"));Guid participant=state.Players[1].ParticipantId;
        string saved=CampaignSnapshotCodec.Encode(catalog,state.Capture());
        var restored=CampaignState.Restore(catalog,CampaignSnapshotCodec.Decode(catalog,saved));restored.Bind(participant,222);
        Assert.Equal(state.Players[1].Credits,restored.Players[222].Credits);Assert.Equal(2,restored.Players[222].Loadout.WeaponLevel);
        Assert.Equal(2,restored.PendingRewards.Count);Assert.Equal(race,restored.PendingRewards[0].Receipt.Id);
        Assert.True(restored.Acknowledge(222,race));Assert.False(restored.Acknowledge(222,race));
        var again=CampaignState.Restore(catalog,restored.Capture());Assert.Single(again.PendingRewards);
        Assert.False(again.RecordRace(race,[],new Dictionary<int,bool>(),new Dictionary<int,CombatScore>()));
    }
    [Fact]
    public void CommitIsAtomicAndCommandsAreIdempotentAcrossRestart()
    {
        var catalog=DomainTests.Catalog();var initial=Campaign(catalog);Finish(initial,Guid.NewGuid());
        bool fail=false;CampaignSnapshot? disk=null;
        var durable=new DurableCampaign(initial,s=>{if(fail)throw new IOException("disk full");disk=s;});Assert.True(durable.Initialize());
        int before=durable.Current.Players[1].Credits;Guid buy=Guid.NewGuid();fail=true;
        Assert.False(durable.Commit(buy,s=>s.TryPurchase(1,"machine_gun")));Assert.True(durable.SaveBlocked);Assert.Equal(before,durable.Current.Players[1].Credits);
        fail=false;Assert.True(durable.Retry());Assert.Equal(before,durable.Current.Players[1].Credits);
        Assert.True(durable.Commit(buy,s=>s.TryPurchase(1,"machine_gun")));int paid=durable.Current.Players[1].Credits;
        Assert.True(durable.Commit(buy,s=>throw new Exception("duplicate executed")));Assert.Equal(paid,durable.Current.Players[1].Credits);
        var loaded=CampaignState.Restore(catalog,disk!);Assert.True(loaded.HasOperation(buy));
    }
    [Fact]
    public void FailedRaceCommitKeepsResultPrivateUntilRetried()
    {
        var catalog=DomainTests.Catalog();bool fail=true;
        var durable=new DurableCampaign(Campaign(catalog),_=>{if(fail)throw new IOException("disk full");});Guid race=Guid.NewGuid();
        Assert.False(durable.Commit(race,s=>Finish(s,race),true));Assert.Equal(0,durable.Current.CompletedRounds);Assert.Empty(durable.Current.PendingRewards);
        fail=false;Assert.True(durable.Retry());Assert.Equal(1,durable.Current.CompletedRounds);Assert.Equal(2,durable.Current.PendingRewards.Count);
        Assert.True(durable.Commit(race,_=>throw new Exception("repeated result")));
    }
    [Fact]
    public void SnapshotDetectsCorruptionWrongRulesAndRecoversBackup()
    {
        var catalog=DomainTests.Catalog();string folder=Path.Combine(Path.GetTempPath(),"brute-campaign-"+Guid.NewGuid());
        try
        {
            var repo=new CampaignRepository(catalog,folder);var state=Campaign(catalog);repo.Save(state.Capture());Finish(state,Guid.NewGuid());repo.Save(state.Capture());
            File.WriteAllText(Path.Combine(folder,state.Id+".json"),"broken");var recovered=repo.Load(state.Id,out string warning);
            Assert.Contains("kopii",warning);Assert.Equal(0,recovered.CompletedRounds);Assert.Single(Directory.GetFiles(folder,"*.corrupt-*"));
            // A successful save after recovery must not replace the good backup with corrupt bytes.
            repo.Save(recovered with{Revision=1});File.WriteAllText(Path.Combine(folder,state.Id+".json"),"broken again");
            Assert.Equal(0,repo.Load(state.Id,out _).CompletedRounds);
            Assert.Throws<NotSupportedException>(()=>CampaignSnapshotCodec.Validate(catalog,recovered with{RulesetHash="other"}));
            Assert.Throws<NotSupportedException>(()=>CampaignSnapshotCodec.Validate(catalog,recovered with{SchemaVersion=99}));
        }
        finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }
    [Fact]
    public void ProofNeedsTheSecretAndIsBoundToPeerAndOneChallenge()
    {
        var auth=new CampaignAuthentication();Guid participant=Guid.NewGuid(),profile=Guid.NewGuid(),campaign=Guid.NewGuid();string secret=CampaignAuthentication.NewSecret();
        var challenge=auth.Issue(99,campaign,participant,profile);string proof=CampaignAuthentication.Proof(new(participant,secret),challenge);
        Assert.False(auth.Verify(98,proof,_=>secret));Assert.True(auth.Verify(99,proof,_=>secret));Assert.False(auth.Verify(99,proof,_=>secret));
        var next=auth.Issue(99,campaign,participant,profile);Assert.NotEqual(challenge.Nonce,next.Nonce);Assert.False(auth.Verify(99,proof,_=>secret));
        Assert.False(auth.Verify(99,CampaignAuthentication.Proof(new(participant,secret),next),_=>secret));
    }
    [Fact]
    public void SavedPurchaseAndRewardCanBeRetriedAfterAcknowledgementWasLost()
    {
        var catalog=DomainTests.Catalog();CampaignSnapshot? disk=null;
        var host=new DurableCampaign(Campaign(catalog),s=>disk=CampaignSnapshotCodec.Clone(s));Guid race=Guid.NewGuid();
        Assert.True(host.Commit(race,s=>Finish(s,race)));Guid purchase=Guid.NewGuid();
        Assert.True(host.Commit(purchase,s=>s.TryPurchase(1,"machine_gun")));
        int credits=host.Current.Players[1].Credits;
        var restarted=new DurableCampaign(CampaignState.Restore(catalog,disk!),s=>disk=CampaignSnapshotCodec.Clone(s));
        Guid participant=host.Current.Players[1].ParticipantId;restarted.Current.Bind(participant,1001);
        Assert.True(restarted.Commit(purchase,_=>throw new Exception("duplicate purchase")));Assert.Equal(credits,restarted.Current.Players[1001].Credits);
        var profile=new PlayerProfile();var receipt=restarted.Current.PendingRewards.Single(r=>r.ParticipantId==participant).Receipt;
        Assert.True(ProfileRules.ApplyReward(catalog,profile,receipt));string savedProfile=ProfileCodec.Encode(catalog,profile);
        // Profile committed; the host never received the acknowledgement before restarting.
        profile=ProfileCodec.Decode(catalog,savedProfile);Assert.False(ProfileRules.ApplyReward(catalog,profile,receipt));Assert.Equal(1,profile.CompletedRaces);
        Assert.True(restarted.Commit(Guid.NewGuid(),s=>s.Acknowledge(1001,receipt.Id)));
        var again=CampaignState.Restore(catalog,disk!);Assert.DoesNotContain(again.PendingRewards,r=>r.ParticipantId==participant);
    }
    [Fact]
    public void ProfileRecoverySurvivesAnotherRestartBeforeTheNextSave()
    {
        var catalog=DomainTests.Catalog();string folder=Path.Combine(Path.GetTempPath(),"brute-profile-"+Guid.NewGuid());string path=Path.Combine(folder,"profile.json");
        try
        {
            var repo=new ProfileRepository(catalog,path);var profile=new PlayerProfile{Coins=60};Guid identity=profile.PlayerId;
            repo.Save(profile);repo.Save(profile);File.WriteAllText(path,"corrupt");
            Assert.Equal(identity,repo.Load(out _).PlayerId);Assert.False(File.Exists(path));
            var restarted=new ProfileRepository(catalog,path);var recovered=restarted.Load(out string warning);
            Assert.Equal(identity,recovered.PlayerId);Assert.Equal(60,recovered.Coins);Assert.Contains("kopii",warning);
        }
        finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }
    [Fact]
    public void MigrationChecksTheOriginalV1ChecksumBeforeAddingCredentials()
    {
        var catalog=DomainTests.Catalog();
        string legacy=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/profile-v1.json"));
        var migrated=ProfileCodec.Decode(catalog,legacy);Assert.Equal(2,migrated.SchemaVersion);Assert.Equal(150,migrated.Coins);Assert.Equal(2,migrated.CompletedCampaigns);Assert.Equal("Profil migracji",migrated.Name);Assert.Equal(3,migrated.Presets.Length);Assert.Empty(migrated.CampaignCredentials);
        Assert.Throws<InvalidDataException>(()=>ProfileCodec.Decode(catalog,legacy.Replace("\"coins\":150","\"coins\":999")));
    }
}
