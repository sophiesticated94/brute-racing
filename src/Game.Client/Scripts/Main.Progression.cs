using Game.Core;
using Godot;

namespace Game.Client;

public partial class Main
{
    private PlayerProfile profile=new();
    private ProfileRepository? profileRepository;
    private GarageScreen garage=null!;
    private GameMode gameMode=GameMode.Exhibition;
    private CampaignState? campaign;
    private CampaignView? campaignView;
    private Guid raceSessionId;

    private void InitializeProgression()
    {
        if(arguments.TryGetValue("mode",out var mode))gameMode=mode switch{"campaign"=>GameMode.Campaign,"quick"=>GameMode.Quick,_=>GameMode.Exhibition};
        if(!smoke && !arguments.ContainsKey("garage-preview") || arguments.ContainsKey("profile-path"))
        {
            string file=arguments.GetValueOrDefault("profile-path",ProjectSettings.GlobalizePath("user://profile.json"));
            profileRepository=new(catalog,file);profile=profileRepository.Load(out string warning);if(warning.Length>0)ui.Notice(warning,10);
        }
        if(arguments.TryGetValue("preset",out var preset))profile.SelectedPreset=Math.Clamp(int.Parse(preset)-1,0,2);
        ui.PlayerName.Text=profile.Name;ui.Paint.Select(profile.Paint);ui.UpdatePresets(profile);
        ui.DirectoryUrl.Text=System.Environment.GetEnvironmentVariable("MCM_DIRECTORY_URL")??"";
        garage=new GarageScreen();AddChild(garage);garage.Configure(catalog);garage.Command+=GarageCommand;
        ui.QuickRequested+=StartQuick;ui.CancelQueueRequested+=()=>{CancelQuick();LeaveInternal();ui.ShowMenu();ui.Notice("Anulowano szukanie rywali.");};
        ui.GarageRequested+=OpenGarage;ui.PresetSelected+=slot=>SelectPreset(slot);
        if(gameMode==GameMode.Campaign)SetBalance(LobbyBalanceMode.OpenMayhem);
    }
    private void SaveProfile()
    {
        try{profile.Name=CleanName(ui.PlayerName.Text);profile.Paint=(int)ui.Paint.Selected;profileRepository?.Save(profile);}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or InvalidDataException)
        {ui.Notice("Nie udało się zapisać profilu: "+e.Message,8);garage?.Notify("Nie udało się zapisać profilu.");}
    }
    private void LaunchCampaignSolo(){CancelQuick();gameMode=GameMode.Campaign;SetBalance(LobbyBalanceMode.OpenMayhem);StartSolo();}
    private void LaunchCampaignHost(){CancelQuick();gameMode=GameMode.Campaign;SetBalance(LobbyBalanceMode.OpenMayhem);StartHosting();}
    private void LaunchCampaignJoin(){CancelQuick();gameMode=GameMode.Campaign;JoinHost();}
    private void EnsureCampaign()
    {
        if(gameMode!=GameMode.Campaign || campaign is not null)return;
        int length=arguments.TryGetValue("campaign-races",out var n)?Math.Clamp(int.Parse(n),3,10):(int)ui.CampaignLength.Value;
        campaign=new(catalog,length,players.Values.Select(p=>(p.Id,p.Loadout)));
        if(smoke){requiredRounds=length;if(!arguments.ContainsKey("timeout"))maxSeconds=length*210;}
        SynchronizeCampaign();
    }
    private void SynchronizeCampaign()
    {
        if(campaign is null)return;
        campaign.Synchronize(players.Values.Select(p=>(p.Id,p.Loadout)));
        foreach(var p in players.Values.ToArray())
            if(campaign.Players.TryGetValue(p.Id,out var state))players[p.Id]=p with{Loadout=ValidateBuild(state.Loadout,p.Color)!};
        campaignView=campaign.View();ui.ShowCampaign(campaignView,localId);RefreshGarage();
    }
    private GarageContext GarageContext()
    {
        var own=campaignView?.Players.FirstOrDefault(p=>p.Id==localId);
        var build=players.GetValueOrDefault(localId)?.Loadout??own?.Loadout??profile.Presets[profile.SelectedPreset].Loadout;
        return new(profile,build,own,own is not null && campaignView?.Complete==false && phase==RacePhase.Lobby,balanceRules);
    }
    private void OpenGarage(){RefreshGarage();garage.Open(GarageContext());}
    private void RefreshGarage(){if(garage?.IsOpen==true)garage.Refresh(GarageContext());}
    private void SelectPreset(int slot)
    {
        if(queueTicket is not null){CancelQuick();LeaveInternal();ui.ShowMenu();}
        profile.SelectedPreset=Math.Clamp(slot,0,2);SaveProfile();
        if(inSession && phase==RacePhase.Lobby && gameMode==GameMode.Campaign)
        {
            var archived=profile.Presets[profile.SelectedPreset].Loadout;
            if(isHost)ApplyCampaignBuild(localId,archived);
            else SendControl(1,new("loadout",Name:SelectedName(),Color:(int)ui.Paint.Selected,Loadout:archived));
        }
        RefreshGarage();
    }
    private void GarageCommand(string kind,string item,int slot)
    {
        if(kind=="preset"){SelectPreset(slot);return;}
        if(kind=="archive")
        {
            if(profile.PendingArchive is null){garage.Notify("Ukończ kampanię, aby zapisać końcowy build.");return;}
            ProfileRules.Archive(profile,slot);SaveProfile();ui.UpdatePresets(profile);RefreshGarage();garage.Notify("Zapisano końcowy build do quick.");return;
        }
        if(kind=="cosmetic")
        {
            if(profile.OwnedCosmetics.Contains(item))profile.SelectedCosmetic=item;
            else if(!ProfileRules.BuyCosmetic(catalog,profile,item)){garage.Notify("Za mało monet.");return;}
            SaveProfile();RefreshGarage();
            if(inSession && phase==RacePhase.Lobby)ChangeLoadout();
            return;
        }
        if(gameMode!=GameMode.Campaign || phase!=RacePhase.Lobby){garage.Notify("Rozwijaj build w garażu między wyścigami kampanii.");return;}
        if(isHost)HandleCampaignAction(localId,kind,item);
        else SendControl(1,new("campaign_action",Message:kind,ItemId:item));
    }
    private bool ApplyCampaignBuild(int playerId,PlayerLoadoutDto build)
    {
        if(campaign is null || !campaign.TrySetLoadout(playerId,build,balanceRules,out string error))return false;
        SynchronizeCampaign();if(players.ContainsKey(playerId))players[playerId]=players[playerId] with{Ready=false};PublishLobby();return true;
    }
    private void HandleCampaignAction(int playerId,string kind,string item)
    {
        if(campaign is null || phase!=RacePhase.Lobby || campaign.Complete || !campaign.Players.TryGetValue(playerId,out var own))return;
        bool success=false;string error="Brak kredytów lub niespełnione wymagania.";
        switch(kind)
        {
            case "buy":success=campaign.TryPurchase(playerId,item);break;
            case "rank":success=campaign.TryRank(playerId,item,balanceRules,out error);break;
            case "respec":campaign.Respec(playerId);success=true;break;
            case "equip":
                if(own.OwnedLevels.TryGetValue(item,out int level) && item!="mine")success=campaign.TrySetLoadout(playerId,own.Loadout with{WeaponId=item,WeaponLevel=level},balanceRules,out error);
                break;
        }
        if(!success)
        {if(playerId==localId)garage.Notify(error);else SendControl(playerId,new("build_error",Message:error));return;}
        players[playerId]=players[playerId] with{Ready=false};SynchronizeCampaign();PublishLobby();
    }
    private void AutoSpendCampaign(int id)
    {
        if(campaign is null || campaign.Complete)return;
        var own=campaign.Players[id];
        while(campaign.TryPurchase(id,own.Loadout.WeaponId)){}
        while(campaign.TryPurchase(id,"mine")){}
        var goal=catalog.BotArchetypes[Math.Abs(id%5)].Loadout;
        string branch=catalog.Skills.Nodes[goal.SkillRanks.Keys.First()].Branch;
        bool bought=true;
        while(bought)
        {
            bought=false;
            foreach(var node in catalog.Skills.TopologicalOrder.Where(n=>catalog.Skills.Nodes[n].Branch==branch))
                if(campaign.TryRank(id,node,balanceRules,out _)){bought=true;break;}
        }
    }
    private void BeginIntermission()
    {
        if(campaign is null || campaign.Complete || !inSession || !isHost)return;
        phase=RacePhase.Lobby;
        foreach(var r in racers.Values){r.Car.Driving=false;r.Car.Health=r.Car.Stats.MaxHealth;}
        foreach(var p in players.Values.ToArray())
        {if(p.Bot || autoPlay)AutoSpendCampaign(p.Id);players[p.Id]=p with{Ready=p.Bot || autoPlay};}
        SynchronizeCampaign();autoStartAt=now+.5;PublishLobby();
    }
    private void ReplayRequested()
    {
        if(!isHost)return;
        if(gameMode==GameMode.Campaign)
        {
            if(campaign?.Complete==true){campaign=null;EnsureCampaign();}
            BeginIntermission();
        }
        else StartRace();
    }
    private void AwardRace()
    {
        if(campaign is not null)
        {
            campaign.RecordRace(raceSessionId,finalRanking,racers.ToDictionary(r=>r.Key,r=>r.Value.Progress.Finished),racers.ToDictionary(r=>r.Key,r=>new CombatScore(r.Value.Hits,r.Value.Kills)));
            SynchronizeCampaign();
        }
        foreach(var p in players.Values.Where(p=>!p.Bot))
        {
            DeliverReward(p.Id,new(raceSessionId,RewardKind.Race));
            if(campaign?.Complete==true)
            {
                var own=campaign.Players[p.Id];
                var archive=new ArchivedBuild($"Kanion {campaign.Length} • {catalog.Weapons[own.Loadout.WeaponId].Name}",own.Loadout,campaign.Id,DateTimeOffset.UtcNow);
                DeliverReward(p.Id,new(campaign.Id,RewardKind.Campaign,campaign.RewardCoins(p.Id),archive));
            }
        }
        if(campaign is not null && !campaign.Complete && !smoke)
        {
            uint epoch=raceId;GetTree().CreateTimer(3).Timeout+=()=>{if(epoch==raceId && phase==RacePhase.Podium)BeginIntermission();};
        }
    }
    private void DeliverReward(int target,RewardReceipt receipt)
    {
        if(target==localId)AcceptReward(receipt);
        else SendControl(target,new("reward",Reward:receipt,SessionId:raceSessionId));
    }
    private void AcceptReward(RewardReceipt receipt)
    {
        if(receipt.Kind==RewardKind.Race && receipt.Id!=raceSessionId || receipt.Kind==RewardKind.Campaign && receipt.Id!=(campaign?.Id??campaignView?.Id))return;
        ProfileRules.ApplyReward(catalog,profile,receipt);SaveProfile();RefreshGarage();
    }
    private void UpdateCampaignMessage(SessionMessage message)
    {
        gameMode=message.Mode;
        if(message.SessionId!=Guid.Empty)raceSessionId=message.SessionId;
        campaignView=message.Campaign;ui.ShowCampaign(campaignView,localId);
    }
    private void PreviewGarage()
    {
        profile.Coins=200;
        var build=PlayerLoadoutDto.Default with{PlayerLevel=6};
        garage.Open(new(profile,build,new(localId,500,6,2000,30,0,catalog.Weapons.Keys.ToDictionary(k=>k,k=>1),build),true,LobbyBalanceRules.OpenMayhem));
    }
}
