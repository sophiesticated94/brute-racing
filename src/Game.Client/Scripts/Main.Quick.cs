using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Game.Core;
using Godot;

namespace Game.Client;

public partial class Main
{
    private readonly System.Net.Http.HttpClient directoryHttp=new(){Timeout=TimeSpan.FromSeconds(5)};
    private readonly SemaphoreSlim queueMutation=new(1,1);
    private Task queueCancellation=Task.CompletedTask;
    private QueueTicket? queueTicket;
    private PlayerLoadoutDto? queuedBuild;
    private MatchOffer? quickOffer;
    private string directoryAddress="";
    private int queueGeneration;
    private bool queueBusy,queueAcknowledged,quickStartApproved;
    private double nextQueuePoll;
    private readonly Dictionary<int,Guid> quickPeers=[];
    private int quickOffers,quickRetries,quickConflicts;

    private async void StartQuick()
    {
        CancelQuick();LeaveInternal();gameMode=GameMode.Quick;SetBalance(LobbyBalanceMode.OpenMayhem);
        string url=arguments.GetValueOrDefault("directory",ui.DirectoryUrl.Text).Trim().TrimEnd('/');
        if(!Uri.TryCreate(url,UriKind.Absolute,out var endpoint)||endpoint.Scheme is not ("https" or "http"))
        {ui.Notice("Podaj adres katalogu quick. Kampania po IP działa bez katalogu.",8);return;}
        directoryAddress=url;int generation=queueGeneration;queueBusy=true;
        ui.QueueStatus("Sprawdzanie katalogu…");
        try
        {
            using var health=await directoryHttp.GetAsync(url+"/health");health.EnsureSuccessStatusCode();
            using var document=JsonDocument.Parse(await health.Content.ReadAsStringAsync());
            var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("protocol",out var version)||version.ValueKind!=JsonValueKind.Number||!version.TryGetInt32(out int protocol)||protocol!=4||
                !root.TryGetProperty("contentHash",out var hash)||hash.ValueKind!=JsonValueKind.String||hash.GetString()!=contentHash)
                throw new InvalidDataException("Katalog wymaga protokołu 4 i tej samej wersji danych gry.");
            var request=new QueueRequest(profile.PlayerId,CleanName(SelectedName()),RequestedLoadout(),(int)ui.Port.Value,contentHash,!arguments.ContainsKey("no-host"));
            if(!queueCancellation.IsCompleted)ui.QueueStatus("Kończenie poprzedniego wyszukiwania…");
            await queueCancellation;await queueMutation.WaitAsync();
            QueueTicket ticket;
            try
            {
                while(true)
                {
                    if(generation!=queueGeneration)return;
                    using var response=await directoryHttp.PostAsJsonAsync(url+"/queue",request,GameCatalog.Json);
                    if(response.StatusCode==System.Net.HttpStatusCode.Conflict)
                    {
                        quickConflicts++;
                        if(generation!=queueGeneration)return;
                        ui.QueueStatus("Poprzedni wpis jest aktywny. Oczekiwanie na jego wygaśnięcie…");
                        int delay=Math.Clamp((int)(response.Headers.RetryAfter?.Delta?.TotalSeconds??60),1,60);
                        for(int second=0;second<delay&&generation==queueGeneration;second++)await Task.Delay(1000);
                        continue;
                    }
                    response.EnsureSuccessStatusCode();
                    ticket=await response.Content.ReadFromJsonAsync<QueueTicket>(GameCatalog.Json)??throw new InvalidDataException("Pusta odpowiedź katalogu.");
                    if(generation!=queueGeneration){await DeleteTicket(url,ticket);return;}
                    break;
                }
            }
            finally{queueMutation.Release();}

            queuedBuild=request.Build with{SkillRanks=new(request.Build.SkillRanks,StringComparer.Ordinal)};
            queueTicket=ticket;autoStart=false;nextQueuePoll=now;ui.QueueStatus($"Szukanie rywali • fitness {ticket.Fitness:0}");
            GD.Print($"QUICK_QUEUED fitness={ticket.Fitness}");
        }
        catch(Exception e) when(e is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException)
        {if(generation==queueGeneration)ui.Notice("Katalog: "+e.Message,8);}
        finally{if(generation==queueGeneration)queueBusy=false;}
    }
    private void CancelQuick()
    {
        queueGeneration++;queueBusy=false;queueAcknowledged=quickStartApproved=false;quickOffer=null;quickPeers.Clear();
        var previous=queueTicket;queueTicket=null;queuedBuild=null;if(previous is not null)queueCancellation=CancelTicketAfter(queueCancellation,directoryAddress,previous);
    }
    private async Task CancelTicketAfter(Task previous,string url,QueueTicket ticket)
    {await previous;await queueMutation.WaitAsync();try{await DeleteTicket(url,ticket);}finally{queueMutation.Release();}}
    private async Task DeleteTicket(string url,QueueTicket ticket)
    {
        var deadline=DateTimeOffset.UtcNow.AddSeconds(65);
        do
        {
            int delay=1;
            try
            {
                using var request=new HttpRequestMessage(HttpMethod.Delete,$"{url}/queue/{ticket.Id}");request.Headers.Add("X-Queue-Secret",ticket.Secret);
                using var response=await directoryHttp.SendAsync(request);
                if(response.IsSuccessStatusCode||response.StatusCode==System.Net.HttpStatusCode.NotFound)return;
                delay=Math.Clamp((int)(response.Headers.RetryAfter?.Delta?.TotalSeconds??1),1,5);
            }
            catch(Exception e)when(e is HttpRequestException or TaskCanceledException){}
            await Task.Delay(delay*1000);
        }while(DateTimeOffset.UtcNow<deadline);
        // No heartbeat is sent to a cancelled ticket; an unreachable directory expires it.
    }
    private async Task QueueAction(string action,QueueTicket ticket,Guid offer)
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,$"{directoryAddress}/queue/{ticket.Id}/{action}?offer={offer}");
        request.Headers.Add("X-Queue-Secret",ticket.Secret);using var response=await directoryHttp.SendAsync(request);response.EnsureSuccessStatusCode();
    }
    private async void UpdateQuick()
    {
        if(queueTicket is null || queueBusy || now<nextQueuePoll || phase is RacePhase.Racing or RacePhase.Countdown or RacePhase.Podium)return;
        queueBusy=true;nextQueuePoll=now+1;int generation=queueGeneration;var ticket=queueTicket;
        try
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,$"{directoryAddress}/queue/{ticket.Id}");request.Headers.Add("X-Queue-Secret",ticket.Secret);
            using var response=await directoryHttp.SendAsync(request);
            if(response.StatusCode==System.Net.HttpStatusCode.NotFound){if(generation==queueGeneration)StartQuick();return;}
            response.EnsureSuccessStatusCode();
            var status=await response.Content.ReadFromJsonAsync<QueueStatus>(GameCatalog.Json)??throw new InvalidDataException("Pusta kolejka.");
            if(generation!=queueGeneration)return;
            if(status.Offer is null)
            {
                if(quickOffer is not null){LeaveInternal();quickOffer=null;quickPeers.Clear();queueAcknowledged=false;quickRetries++;ui.ShowMenu();}
                ui.QueueStatus($"Szukanie rywali • fitness {status.Fitness:0} • ±{status.Tolerance*100:0}% • {status.WaitSeconds:0} s");return;
            }
            if(quickOffer?.Id!=status.Offer.Id)
            {
                var selected=status.Offer;quickStartApproved=false;quickOffer=selected;quickOffers++;quickPeers.Clear();queueAcknowledged=false;
                if(quickOffer.HostTicket==ticket.Id)
                {
                    ui.Port.Value=quickOffer.HostPort;ui.Bots.Value=0;StartHosting();if(generation!=queueGeneration)return;expectedPlayers=selected.Members.Length;autoStart=false;
                    quickPeers[localId]=ticket.Id;
                }
                else{ui.Address.Text=quickOffer.HostAddress;ui.Port.Value=quickOffer.HostPort;JoinHost();if(generation!=queueGeneration)return;}
                GD.Print($"QUICK_OFFER id={quickOffer.Id} host={quickOffer.HostTicket==ticket.Id} players={quickOffer.Members.Length}");
            }
            if(!inSession){await QueueAction("fail",ticket,quickOffer.Id);return;}
            bool connected=isHost?quickPeers.Count==quickOffer.Members.Length:players.ContainsKey(localId);
            if(connected&&!queueAcknowledged)
            {await QueueAction("ready",ticket,quickOffer.Id);if(generation!=queueGeneration)return;queueAcknowledged=true;GD.Print("QUICK_ENET_CONFIRMED");}
            ui.QueueStatus($"Quick: połączenie ENet {players.Count}/{quickOffer.Members.Length}");
            if(status.State=="playing"&&isHost&&connected&&phase==RacePhase.Lobby)
            {
                foreach(var id in players.Keys.ToArray())players[id]=players[id] with{Ready=true};
                quickStartApproved=true;GD.Print("QUICK_START");TryStartRace();
            }
        }
        catch(Exception e) when(e is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException)
        {
            if(generation==queueGeneration){ui.Notice("Katalog: "+e.Message,5);nextQueuePoll=now+3;}
        }
        finally{if(generation==queueGeneration)queueBusy=false;}
    }
    private async void RetryQuick()
    {
        if(queueTicket is null)return;
        var ticket=queueTicket;var failedOffer=quickOffer?.Id??Guid.Empty;int generation=++queueGeneration;
        LeaveInternal();ui.ShowMenu();queueAcknowledged=quickStartApproved=false;queueBusy=true;quickOffer=null;quickPeers.Clear();
        try{await QueueAction("fail",ticket,failedOffer);}catch(Exception e)when(e is HttpRequestException or TaskCanceledException){}
        finally{if(generation==queueGeneration){queueBusy=false;quickRetries++;nextQueuePoll=now+1;ui.QueueStatus("Host nieosiągalny. Powrót do kolejki…");}}
    }
    private bool ValidateQuickHello(int sender,SessionMessage message)
    {
        if(gameMode!=GameMode.Quick)return true;
        var member=quickOffer?.Members.FirstOrDefault(m=>m.TicketId==message.TicketId);
        if(member is null || message.MatchId!=quickOffer!.Id || quickPeers.ContainsValue(member.TicketId) || message.Loadout is null)return false;
        var expected=member.Build;var actual=message.Loadout;
        bool valid=expected.CarId==actual.CarId&&expected.WeaponId==actual.WeaponId&&expected.WeaponLevel==actual.WeaponLevel&&expected.MineLevel==actual.MineLevel&&expected.PlayerLevel==actual.PlayerLevel&&expected.CosmeticId==actual.CosmeticId&&expected.SkillRanks.OrderBy(p=>p.Key).SequenceEqual(actual.SkillRanks.OrderBy(p=>p.Key));
        if(valid)quickPeers[sender]=member.TicketId;
        return valid;
    }
}
