using System.Net;
using System.Security.Cryptography;
using Game.Core;

namespace Game.DirectoryApi;

/// <summary>One process owns the queue. No client-reported fitness or success is trusted as build evidence.</summary>
public sealed class MatchQueue(GameCatalog catalog, TimeProvider time)
{
    private sealed class Entry(QueueTicket ticket,QueueRequest request,string address,DateTimeOffset now)
    {
        public QueueTicket Ticket=ticket;
        public QueueRequest Request=request;
        public string Address=address;
        public DateTimeOffset Joined=now,Heartbeat=now,HostBlockedUntil;
        public MatchOffer? Offer;
        public bool Ready,Playing;
    }
    private readonly object gate=new();
    private readonly Dictionary<Guid,Entry> entries=[];
    private readonly Dictionary<Guid,DateTimeOffset> offers=[];
    public static readonly TimeSpan EntryTtl=TimeSpan.FromSeconds(60),OfferTtl=TimeSpan.FromSeconds(12);
    public string ContentHash {get;}=catalog.ContentHash;
    public QueueTicket Enqueue(QueueRequest request,string remoteAddress)
    {
        if(request.ClientId==Guid.Empty || request.Name is null || request.Name.Length is <1 or >20 || request.Port is <1024 or >65535 || request.ContentHash!=ContentHash || !new BuildValidator(catalog).IsValid(request.Build))throw new ArgumentException("Nieprawidłowy build, protokół lub parametry kolejki.");
        string address=request.AdvertisedAddress??remoteAddress;
        if(!IPAddress.TryParse(address,out var ip)||IPAddress.Any.Equals(ip)||IPAddress.IPv6Any.Equals(ip))throw new ArgumentException("Nieprawidłowy adres hosta.");
        // A remote client cannot advertise somebody else's address. LAN override is restricted to loopback callers.
        if(!IPAddress.TryParse(remoteAddress,out var remote)||!IPAddress.IsLoopback(remote)&&!remote.Equals(ip))throw new ArgumentException("Adres hosta różni się od adresu nadawcy.");
        if(ip.IsIPv4MappedToIPv6)ip=ip.MapToIPv4();
        float fitness=FitnessCalculator.Calculate(catalog,request.Build).Total;
        lock(gate)
        {
            Sweep();
            foreach(var old in entries.Values.Where(e=>e.Request.ClientId==request.ClientId).ToArray())Remove(old);
            if(entries.Count>=1024)throw new InvalidOperationException("Kolejka pełna.");
            var ticket=new QueueTicket(Guid.NewGuid(),Convert.ToHexString(RandomNumberGenerator.GetBytes(24)),fitness);
            entries.Add(ticket.Id,new(ticket,request,ip.ToString(),time.GetUtcNow()));return ticket;
        }
    }
    public QueueStatus Poll(Guid id,string secret)
    {
        lock(gate)
        {
            Sweep();var own=Find(id,secret);own.Heartbeat=time.GetUtcNow();Match();
            return new(own.Playing?"playing":own.Offer is null?"queued":"offer",own.Ticket.Fitness,(time.GetUtcNow()-own.Joined).TotalSeconds,Tolerance(own),own.Offer);
        }
    }
    public void Ready(Guid id,string secret,Guid? expectedOffer=null)
    {
        lock(gate)
        {
            Sweep();var own=Find(id,secret);own.Heartbeat=time.GetUtcNow();
            if(own.Offer is null || expectedOffer is {} expected && own.Offer.Id!=expected)throw new InvalidOperationException("Oferta wygasła.");
            own.Ready=true;
            var group=Members(own.Offer.Id);
            if(group.Length==own.Offer.Members.Length && group.All(e=>e.Ready))
            {foreach(var e in group)e.Playing=true;offers.Remove(own.Offer.Id);}
        }
    }
    public void Fail(Guid id,string secret,Guid? expectedOffer=null)
    {
        lock(gate){Sweep();var own=Find(id,secret);if(own.Offer is {} offer && (expectedOffer is null || expectedOffer==offer.Id))Requeue(offer.Id);own.Heartbeat=time.GetUtcNow();}
    }
    public void Cancel(Guid id,string secret)
    {lock(gate){Sweep();Remove(Find(id,secret));}}
    public void Sweep()
    {
        lock(gate)
        {
            var now=time.GetUtcNow();
            foreach(var entry in entries.Values.Where(e=>now-e.Heartbeat>EntryTtl).ToArray())Remove(entry);
            foreach(var offer in offers.Where(p=>now-p.Value>OfferTtl).Select(p=>p.Key).ToArray())Requeue(offer);
        }
    }
    private Entry Find(Guid id,string secret)
    {
        if(!entries.TryGetValue(id,out var entry)||secret is null||secret.Length!=entry.Ticket.Secret.Length||!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(secret),System.Text.Encoding.ASCII.GetBytes(entry.Ticket.Secret)))throw new KeyNotFoundException("Wpis kolejki wygasł.");
        return entry;
    }
    private Entry[] Members(Guid offer)=>entries.Values.Where(e=>e.Offer?.Id==offer).ToArray();
    private void Remove(Entry entry)
    {
        if(entry.Offer is {} offer && !entry.Playing)Requeue(offer.Id);
        entries.Remove(entry.Ticket.Id);
    }
    private void Requeue(Guid offerId)
    {
        foreach(var entry in Members(offerId))
        {
            if(entry.Offer!.HostTicket==entry.Ticket.Id)entry.HostBlockedUntil=time.GetUtcNow().AddSeconds(30);
            entry.Offer=null;entry.Ready=entry.Playing=false;
        }
        offers.Remove(offerId);
    }
    private float Tolerance(Entry entry)=>Math.Min(.25f,.1f+.05f*(int)((time.GetUtcNow()-entry.Joined).TotalSeconds/10));
    private void Match()
    {
        foreach(var seed in entries.Values.Where(e=>e.Offer is null).OrderBy(e=>e.Joined).ThenBy(e=>e.Ticket.Id).ToArray())
        {
            if(seed.Offer is not null)continue;
            var candidates=entries.Values.Where(e=>e.Offer is null && Math.Abs(e.Ticket.Fitness-seed.Ticket.Fitness)/Math.Max(seed.Ticket.Fitness,e.Ticket.Fitness)<=Math.Min(Tolerance(seed),Tolerance(e))).OrderBy(e=>e.Joined).ThenBy(e=>e.Ticket.Id).Take(8).ToArray();
            if(candidates.Length<2 || candidates.Length<4 && (time.GetUtcNow()-seed.Joined).TotalSeconds<15)continue;
            var host=candidates.FirstOrDefault(e=>e.Request.CanHost && e.HostBlockedUntil<=time.GetUtcNow());
            if(host is null)continue;
            var offer=new MatchOffer(Guid.NewGuid(),host.Ticket.Id,host.Address,host.Request.Port,candidates.Select(e=>new QueueMember(e.Ticket.Id,e.Request.Name,e.Request.Build)).ToArray());
            offers.Add(offer.Id,time.GetUtcNow());
            foreach(var entry in candidates){entry.Offer=offer;entry.Ready=false;}
        }
    }
}
