using Game.Core;
using Game.DirectoryApi;

namespace Game.Core.Tests;

public class MatchmakingTests
{
    private sealed class ManualClock:TimeProvider
    {
        public DateTimeOffset Now=DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow()=>Now;
        public void Advance(int seconds)=>Now=Now.AddSeconds(seconds);
    }
    private static QueueRequest Request(GameCatalog catalog,int level=1,bool host=true)=>new(Guid.NewGuid(),"Driver",PlayerLoadoutDto.Default with{WeaponLevel=level},29890,catalog.ContentHash,host);
    [Fact]
    public void FitnessUsesStatsAndPerksAndAllFiveArchetypesValidate()
    {
        var catalog=DomainTests.Catalog();
        foreach(string weapon in new[]{"machine_gun","laser","rocket"})
        {
            float last=0;
            for(int level=1;level<=5;level++)
            {float current=FitnessCalculator.Calculate(catalog,PlayerLoadoutDto.Default with{WeaponId=weapon,WeaponLevel=level,MineLevel=level}).Total;Assert.True(current>last);last=current;}
        }
        Assert.Equal(5,catalog.BotArchetypes.Length);
        foreach(var bot in catalog.BotArchetypes)
        {
            var result=new BuildValidator(catalog).Validate(LoadoutDeclaration.FromLegacy(bot.Loadout),LobbyBalanceRules.FairCap);
            Assert.True(result.Valid);Assert.True(result.SpentPoints<=15);Assert.Equal(3,result.Effective!.PrimaryLevel);
        }
    }
    [Fact]
    public void FourPlayersGetAnOfferButOnlyAllEnetAcknowledgementsStartTheMatch()
    {
        var catalog=DomainTests.Catalog();var clock=new ManualClock();var queue=new MatchQueue(catalog,clock);
        var tickets=Enumerable.Range(0,4).Select(_=>queue.Enqueue(Request(catalog),"127.0.0.1")).ToArray();
        var offer=queue.Poll(tickets[0].Id,tickets[0].Secret).Offer!;Assert.Equal(4,offer.Members.Length);
        foreach(var ticket in tickets.Take(3))queue.Ready(ticket.Id,ticket.Secret);
        Assert.Equal("offer",queue.Poll(tickets[0].Id,tickets[0].Secret).State);
        queue.Ready(tickets[3].Id,tickets[3].Secret);
        Assert.Equal("playing",queue.Poll(tickets[0].Id,tickets[0].Secret).State);
    }
    [Fact]
    public void TwoPlayersWaitFifteenSecondsAndFailedHostIsReplaced()
    {
        var catalog=DomainTests.Catalog();var clock=new ManualClock();var queue=new MatchQueue(catalog,clock);
        var a=queue.Enqueue(Request(catalog),"127.0.0.1");var b=queue.Enqueue(Request(catalog),"127.0.0.1");
        Assert.Null(queue.Poll(a.Id,a.Secret).Offer);clock.Advance(15);
        var offer=queue.Poll(a.Id,a.Secret).Offer!;Assert.Equal(2,offer.Members.Length);
        queue.Fail(a.Id,a.Secret);var replacement=queue.Poll(a.Id,a.Secret).Offer!;
        Assert.NotEqual(offer.HostTicket,replacement.HostTicket);
        queue.Cancel(b.Id,b.Secret);Assert.Null(queue.Poll(a.Id,a.Secret).Offer);
    }
    [Fact]
    public void EntriesAndOffersExpireAndSecretsCannotBeForged()
    {
        var catalog=DomainTests.Catalog();var clock=new ManualClock();var queue=new MatchQueue(catalog,clock);
        var a=queue.Enqueue(Request(catalog),"127.0.0.1");var b=queue.Enqueue(Request(catalog),"127.0.0.1");
        Assert.Throws<KeyNotFoundException>(()=>queue.Poll(a.Id,"forged"));clock.Advance(15);
        var offer=queue.Poll(a.Id,a.Secret).Offer!;clock.Advance(13);
        Assert.NotEqual(offer.Id,queue.Poll(a.Id,a.Secret).Offer?.Id);
        clock.Advance(61);Assert.Throws<KeyNotFoundException>(()=>queue.Poll(b.Id,b.Secret));
    }
    [Fact]
    public void StaleFailureCannotCancelAReplacementOffer()
    {
        var catalog=DomainTests.Catalog();var clock=new ManualClock();var queue=new MatchQueue(catalog,clock);
        var a=queue.Enqueue(Request(catalog),"127.0.0.1");var b=queue.Enqueue(Request(catalog),"127.0.0.1");clock.Advance(15);
        var old=queue.Poll(a.Id,a.Secret).Offer!;queue.Fail(a.Id,a.Secret,old.Id);
        var replacement=queue.Poll(a.Id,a.Secret).Offer!;
        queue.Fail(b.Id,b.Secret,old.Id);
        Assert.Equal(replacement.Id,queue.Poll(a.Id,a.Secret).Offer!.Id);
        Assert.Throws<InvalidOperationException>(()=>queue.Ready(a.Id,a.Secret,old.Id));
    }
    [Fact]
    public void QueueRejectsInvalidBuildsContentAndUntrustedAddresses()
    {
        var catalog=DomainTests.Catalog();var queue=new MatchQueue(catalog,new ManualClock());
        Assert.Throws<ArgumentException>(()=>queue.Enqueue(Request(catalog) with{Build=PlayerLoadoutDto.Default with{WeaponLevel=99}},"127.0.0.1"));
        Assert.Throws<ArgumentException>(()=>queue.Enqueue(Request(catalog) with{ContentHash="fake"},"127.0.0.1"));
        Assert.Throws<ArgumentException>(()=>queue.Enqueue(Request(catalog) with{AdvertisedAddress="1.2.3.4"},"5.6.7.8"));
    }
    [Fact]
    public void ToleranceWidensAndNeverExceedsTwentyFivePercent()
    {
        var catalog=DomainTests.Catalog();var clock=new ManualClock();var queue=new MatchQueue(catalog,clock);
        var a=queue.Enqueue(Request(catalog),"127.0.0.1");
        Assert.Equal(.1f,queue.Poll(a.Id,a.Secret).Tolerance);clock.Advance(10);
        Assert.Equal(.15f,queue.Poll(a.Id,a.Secret).Tolerance);clock.Advance(25);
        Assert.Equal(.25f,queue.Poll(a.Id,a.Secret).Tolerance);
    }
    [Fact]
    public void DuplicateClientCannotReplaceTicketOrInvalidateOffer()
    {
        var catalog=DomainTests.Catalog();var clock=new ManualClock();var queue=new MatchQueue(catalog,clock);
        var request=Request(catalog);var ticket=queue.Enqueue(request,"127.0.0.1");
        var other=queue.Enqueue(Request(catalog),"127.0.0.1");clock.Advance(15);
        var before=queue.Poll(ticket.Id,ticket.Secret);
        Assert.Throws<QueueConflictException>(()=>queue.Enqueue(request with{Name="Attacker"},"127.0.0.1"));
        Assert.Equal(before.Offer,queue.Poll(ticket.Id,ticket.Secret).Offer);
        Assert.Equal(before.Offer,queue.Poll(other.Id,other.Secret).Offer);
        queue.Cancel(ticket.Id,ticket.Secret);
        Assert.NotEqual(ticket.Id,queue.Enqueue(request,"127.0.0.1").Id);
    }
    [Fact]
    public void IpBucketsAllowEightClientsAndRefillWithoutStateChangesOnRejection()
    {
        var clock=new ManualClock();var limiter=new RequestLimiter(clock,new());
        for(int i=0;i<8;i++)Assert.True(limiter.Accept("nat",true,out _));
        Assert.False(limiter.Accept("nat",true,out int retry));Assert.Equal(1,retry);
        for(int i=0;i<100;i++)Assert.True(limiter.Accept("nat",false,out _));
        clock.Advance(1);Assert.True(limiter.Accept("nat",true,out _));
        for(int i=0;i<8;i++)Assert.True(limiter.Accept("other",true,out _));
        for(int i=0;i<200;i++)limiter.Accept("nat",false,out _);
        Assert.False(limiter.Accept("nat",false,out _));clock.Advance(6);Assert.True(limiter.Accept("nat",false,out _));
    }
    [Fact]
    public void FullQueueRejectsTheNextEntryAndRetainsExistingTickets()
    {
        var catalog=DomainTests.Catalog();var queue=new MatchQueue(catalog,new ManualClock());
        var tickets=Enumerable.Range(0,1024).Select(_=>queue.Enqueue(Request(catalog),"127.0.0.1")).ToArray();
        Assert.Throws<QueueCapacityException>(()=>queue.Enqueue(Request(catalog),"127.0.0.1"));
        Assert.Equal("offer",queue.Poll(tickets[0].Id,tickets[0].Secret).State);
    }
    [Theory]
    [InlineData("127.0.0.1",28747)]
    [InlineData("2001:db8::1",65535)]
    public void ConnectionCodesRoundtripAndDetectTypos(string ip,int port)
    {
        var code=ConnectionCode.Encode(ip,port);Assert.True(ConnectionCode.TryDecode(code,out string decoded,out int decodedPort));
        Assert.Equal(ip,decoded);Assert.Equal(port,decodedPort);Assert.False(ConnectionCode.TryDecode(code+"x",out _,out _));
    }
}
