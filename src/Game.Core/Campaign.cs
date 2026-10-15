namespace Game.Core;

public enum GameMode { Exhibition, Campaign, Quick }
public sealed record EconomyDefinition(int XpPerLevel = 100, int SkillPointsPerLevel = 6,
    int BaseCredits = 250, int CreditsRoundStep = 150, int FinishXp = 20, int PositionCredits = 10,
    int CampaignCoins = 40, int CoinsPerRound = 5, int CoinsPerPosition = 15);
public sealed record CosmeticDefinition(string Id, string Name, string Type, string Color, int Price);
public sealed record CombatScore(int Hits = 0, int Kills = 0);
public sealed record CampaignPlayerView(int Id, int Xp, int Level, int Credits, int AvailablePoints,
    int ChampionshipPoints, Dictionary<string,int> OwnedLevels, PlayerLoadoutDto Loadout);
public sealed record CampaignView(Guid Id, int Length, int CompletedRounds, bool Complete, CampaignPlayerView[] Players, int[] Ranking);

public sealed class CampaignParticipant
{
    public int Id { get; }
    public int Xp { get; internal set; }
    private readonly int xpPerLevel;
    public int Level => Math.Min(6,1+Xp/xpPerLevel);
    public int Credits { get; internal set; }
    public int ChampionshipPoints { get; internal set; }
    public Dictionary<string,int> OwnedLevels { get; } = new(StringComparer.Ordinal);
    public PlayerLoadoutDto Loadout { get; internal set; }
    internal CampaignParticipant(int id,PlayerLoadoutDto initial,GameCatalog catalog)
    {
        Id=id; xpPerLevel=catalog.Economy.XpPerLevel; foreach (string weapon in catalog.Weapons.Keys) OwnedLevels[weapon]=1;
        Loadout=new("rook",initial.WeaponId,1,new(),1,1,initial.CosmeticId);
    }
}

public sealed class CampaignState
{
    public Guid Id { get; } = Guid.NewGuid();
    public int Length { get; }
    public int CompletedRounds { get; private set; }
    public bool Complete => CompletedRounds>=Length;
    public Dictionary<int,CampaignParticipant> Players { get; } = [];
    private readonly HashSet<Guid> appliedRaces = [];
    private readonly GameCatalog catalog;
    public CampaignState(GameCatalog catalog,int length,IEnumerable<(int Id,PlayerLoadoutDto Initial)> players)
    {
        if (length is < 3 or > 10) throw new ArgumentOutOfRangeException(nameof(length));
        this.catalog=catalog; Length=length;
        foreach (var player in players) Players.Add(player.Id,new(player.Id,player.Initial,catalog));
    }
    public int AvailablePoints(int id) => Math.Max(0,(Players[id].Level-1)*catalog.Economy.SkillPointsPerLevel-catalog.Skills.Cost(Players[id].Loadout.SkillRanks));
    public void Synchronize(IEnumerable<(int Id,PlayerLoadoutDto Initial)> roster)
    {
        if(CompletedRounds>0)return;
        var all=roster.ToArray();
        foreach(int id in Players.Keys.Except(all.Select(p=>p.Id)).ToArray())Players.Remove(id);
        foreach(var entry in all)if(!Players.ContainsKey(entry.Id))Players.Add(entry.Id,new(entry.Id,entry.Initial,catalog));
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
        CompletedRounds++;return true;
    }
    public int[] Ranking()=>Players.Values.OrderByDescending(p=>p.ChampionshipPoints).ThenBy(p=>p.Id).Select(p=>p.Id).ToArray();
    public CampaignView View()=>new(Id,Length,CompletedRounds,Complete,Players.Values.Select(p=>new CampaignPlayerView(p.Id,p.Xp,p.Level,p.Credits,AvailablePoints(p.Id),p.ChampionshipPoints,new(p.OwnedLevels,StringComparer.Ordinal),p.Loadout)).ToArray(),Ranking());
    public int RewardCoins(int id)=>Complete ? catalog.Economy.CampaignCoins+Length*catalog.Economy.CoinsPerRound+(Players.Count-Array.IndexOf(Ranking(),id)-1)*catalog.Economy.CoinsPerPosition : 0;
}
