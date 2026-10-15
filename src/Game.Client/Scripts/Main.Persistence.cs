using Game.Core;
using Godot;

namespace Game.Client;

public partial class Main
{
    private CampaignRepository? campaignRepository;
    private DurableCampaign? durableCampaign;
    private readonly CampaignAuthentication campaignAuth=new();
    private readonly HashSet<int> campaignIdentityReady=[];
    private readonly Dictionary<int,SessionMessage> pendingCampaignHellos=[];
    private SessionMessage? pendingIdentity;
    private double nextCampaignRetry,nextRewardDelivery;
    private bool pendingRaceFinalization;
    private void InitializeCampaignStorage()
    {
        if(profileRepository is not null || arguments.ContainsKey("campaign-dir"))
        {
            string directory=arguments.GetValueOrDefault("campaign-dir",arguments.TryGetValue("profile-path",out var file)?Path.Combine(Path.GetDirectoryName(Path.GetFullPath(file))!,"campaigns"):ProjectSettings.GlobalizePath("user://campaigns"));
            campaignRepository=new(catalog,directory);
        }
        ui.ResumeRequested+=ResumeCampaign;
        ui.SetCampaignSaves(campaignRepository?.List(profile.PlayerId).ToArray()??[]);
    }
    private bool CommitCampaign(Guid operation,Func<CampaignState,bool> change,bool retain=false)
    {
        if(durableCampaign is null)return false;
        bool committed=durableCampaign.Commit(operation,change,retain);campaign=durableCampaign.Current;
        if(durableCampaign.SaveBlocked)ui.Notice("Zapis kampanii nie powiódł się. Start zablokowany; ponawiam zapis.",5);
        return committed;
    }
    private bool CampaignCanStart()=>campaign is null || !campaign.Complete && durableCampaign?.SaveBlocked==false && campaign.Players.All(p=>p.Value.IsBot || p.Value.IdentityConfirmed && (players.GetValueOrDefault(p.Key)?.Bot==true || campaignIdentityReady.Contains(p.Key)));
    private bool CampaignRewardsReceived()
    {
        var view=campaign?.View()??campaignView;
        return view is null || dedicated || !view.Players.Any(p=>p.Id==localId) || profile.AppliedRewards.Contains(raceSessionId)&&(!view.Complete||profile.AppliedRewards.Contains(view.Id));
    }
    private void TickCampaignPersistence()
    {
        if(pendingIdentity is not null && now>=nextCampaignRetry){nextCampaignRetry=now+1;AcceptCampaignIdentity(pendingIdentity);}
        if(!isHost || campaign is null)return;
        if(durableCampaign?.SaveBlocked==true && now>=nextCampaignRetry)
        {
            nextCampaignRetry=now+1;
            if(durableCampaign.Retry()){campaign=durableCampaign.Current;SynchronizeCampaign();if(pendingRaceFinalization)FinishRace();}
        }
        if(!dedicated && !campaignIdentityReady.Contains(localId) && campaign.Players.ContainsKey(localId) && now>=nextCampaignRetry)
        {
            nextCampaignRetry=now+1;
            if(SaveProfile()&&CommitCampaign(Guid.NewGuid(),s=>s.Confirm(localId)))campaignIdentityReady.Add(localId);
        }
        if(now<nextRewardDelivery || durableCampaign?.SaveBlocked==true || pendingRaceFinalization)return;
        nextRewardDelivery=now+1;
        foreach(var reward in campaign.PendingRewards.ToArray())
        {
            var own=campaign.Players.FirstOrDefault(p=>p.Value.ParticipantId==reward.ParticipantId);
            if(own.Value is not null && campaignIdentityReady.Contains(own.Key))DeliverReward(own.Key,reward.Receipt);
        }
    }
    private void AcknowledgeCampaignReward(int peer,Guid reward)
    {if(campaignIdentityReady.Contains(peer))CommitCampaign(Guid.NewGuid(),s=>s.Acknowledge(peer,reward));}
    private void ResumeCampaign(Guid id)
    {
        try
        {
            if(campaignRepository is null)throw new InvalidDataException("Brak katalogu zapisów kampanii.");
            var snapshot=campaignRepository.Load(id,out string warning);
            if(snapshot.OwnerProfileId!=profile.PlayerId)throw new InvalidDataException("Ten profil nie jest gospodarzem zapisanej kampanii.");
            var restored=CampaignState.Restore(catalog,snapshot);
            var own=restored.FindProfile(profile.PlayerId);
            if(!dedicated && (own is null || !profile.CampaignCredentials.TryGetValue(id,out var credential) || credential.ParticipantId!=own.ParticipantId || credential.Secret!=restored.Credential(own.ParticipantId).Secret))throw new InvalidDataException("Profil nie zawiera tokenu powrotu do tej kampanii.");
            LeaveInternal();gameMode=GameMode.Campaign;balanceRules=snapshot.Rules;campaign=restored;
            campaign.BindRestored(profile.PlayerId,dedicated);durableCampaign=new(campaign,campaignRepository.Save);
            inSession=isHost=true;localId=dedicated?0:1;
            foreach(var p in campaign.Players.Values)players[p.Id]=new(p.Id,p.Name,p.Color,p.Id!=localId||autoPlay,p.Id!=localId,p.Loadout,p.ParticipantId);
            if(!dedicated)campaignIdentityReady.Add(localId);
            transport=CreateTransport();transport.Host((int)ui.Port.Value,dedicated?9:8);
            requiredRounds=campaign.Length-campaign.CompletedRounds;completedRaces=0;connectionStart=now;autoStartAt=now+2;
            PublishLobby();SyncSessionState();ui.Notice(warning.Length>0?warning:"Kampania wznowiona w garażu. Przerwana runda rozpocznie się od nowa.",8);
            GD.Print($"CAMPAIGN_RESUMED id={campaign.Id} revision={campaign.Revision} rounds={campaign.CompletedRounds}");
            GD.Print($"HOST_READY port={(int)ui.Port.Value}");
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or System.Text.Json.JsonException)
        {ui.Notice("Nie można wznowić kampanii: "+e.Message,12);if(smoke){GD.PushError(e.Message);GetTree().Quit(3);}}
    }
    private void HandleCampaignHello(int sender,SessionMessage message)
    {
        if(campaign is null || message.ProfileId==Guid.Empty){transport?.Disconnect(sender);return;}
        var existing=campaign.FindProfile(message.ProfileId);
        if(existing is not null)
        {
            if(players.TryGetValue(existing.Id,out var active)&&!active.Bot){RejectPeer(sender,"Miejsce tego profilu jest już aktywne.");return;}
            pendingCampaignHellos[sender]=message;
            SendControl(sender,new("campaign_challenge",Challenge:campaignAuth.Issue(sender,campaign.Id,existing.ParticipantId,message.ProfileId)));return;
        }
        if(campaign.CompletedRounds>0){RejectPeer(sender,"Ta kampania przyjmuje wyłącznie powracających uczestników.");return;}
        var roster=players.Values.ToList();
        if(roster.Count>=8)
        {
            var bot=roster.FirstOrDefault(p=>p.Bot&&campaign.Players[p.Id].IsBot);
            if(bot is null){transport?.Disconnect(sender);return;}roster.Remove(bot);
        }
        roster.Add(new(sender,CleanName(message.Name),message.Color,false,false,message.Loadout!));
        CampaignCredential? credential=null;
        if(!CommitCampaign(Guid.NewGuid(),s=>{s.Synchronize(roster.Select(p=>(p.Id,p.Loadout)));s.Describe(sender,CleanName(message.Name),message.Color,false);credential=s.Register(sender,message.ProfileId);return true;}))
        {RejectPeer(sender,"Nie udało się utrwalić dołączenia do kampanii.");return;}
        players.Clear();foreach(var p in roster)players[p.Id]=p;
        acceptedHello[sender]=now;autoStartAt=Math.Max(autoStartAt,now+.5);PublishLobby();
        SendControl(sender,new("campaign_welcome",CampaignId:campaign.Id,Credential:credential,Revision:campaign.Revision));
        GD.Print($"PLAYER_JOINED id={sender} count={players.Count}");
    }
    private void AttachCampaignPeer(int sender,SessionMessage hello,Guid participant,bool welcome)
    {
        if(campaign is null || !campaign.Participants.TryGetValue(participant,out var member))return;
        if(players.TryGetValue(member.Id,out var active)&&!active.Bot){transport?.Disconnect(sender);return;}
        int old=member.Id;
        if(!CommitCampaign(Guid.NewGuid(),s=>{s.Bind(participant,sender);return true;})){transport?.Disconnect(sender);return;}
        players.Remove(old);players[sender]=new(sender,member.Name,member.Color,false,false,member.Loadout,participant);
        acceptedHello[sender]=now;autoStartAt=Math.Max(autoStartAt,now+.5);PublishLobby();
        SendControl(sender,new(welcome?"campaign_welcome":"campaign_attached",CampaignId:campaign.Id,Credential:welcome?campaign.Credential(participant):null,ParticipantId:participant,Revision:campaign.Revision));
        GD.Print($"CAMPAIGN_REJOIN peer={sender} participant={participant}");
    }
    private void AcceptCampaignIdentity(SessionMessage message)
    {
        if(message.Credential is not null)profile.CampaignCredentials[message.CampaignId]=message.Credential;
        pendingIdentity=message;
        if(!SaveProfile())return;
        pendingIdentity=null;SendControl(1,new("campaign_identity_ack",CampaignId:message.CampaignId,RequestId:Guid.NewGuid()));
    }
    private bool HandleCampaignControl(int sender,SessionMessage message)
    {
        if(isHost)
        {
            if(message.Kind=="campaign_proof")
            {
                if(phase!=RacePhase.Lobby || campaign is null || !pendingCampaignHellos.Remove(sender,out var hello))return true;
                var member=campaign.FindProfile(hello.ProfileId);
                bool valid=campaignAuth.Verify(sender,message.Proof,ch=>member is not null&&ch.CampaignId==campaign.Id&&ch.ParticipantId==member.ParticipantId&&ch.ProfileId==member.ProfileId?campaign.Credential(member.ParticipantId).Secret:null);
                if(!valid){RejectPeer(sender,"Nieprawidłowy lub zużyty dowód powrotu do kampanii.");return true;}
                AttachCampaignPeer(sender,hello,member!.ParticipantId,false);return true;
            }
            if(message.Kind=="campaign_identity_ack")
            {
                if(campaign?.Id==message.CampaignId&&acceptedHello.ContainsKey(sender)&&message.RequestId!=Guid.Empty&&CommitCampaign(message.RequestId,s=>s.Confirm(sender)))
                {campaignIdentityReady.Add(sender);players[sender]=players[sender] with{Ready=autoStart};PublishLobby();}
                return true;
            }
            if(message.Kind=="reward_ack")
            {if(campaign?.Id==message.CampaignId&&acceptedHello.ContainsKey(sender))AcknowledgeCampaignReward(sender,message.SessionId);return true;}
        }
        else
        {
            if(message.Kind=="campaign_challenge" && message.Challenge is{} challenge)
            {
                if(challenge.ProfileId!=profile.PlayerId||!profile.CampaignCredentials.TryGetValue(challenge.CampaignId,out var credential)||credential.ParticipantId!=challenge.ParticipantId)
                {Leave("Ten profil nie posiada tokenu powrotu do kampanii.");return true;}
                SendControl(1,new("campaign_proof",Proof:CampaignAuthentication.Proof(credential,challenge)));return true;
            }
            if(message.Kind is "campaign_welcome" or "campaign_attached"){AcceptCampaignIdentity(message);return true;}
            if(message.Kind=="campaign_action_ack")return true;
        }
        return false;
    }
}
