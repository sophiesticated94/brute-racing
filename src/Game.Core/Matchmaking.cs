using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Game.Core;

public sealed record FitnessWeights(Dictionary<string,float> Stats, Dictionary<string,float> Perks);
public sealed record FitnessResult(float Total, Dictionary<string,float> Components);
public static class FitnessCalculator
{
    public static FitnessResult Calculate(GameCatalog catalog, PlayerLoadoutDto declaration)
    {
        var build=VehicleStatsCalculator.CalculateBuild(catalog,declaration);
        var values=new Dictionary<string,float>(StringComparer.Ordinal);
        foreach(var pair in catalog.Fitness.Stats.OrderBy(p=>p.Key,StringComparer.Ordinal))
        {
            var parts=pair.Key.Split('.');if(parts.Length!=2)throw new InvalidDataException("Fitness stat.");object target=parts[0] switch{"car"=>build.Vehicle,"primary"=>build.Primary,"mines"=>build.Mines,"mechanics"=>build.Mechanics,_=>throw new InvalidDataException("Fitness target.")};
            var property=target.GetType().GetProperty(parts[1])??throw new InvalidDataException("Fitness stat.");
            float value=Convert.ToSingle(property.GetValue(target),System.Globalization.CultureInfo.InvariantCulture);
            values[pair.Key]=value*pair.Value;
        }
        values["primary.dps"]=build.Primary.Damage*build.Primary.FireRate*build.Primary.ProjectileCount*catalog.Fitness.Perks["Dps"];
        values["mines.dps"]=build.Mines.Damage*build.Mines.FireRate*catalog.Fitness.Perks["MineDps"];
        foreach(var flag in Enum.GetValues<WeaponPerkFlags>().Where(f=>f!=0))
            if((build.Primary.Perks|build.Mines.Perks).HasFlag(flag))values[flag.ToString()]=catalog.Fitness.Perks.GetValueOrDefault(flag.ToString());
        foreach(var flag in Enum.GetValues<MechanicsFlags>().Where(f=>f!=0))
            if(build.Mechanics.Flags.HasFlag(flag))values[flag.ToString()]=catalog.Fitness.Perks.GetValueOrDefault(flag.ToString());
        float total=MathF.Round(MathF.Max(1,values.Values.Sum()),2);
        if(!float.IsFinite(total))throw new InvalidDataException("Nonfinite fitness.");
        return new(total,values);
    }
}
public sealed record BotArchetype(string Id,string Name,PlayerLoadoutDto Loadout);

public static class ConnectionCode
{
    public static string Encode(string address,int port)
    {
        if(!IPAddress.TryParse(address,out var ip)||port is <1 or >65535)throw new ArgumentException("Nieprawidłowy adres IP lub port.");
        string payload=$"1|{ip}|{port}";
        string checksum=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))[..8];
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload+"|"+checksum)).TrimEnd('=').Replace('+','-').Replace('/','_');
    }
    public static bool TryDecode(string code,out string address,out int port)
    {
        address="";port=0;
        try
        {
            string b64=code.Trim().Replace('-','+').Replace('_','/');b64=b64.PadRight((b64.Length+3)/4*4,'=');
            var parts=Encoding.UTF8.GetString(Convert.FromBase64String(b64)).Split('|');
            if(parts.Length!=4||parts[0]!="1"||!IPAddress.TryParse(parts[1],out var ip)||!int.TryParse(parts[2],out port)||port is <1 or >65535)return false;
            string checksum=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|",parts.Take(3)))))[..8];
            if(!string.Equals(checksum,parts[3],StringComparison.Ordinal))return false;
            address=ip.ToString();return true;
        }
        catch(FormatException){return false;}
    }
}

public sealed record QueueRequest(Guid ClientId,string Name,PlayerLoadoutDto Build,int Port,string ContentHash,bool CanHost=true,string? AdvertisedAddress=null);
public sealed record QueueTicket(Guid Id,string Secret,float Fitness);
public sealed record QueueMember(Guid TicketId,string Name,PlayerLoadoutDto Build);
public sealed record MatchOffer(Guid Id,Guid HostTicket,string HostAddress,int HostPort,QueueMember[] Members);
public sealed record QueueStatus(string State,float Fitness,double WaitSeconds,float Tolerance,MatchOffer? Offer=null,string Message="");
public sealed record QueueAuth(string Secret);
