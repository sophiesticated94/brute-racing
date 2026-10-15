using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Game.Core;

public sealed record ArchivedBuild(string Name,PlayerLoadoutDto Loadout,Guid CampaignId,DateTimeOffset EarnedAt);
public enum RewardKind { Race, Campaign }
public sealed record RewardReceipt(Guid Id,RewardKind Kind,int Coins=0,ArchivedBuild? Archive=null);
public sealed class PlayerProfile
{
    public int SchemaVersion {get;set;}=1;
    public Guid PlayerId {get;set;}=Guid.NewGuid();
    public string Name {get;set;}="Kierowca";
    public int Paint {get;set;}
    public int Coins {get;set;}
    public int SelectedPreset {get;set;}
    public string SelectedCosmetic {get;set;}="stock";
    public HashSet<string> OwnedCosmetics {get;set;}=new(StringComparer.Ordinal){"stock"};
    public ArchivedBuild[] Presets {get;set;}=new[]{"machine_gun","laser","rocket"}.Select((weapon,i)=>new ArchivedBuild($"Rook {i+1}",PlayerLoadoutDto.Default with {WeaponId=weapon},Guid.Empty,DateTimeOffset.UnixEpoch)).ToArray();
    public ArchivedBuild? PendingArchive {get;set;}
    public HashSet<Guid> AppliedRewards {get;set;}=[];
    public int CompletedRaces {get;set;}
    public int CompletedCampaigns {get;set;}
}

public static class ProfileRules
{
    public static void Validate(GameCatalog catalog,PlayerProfile profile)
    {
        if(profile.SchemaVersion!=1 || profile.PlayerId==Guid.Empty || profile.Name is null || profile.Name.Length>20 || profile.Paint is <0 or >7 || profile.Coins is <0 or >1000000 ||
            profile.SelectedPreset is <0 or >2 || profile.Presets is not {Length:3} || profile.OwnedCosmetics is null || profile.AppliedRewards is null ||
            profile.CompletedCampaigns<0 || profile.CompletedRaces<0 || !profile.OwnedCosmetics.Contains(profile.SelectedCosmetic) || profile.OwnedCosmetics.Any(id=>!catalog.Cosmetics.ContainsKey(id)))
            throw new InvalidDataException("Nieprawidłowy profil.");
        var validator=new BuildValidator(catalog);
        if(profile.Presets.Any(p=>p is null || p.Name is null || p.Name.Length>60 || !validator.IsValid(p.Loadout)) ||
            profile.PendingArchive is {} pending && !validator.IsValid(pending.Loadout)) throw new InvalidDataException("Nieprawidłowe archiwum buildu.");
    }
    public static bool ApplyReward(GameCatalog catalog,PlayerProfile profile,RewardReceipt receipt)
    {
        if(receipt.Id==Guid.Empty || !Enum.IsDefined(receipt.Kind) || receipt.Coins is <0 or >10000 ||
            receipt.Archive is {} archive && !new BuildValidator(catalog).IsValid(archive.Loadout)) throw new InvalidDataException("Nieprawidłowa nagroda.");
        if(profile.AppliedRewards.Contains(receipt.Id)) return false;
        profile.Coins=checked(profile.Coins+receipt.Coins);
        if(profile.Coins>1000000) profile.Coins=1000000;
        profile.AppliedRewards.Add(receipt.Id);
        if(receipt.Kind==RewardKind.Race) profile.CompletedRaces++;
        else {profile.CompletedCampaigns++;if(receipt.Archive is not null) profile.PendingArchive=receipt.Archive;}
        return true;
    }
    public static bool BuyCosmetic(GameCatalog catalog,PlayerProfile profile,string id)
    {
        if(!catalog.Cosmetics.TryGetValue(id,out var item) || profile.OwnedCosmetics.Contains(id) || profile.Coins<item.Price) return false;
        profile.Coins-=item.Price; profile.OwnedCosmetics.Add(id);profile.SelectedCosmetic=id;return true;
    }
    public static void Archive(PlayerProfile profile,int slot)
    {
        if(slot is <0 or >2 || profile.PendingArchive is null) throw new InvalidOperationException("Brak końcowego buildu kampanii.");
        profile.Presets[slot]=profile.PendingArchive;profile.SelectedPreset=slot;
    }
}

public sealed record ProfileEnvelope(int SchemaVersion,PlayerProfile Profile,string Checksum);
public static class ProfileCodec
{
    public static string Encode(GameCatalog catalog,PlayerProfile profile)
    {
        ProfileRules.Validate(catalog,profile);
        return JsonSerializer.Serialize(new ProfileEnvelope(1,profile,Checksum(profile)),GameCatalog.Json);
    }
    public static PlayerProfile Decode(GameCatalog catalog,string json)
    {
        var envelope=JsonSerializer.Deserialize<ProfileEnvelope>(json,GameCatalog.Json)??throw new InvalidDataException("Pusty profil.");
        if(envelope.SchemaVersion!=1) throw new NotSupportedException("Nowszy format profilu. Plik pozostawiono bez zmian.");
        if(envelope.Profile is null || envelope.Checksum!=Checksum(envelope.Profile)) throw new InvalidDataException("Nieprawidłowa suma kontrolna profilu.");
        ProfileRules.Validate(catalog,envelope.Profile);return envelope.Profile;
    }
    private static string Checksum(PlayerProfile profile)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(profile,GameCatalog.Json))));
}

public sealed class ProfileRepository(GameCatalog catalog,string path)
{
    public bool ReadOnly {get;private set;}
    public PlayerProfile Load(out string warning)
    {
        warning="";
        if(!File.Exists(path)) return new();
        try{return ProfileCodec.Decode(catalog,File.ReadAllText(path));}
        catch(NotSupportedException e){ReadOnly=true;warning=e.Message;return new();}
        catch(Exception e) when(e is IOException or InvalidDataException or JsonException or ArgumentException or NullReferenceException)
        {
            try
            {
                var backup=ProfileCodec.Decode(catalog,File.ReadAllText(path+".bak"));
                File.Move(path,path+".corrupt-"+Guid.NewGuid().ToString("N"));warning="Odzyskano profil z kopii zapasowej.";return backup;
            }
            catch(Exception backupError) when(backupError is IOException or InvalidDataException or JsonException or ArgumentException or NotSupportedException or NullReferenceException)
            {
                File.Move(path,path+".corrupt-"+Guid.NewGuid().ToString("N"));warning="Uszkodzony profil zachowano osobno. Utworzono nowy.";return new();
            }
        }
    }
    public void Save(PlayerProfile profile)
    {
        if(ReadOnly) return;
        string content=ProfileCodec.Encode(catalog,profile); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp=path+".tmp-"+Guid.NewGuid().ToString("N");
        try
        {
            using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {var bytes=Encoding.UTF8.GetBytes(content);stream.Write(bytes);stream.Flush(true);}
            if(File.Exists(path))File.Replace(temp,path,path+".bak",true);else File.Move(temp,path);
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
