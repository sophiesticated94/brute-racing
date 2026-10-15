using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Game.Core;

public sealed record CampaignCredential(Guid ParticipantId,string Secret);
public sealed record CampaignMember(Guid ParticipantId,Guid ProfileId,int Seat,string Name,int Color,bool IsBot,bool IdentityConfirmed,
    string ResumeSecret,int Xp,int Level,int Credits,int AvailablePoints,int ChampionshipPoints,Dictionary<string,int> OwnedLevels,PlayerLoadoutDto Loadout);
public sealed record CampaignRound(Guid RaceId,Guid[] Ranking);
public sealed record PendingCampaignReward(Guid ParticipantId,RewardReceipt Receipt);
public sealed record CampaignRewardAck(Guid ParticipantId,Guid RewardId);
public sealed record CampaignSnapshot(int SchemaVersion,Guid CampaignId,long Revision,string RulesetHash,Guid OwnerProfileId,LobbyBalanceRules Rules,int Length,int CompletedRounds,
    CampaignMember[] Participants,CampaignRound[] Rounds,Guid[] AppliedOperations,PendingCampaignReward[] PendingRewards,CampaignRewardAck[] RewardAcks);
public sealed record CampaignEnvelope(int SchemaVersion,CampaignSnapshot Campaign,string Checksum);

public static class CampaignSnapshotCodec
{
    public static string RulesetHash(GameCatalog catalog,LobbyBalanceRules rules)=>Hash(JsonSerializer.Serialize(new{version=1,content=catalog.ContentHash,rules},GameCatalog.Json));
    public static CampaignSnapshot Clone(CampaignSnapshot snapshot)=>JsonSerializer.Deserialize<CampaignSnapshot>(JsonSerializer.Serialize(snapshot,GameCatalog.Json),GameCatalog.Json)!;
    public static void Validate(GameCatalog catalog,CampaignSnapshot value)
    {
        if(value.SchemaVersion!=1)throw new NotSupportedException("Nieobsługiwana wersja zapisu kampanii.");
        if(value.Rules is null||!value.Rules.IsValid)throw new InvalidDataException("Nieprawidłowe reguły zapisu kampanii.");
        if(value.RulesetHash!=RulesetHash(catalog,value.Rules))throw new NotSupportedException("Zapis kampanii wymaga innego zestawu danych lub reguł.");
        if(value.CampaignId==Guid.Empty||value.OwnerProfileId==Guid.Empty||value.Revision<0||value.Length is <3 or >10||value.CompletedRounds<0||value.CompletedRounds>value.Length||
            value.Participants is not{Length:>=1 and <=8}||value.Rounds is null||value.Rounds.Length!=value.CompletedRounds||value.AppliedOperations is null||value.AppliedOperations.Length>10000||
            value.PendingRewards is null||value.PendingRewards.Length>160||value.RewardAcks is null||value.RewardAcks.Length>160)throw new InvalidDataException("Nieprawidłowy zapis kampanii.");
        if(value.Participants.Select(p=>p.ParticipantId).Distinct().Count()!=value.Participants.Length||value.Participants.Select(p=>p.Seat).Distinct().Count()!=value.Participants.Length||
            value.Participants.Where(p=>!p.IsBot).Select(p=>p.ProfileId).Distinct().Count()!=value.Participants.Count(p=>!p.IsBot))throw new InvalidDataException("Powtórzona tożsamość kampanii.");
        foreach(var p in value.Participants)
        {
            if(p.ParticipantId==Guid.Empty||p.Seat is <1 or >8||p.Name is null||p.Name.Length is <1 or >20||p.Color is <0 or >7||p.Xp<0||p.Xp>100000||p.Level!=Math.Min(6,1+p.Xp/catalog.Economy.XpPerLevel)||
                p.Credits is <0 or >1000000||p.ChampionshipPoints<0||p.OwnedLevels is null||p.OwnedLevels.Count!=catalog.Weapons.Count||catalog.Weapons.Keys.Any(w=>!p.OwnedLevels.TryGetValue(w,out int l)||l is <1 or >5)||
                !new BuildValidator(catalog).IsValid(p.Loadout)||p.Loadout.PlayerLevel!=p.Level||p.Loadout.WeaponLevel>p.OwnedLevels[p.Loadout.WeaponId]||p.Loadout.MineLevel>p.OwnedLevels["mine"]||
                p.AvailablePoints!=(p.Level-1)*catalog.Economy.SkillPointsPerLevel-catalog.Skills.Cost(p.Loadout.SkillRanks)||
                !p.IsBot&&(p.ProfileId==Guid.Empty||!CampaignAuthentication.IsSecret(p.ResumeSecret)))throw new InvalidDataException("Nieprawidłowy uczestnik kampanii.");
        }
        var ids=value.Participants.Select(p=>p.ParticipantId).ToHashSet();
        if(value.Rounds.Any(r=>r.RaceId==Guid.Empty||r.Ranking is null||r.Ranking.Length!=ids.Count||!r.Ranking.ToHashSet().SetEquals(ids))||value.Rounds.Select(r=>r.RaceId).Distinct().Count()!=value.Rounds.Length||
            value.AppliedOperations.Any(id=>id==Guid.Empty)||value.AppliedOperations.Distinct().Count()!=value.AppliedOperations.Length)throw new InvalidDataException("Nieprawidłowa historia kampanii.");
        bool LegalReward(Guid participant,Guid reward)=>ids.Contains(participant)&&(value.Rounds.Any(r=>r.RaceId==reward)||value.CompletedRounds==value.Length&&reward==value.CampaignId);
        if(value.PendingRewards.Any(r=>!LegalReward(r.ParticipantId,r.Receipt.Id)||r.Receipt.CampaignId!=value.CampaignId)||value.RewardAcks.Any(r=>!LegalReward(r.ParticipantId,r.RewardId))||
            value.PendingRewards.Select(r=>new CampaignRewardAck(r.ParticipantId,r.Receipt.Id)).Distinct().Count()!=value.PendingRewards.Length||value.RewardAcks.Distinct().Count()!=value.RewardAcks.Length||
            value.PendingRewards.Any(r=>value.RewardAcks.Contains(new(r.ParticipantId,r.Receipt.Id))))throw new InvalidDataException("Nieprawidłowe potwierdzenia nagród.");
        foreach(var reward in value.PendingRewards)ProfileRules.ApplyReward(catalog,new PlayerProfile(),reward.Receipt);
    }
    public static string Encode(GameCatalog catalog,CampaignSnapshot snapshot)
    {Validate(catalog,snapshot);return JsonSerializer.Serialize(new CampaignEnvelope(1,snapshot,Hash(JsonSerializer.Serialize(snapshot,GameCatalog.Json))),GameCatalog.Json);}
    public static CampaignSnapshot Decode(GameCatalog catalog,string json)
    {
        if(json.Length>1024*1024)throw new InvalidDataException("Zapis kampanii przekracza limit.");
        var envelope=JsonSerializer.Deserialize<CampaignEnvelope>(json,GameCatalog.Json)??throw new InvalidDataException("Pusty zapis.");
        if(envelope.SchemaVersion!=1)throw new NotSupportedException("Nieobsługiwana wersja zapisu kampanii.");
        if(envelope.Campaign is null||envelope.Checksum!=Hash(JsonSerializer.Serialize(envelope.Campaign,GameCatalog.Json)))throw new InvalidDataException("Nieprawidłowa suma kontrolna kampanii.");
        Validate(catalog,envelope.Campaign);return envelope.Campaign;
    }
    private static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

public static class AtomicJsonFile
{
    public static void Write(string path,string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp=path+".tmp-"+Guid.NewGuid().ToString("N");
        try
        {
            using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {stream.Write(Encoding.UTF8.GetBytes(content));stream.Flush(true);}
            if(File.Exists(path))File.Replace(temp,path,path+".bak",true);else File.Move(temp,path);
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
public sealed class CampaignRepository(GameCatalog catalog,string directory)
{
    public void Save(CampaignSnapshot value)=>AtomicJsonFile.Write(PathFor(value.CampaignId),CampaignSnapshotCodec.Encode(catalog,value));
    public CampaignSnapshot Load(Guid id,out string warning)
    {
        warning="";string file=PathFor(id);
        try{return CampaignSnapshotCodec.Decode(catalog,File.ReadAllText(file));}
        catch(NotSupportedException){throw;}
        catch(Exception e)when(e is IOException or JsonException or InvalidDataException or ArgumentException or NullReferenceException)
        {
            var backup=CampaignSnapshotCodec.Decode(catalog,File.ReadAllText(file+".bak"));
            if(File.Exists(file))File.Copy(file,file+".corrupt-"+Guid.NewGuid().ToString("N"));warning="Odzyskano kampanię z kopii zapasowej.";return backup;
        }
    }
    public IEnumerable<(Guid Id,string Label)> List(Guid owner)
    {
        if(!Directory.Exists(directory))yield break;
        foreach(string file in Directory.EnumerateFiles(directory,"*.json").OrderByDescending(File.GetLastWriteTimeUtc))
        {
            CampaignSnapshot? value=null;
            try{value=CampaignSnapshotCodec.Decode(catalog,File.ReadAllText(file));}catch(Exception e)when(e is IOException or JsonException or InvalidDataException or NotSupportedException or ArgumentException or NullReferenceException){}
            if(value is null)
            {
                // Keep incompatible saves selectable so Resume can explain the failure without replacing them.
                bool belongs=false;
                try{using var raw=JsonDocument.Parse(File.ReadAllText(file));belongs=raw.RootElement.GetProperty("campaign").GetProperty("ownerProfileId").GetGuid()==owner;}
                catch(Exception e)when(e is IOException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException){}
                if(belongs&&Guid.TryParse(Path.GetFileNameWithoutExtension(file),out var id))yield return(id,$"{id.ToString()[..8]} • sprawdź zapis / wersję");
                continue;
            }
            if(value.OwnerProfileId==owner)yield return(value.CampaignId,$"{value.CampaignId.ToString()[..8]} • {value.CompletedRounds}/{value.Length} • {(value.CompletedRounds==value.Length?"ukończona":"garaż")}");
        }
    }
    private string PathFor(Guid id)=>Path.Combine(directory,id.ToString("D")+".json");
}

public sealed class DurableCampaign(CampaignState initial,Action<CampaignSnapshot>? save)
{
    public CampaignState Current {get;private set;}=initial;
    public bool SaveBlocked {get;private set;}
    public string Error {get;private set;}="";
    private CampaignState? pending;
    public bool Initialize()=>Write(Current,true);
    public bool Commit(Guid operation,Func<CampaignState,bool> change,bool retainOnFailure=false)
    {
        if(Current.HasOperation(operation))return true;
        if(SaveBlocked)return false;
        var staged=Current.Clone();if(!change(staged))return false;
        staged.MarkOperation(operation);return Write(staged,retainOnFailure);
    }
    public bool Retry()=>!SaveBlocked||Write(pending??Current,true);
    private bool Write(CampaignState value,bool retain)
    {
        try{save?.Invoke(value.Capture());Current=value;pending=null;SaveBlocked=false;Error="";return true;}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidDataException)
        {SaveBlocked=true;pending=retain?value:null;Error=e.Message;return false;}
    }
}

public sealed record CampaignChallenge(Guid CampaignId,Guid ParticipantId,Guid ProfileId,string Nonce);
public sealed class CampaignAuthentication
{
    private readonly Dictionary<int,CampaignChallenge> pending=[];
    public static string NewSecret()=>Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public static bool IsSecret(string? value)=>value is{Length:64}&&value.All(Uri.IsHexDigit);
    public CampaignChallenge Issue(int peer,Guid campaign,Guid participant,Guid profile)
    {var challenge=new CampaignChallenge(campaign,participant,profile,NewSecret());pending[peer]=challenge;return challenge;}
    public static string Proof(CampaignCredential credential,CampaignChallenge challenge)
    {
        if(credential.ParticipantId!=challenge.ParticipantId||!IsSecret(credential.Secret)||!IsSecret(challenge.Nonce))throw new ArgumentException("Nieprawidłowe uwierzytelnienie kampanii.");
        string message=$"{challenge.CampaignId:D}|{challenge.ParticipantId:D}|{challenge.ProfileId:D}|{challenge.Nonce}";
        return Convert.ToHexString(HMACSHA256.HashData(Convert.FromHexString(credential.Secret),Encoding.UTF8.GetBytes(message)));
    }
    public bool Verify(int peer,string proof,Func<CampaignChallenge,string?> secret)
    {
        if(!pending.Remove(peer,out var challenge)||!IsSecret(proof)||secret(challenge) is not{} key)return false;
        string expected=Proof(new(challenge.ParticipantId,key),challenge);
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected),Encoding.ASCII.GetBytes(proof));
    }
    public void Remove(int peer)=>pending.Remove(peer);
    public void Clear()=>pending.Clear();
}
