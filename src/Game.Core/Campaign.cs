using System.Text.Json.Serialization;

namespace Game.Core;

public enum GameMode { Exhibition, Campaign, Quick }
public sealed record EconomyDefinition(int XpPerLevel = 100, int SkillPointsPerLevel = 6,
    int BaseCredits = 250, int CreditsRoundStep = 150, int FinishXp = 20, int PositionCredits = 10,
    int CampaignCoins = 40, int CoinsPerRound = 5, int CoinsPerPosition = 15);
public sealed record CosmeticDefinition(string Id, string Name, string Type, string Color, int Price);
public sealed record CombatScore(int Hits = 0, int Kills = 0);
public sealed record CampaignPlayerView(int Id, int Xp, int Level, int Credits, int AvailablePoints,
    int ChampionshipPoints, Dictionary<string,int> OwnedLevels, PlayerLoadoutDto Loadout,Guid ParticipantId=default);
public sealed record CampaignView(Guid Id, int Length, int CompletedRounds, bool Complete, CampaignPlayerView[] Players, int[] Ranking,long Revision=0,Guid[]? CompletedRaceIds=null);

public sealed class CampaignParticipant
{
    [JsonIgnore] public int Id { get; internal set; }
    public Guid ParticipantId {get;internal set;}=Guid.NewGuid();
    public Guid ProfileId {get;internal set;}
    public int Seat {get;internal set;}
    public string Name {get;internal set;}="Kierowca";
    public int Color {get;internal set;}
    public bool IsBot {get;internal set;}
    public bool IdentityConfirmed {get;internal set;}
    internal string ResumeSecret {get;set;}="";
    public int Xp { get; internal set; }
    private readonly int xpPerLevel;
    public int Level => Math.Min(6,1+Xp/xpPerLevel);
    public int Credits { get; internal set; }
    public int ChampionshipPoints { get; internal set; }
    public Dictionary<string,int> OwnedLevels { get; } = new(StringComparer.Ordinal);
    public PlayerLoadoutDto Loadout { get; internal set; }
    internal CampaignParticipant(int id,PlayerLoadoutDto initial,GameCatalog catalog)
    {
        Id=id;IsBot=id<0;IdentityConfirmed=IsBot;xpPerLevel=catalog.Economy.XpPerLevel; foreach (string weapon in catalog.Weapons.Keys) OwnedLevels[weapon]=1;
        Loadout=new("rook",initial.WeaponId,1,new(),1,1,initial.CosmeticId);
    }
}

public sealed class CampaignState
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public int Length { get; }
    public int CompletedRounds { get; private set; }
    public bool Complete => CompletedRounds>=Length;
    public Dictionary<Guid,CampaignParticipant> Participants {get;}=[];
    private readonly Dictionary<int,Guid> bindings=[];
    public Dictionary<int,CampaignParticipant> Players=>bindings.ToDictionary(p=>p.Key,p=>Participants[p.Value]);
    public long Revision {get;private set;}
    public Guid OwnerProfileId {get;private set;}
    public LobbyBalanceRules Rules {get;private set;}=LobbyBalanceRules.OpenMayhem;
    private readonly HashSet<Guid> appliedOperations=[];
    private readonly List<CampaignRound> rounds=[];
    private readonly List<PendingCampaignReward> outbox=[];
    private readonly List<CampaignRewardAck> rewardAcks=[];
    public IReadOnlyList<PendingCampaignReward> PendingRewards=>outbox;
    public bool HasOperation(Guid id)=>appliedOperations.Contains(id);
    public void MarkOperation(Guid id){if(id==Guid.Empty)throw new ArgumentException("Puste ID operacji.");appliedOperations.Add(id);Revision++;}
    private readonly HashSet<Guid> appliedRaces = [];
    private readonly GameCatalog catalog;
    public CampaignState(GameCatalog catalog,int length,IEnumerable<(int Id,PlayerLoadoutDto Initial)> players)
    {
        if (length is < 3 or > 10) throw new ArgumentOutOfRangeException(nameof(length));
        this.catalog=catalog; Length=length;
        foreach (var player in players)AddParticipant(player.Id,player.Initial);
    }
    public int AvailablePoints(int id) => Math.Max(0,(Players[id].Level-1)*catalog.Economy.SkillPointsPerLevel-catalog.Skills.Cost(Players[id].Loadout.SkillRanks));
    public void Synchronize(IEnumerable<(int Id,PlayerLoadoutDto Initial)> roster)
    {
        if(CompletedRounds>0)return;
        var all=roster.ToArray();
        foreach(int id in Players.Keys.Except(all.Select(p=>p.Id)).ToArray())if(Players[id].IsBot){Participants.Remove(bindings[id]);bindings.Remove(id);}
        foreach(var entry in all)if(!bindings.ContainsKey(entry.Id))AddParticipant(entry.Id,entry.Initial);
    }
    public bool TryPurchase(int id,string weapon)
    {
        if (Complete || !Players.TryGetValue(id,out var p) || !p.OwnedLevels.TryGetValue(weapon,out int level) || level>=5) return false;
        int cost=catalog.WeaponLevels[weapon][level].UpgradeCost;
        if (p.Credits<cost) return false;
        p.Credits-=cost; p.OwnedLevels[weapon]=level+1;
        p.Loadout=p.Loadout with {WeaponLevel=p.OwnedLevels[p.Loadout.WeaponId],MineLevel=p.OwnedLevels["mine"]}; return true;
    }
    public bool TrySetLoadout(int id,PlayerLoadoutDto requested,LobbyBalanceRules rules,out string error)
    {
        error="Nieprawidłowy build lub brak zakupionego wyposażenia.";
        if (Complete || !Players.TryGetValue(id,out var p) || !p.OwnedLevels.TryGetValue(requested.WeaponId??"",out int owned) ||
            requested.WeaponLevel>owned || requested.MineLevel>p.OwnedLevels["mine"]) return false;
        var declaration=LoadoutDeclaration.FromLegacy(requested with {PlayerLevel=p.Level});
        var validation=new BuildValidator(catalog).Validate(declaration,rules);
        if (!validation.Valid) {error=string.Join(" ",validation.Errors);return false;}
        p.Loadout=validation.Effective!.ToLegacy(); error=""; return true;
    }
    public bool TryRank(int id,string node,LobbyBalanceRules rules,out string error)
    {
        error="Brak punktów lub niespełnione wymagania.";
        if (!Players.TryGetValue(id,out var p) || !catalog.Skills.Nodes.ContainsKey(node)) return false;
        var ranks=new Dictionary<string,int>(p.Loadout.SkillRanks,StringComparer.Ordinal); ranks[node]=ranks.GetValueOrDefault(node)+1;
        var desired=p.Loadout with {SkillRanks=ranks};
        var valid=new BuildValidator(catalog).Validate(LoadoutDeclaration.FromLegacy(desired),rules);
        if (!valid.Valid || valid.Adjusted || catalog.Skills.Cost(ranks)>(p.Level-1)*catalog.Economy.SkillPointsPerLevel) return false;
        return TrySetLoadout(id,desired,rules,out error);
    }
    public void Respec(int id) { if(!Complete && Players.TryGetValue(id,out var p)) p.Loadout=p.Loadout with {SkillRanks=new()}; }
    public bool RecordRace(Guid raceId,int[] ranking,IReadOnlyDictionary<int,bool> finished,IReadOnlyDictionary<int,CombatScore> combat)
    {
        if (Complete || raceId==Guid.Empty || !appliedRaces.Add(raceId)) return false;
        if (ranking.Length!=Players.Count || ranking.Distinct().Count()!=Players.Count || ranking.Any(id=>!Players.ContainsKey(id))) { appliedRaces.Remove(raceId); throw new ArgumentException("Nieprawidłowa klasyfikacja kampanii."); }
        for(int i=0;i<ranking.Length;i++)
        {
            var p=Players[ranking[i]]; var score=combat.GetValueOrDefault(p.Id,new());
            int baseXp=(int)Math.Ceiling(5.0*catalog.Economy.XpPerLevel/(Length-1));
            p.Xp+=baseXp+(finished.GetValueOrDefault(p.Id)?catalog.Economy.FinishXp:0)+(ranking.Length-i-1)*5+Math.Min(25,Math.Max(0,score.Hits)+Math.Max(0,score.Kills)*4);
            p.Credits+=catalog.Economy.BaseCredits+CompletedRounds*catalog.Economy.CreditsRoundStep+(ranking.Length-i-1)*catalog.Economy.PositionCredits;
            p.ChampionshipPoints+=ranking.Length-i;
            p.Loadout=p.Loadout with {PlayerLevel=p.Level};
        }
        rounds.Add(new(raceId,ranking.Select(i=>bindings[i]).ToArray()));CompletedRounds++;return true;
    }
    public int[] Ranking()=>Players.Values.OrderByDescending(p=>p.ChampionshipPoints).ThenBy(p=>p.ParticipantId).Select(p=>p.Id).ToArray();
    public CampaignView View()=>new(Id,Length,CompletedRounds,Complete,Players.Values.Select(p=>new CampaignPlayerView(p.Id,p.Xp,p.Level,p.Credits,AvailablePoints(p.Id),p.ChampionshipPoints,new(p.OwnedLevels,StringComparer.Ordinal),p.Loadout,p.ParticipantId)).ToArray(),Ranking(),Revision,appliedRaces.ToArray());
    public int RewardCoins(int id)=>Complete ? catalog.Economy.CampaignCoins+Length*catalog.Economy.CoinsPerRound+(Players.Count-Array.IndexOf(Ranking(),id)-1)*catalog.Economy.CoinsPerPosition : 0;
    private void AddParticipant(int peer,PlayerLoadoutDto initial)
    {
        var participant=new CampaignParticipant(peer,initial,catalog){Seat=Enumerable.Range(1,8).First(i=>Participants.Values.All(p=>p.Seat!=i))};
        Participants.Add(participant.ParticipantId,participant);bindings.Add(peer,participant.ParticipantId);
    }
    public void Configure(Guid owner,LobbyBalanceRules rules){OwnerProfileId=owner;Rules=rules;}
    public void Describe(int peer,string name,int color,bool bot)
    {var p=Players[peer];p.Name=name;p.Color=color;p.IsBot=bot;if(bot)p.IdentityConfirmed=true;}
    public CampaignCredential Credential(Guid participant)=>new(participant,Participants[participant].ResumeSecret);
    public void Appearance(int peer,string name,int color){var p=Players[peer];p.Name=name;p.Color=color;}
    public CampaignParticipant? FindProfile(Guid profile)=>Participants.Values.FirstOrDefault(p=>p.ProfileId==profile&&profile!=Guid.Empty);
    public CampaignCredential Register(int peer,Guid profile)
    {
        if(profile==Guid.Empty)throw new ArgumentException("Brak profilu.");
        var p=Players[peer];p.ProfileId=profile;p.IsBot=false;p.IdentityConfirmed=false;
        p.ResumeSecret=CampaignAuthentication.NewSecret();return new(p.ParticipantId,p.ResumeSecret);
    }
    public bool Confirm(int peer){if(!Players.TryGetValue(peer,out var p))return false;p.IdentityConfirmed=true;return true;}
    public void Bind(Guid participant,int peer)
    {
        if(!Participants.ContainsKey(participant))throw new ArgumentException("Nieznany uczestnik.");
        foreach(int old in bindings.Where(p=>p.Value==participant).Select(p=>p.Key).ToArray())bindings.Remove(old);
        if(bindings.ContainsKey(peer))throw new ArgumentException("Zajęty PeerId.");
        bindings[peer]=participant;Participants[participant].Id=peer;
    }
    public void BindRestored(Guid hostProfile,bool dedicated)
    {
        bindings.Clear();foreach(var p in Participants.Values.OrderBy(p=>p.Seat))Bind(p.ParticipantId,!dedicated&&p.ProfileId==hostProfile?1:-p.Seat);
    }
    public void QueueRewards(Guid race)
    {
        if(!appliedRaces.Contains(race))throw new InvalidOperationException("Niepotwierdzony wyścig.");
        foreach(var p in Participants.Values.Where(p=>!p.IsBot))
        {
            AddReward(p,new(race,RewardKind.Race,CampaignId:Id));
            if(Complete)AddReward(p,new(Id,RewardKind.Campaign,RewardCoins(p.Id),new ArchivedBuild($"Kanion {Length} • {catalog.Weapons[p.Loadout.WeaponId].Name}",p.Loadout,Id,DateTimeOffset.UtcNow),Id));
        }
    }
    private void AddReward(CampaignParticipant p,RewardReceipt receipt)
    {
        if(!outbox.Any(r=>r.ParticipantId==p.ParticipantId&&r.Receipt.Id==receipt.Id)&&!rewardAcks.Any(r=>r.ParticipantId==p.ParticipantId&&r.RewardId==receipt.Id))outbox.Add(new(p.ParticipantId,receipt));
    }
    public bool Acknowledge(int peer,Guid reward)
    {
        if(!bindings.TryGetValue(peer,out var participant))return false;
        if(rewardAcks.Contains(new(participant,reward)))return false;
        int removed=outbox.RemoveAll(r=>r.ParticipantId==participant&&r.Receipt.Id==reward);
        if(removed==0)return false;rewardAcks.Add(new(participant,reward));return true;
    }
    public CampaignSnapshot Capture()=>new(1,Id,Revision,CampaignSnapshotCodec.RulesetHash(catalog,Rules),OwnerProfileId,Rules,Length,CompletedRounds,
        Participants.Values.OrderBy(p=>p.Seat).Select(p=>new CampaignMember(p.ParticipantId,p.ProfileId,p.Seat,p.Name,p.Color,p.IsBot,p.IdentityConfirmed,p.ResumeSecret,p.Xp,p.Level,p.Credits,
            AvailablePoints(p.Id),p.ChampionshipPoints,new(p.OwnedLevels),p.Loadout)).ToArray(),rounds.ToArray(),appliedOperations.ToArray(),outbox.ToArray(),rewardAcks.ToArray());
    public CampaignState Clone()
    {
        var copy=Restore(catalog,CampaignSnapshotCodec.Clone(Capture()));
        copy.bindings.Clear();foreach(var binding in bindings)copy.Bind(binding.Value,binding.Key);
        return copy;
    }
    public static CampaignState Restore(GameCatalog catalog,CampaignSnapshot snapshot)
    {
        CampaignSnapshotCodec.Validate(catalog,snapshot);
        var state=new CampaignState(catalog,snapshot.Length,[]){Id=snapshot.CampaignId,Revision=snapshot.Revision,CompletedRounds=snapshot.CompletedRounds,OwnerProfileId=snapshot.OwnerProfileId,Rules=snapshot.Rules};
        foreach(var member in snapshot.Participants)
        {
            var p=new CampaignParticipant(-member.Seat,member.Loadout,catalog){ParticipantId=member.ParticipantId,ProfileId=member.ProfileId,Seat=member.Seat,Name=member.Name,Color=member.Color,IsBot=member.IsBot,
                IdentityConfirmed=member.IdentityConfirmed,ResumeSecret=member.ResumeSecret,Xp=member.Xp,Credits=member.Credits,ChampionshipPoints=member.ChampionshipPoints,Loadout=member.Loadout};
            p.OwnedLevels.Clear();foreach(var level in member.OwnedLevels)p.OwnedLevels.Add(level.Key,level.Value);
            state.Participants.Add(p.ParticipantId,p);
        }
        state.rounds.AddRange(snapshot.Rounds);state.appliedRaces.UnionWith(snapshot.Rounds.Select(r=>r.RaceId));state.appliedOperations.UnionWith(snapshot.AppliedOperations);
        state.outbox.AddRange(snapshot.PendingRewards);state.rewardAcks.AddRange(snapshot.RewardAcks);state.BindRestored(snapshot.OwnerProfileId,true);return state;
    }

}
