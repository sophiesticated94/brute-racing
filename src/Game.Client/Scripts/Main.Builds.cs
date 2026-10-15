using Game.Core;
using Godot;

namespace Game.Client;

public partial class Main
{
    private LobbyBalanceRules balanceRules = LobbyBalanceRules.Stock;
    private int rejectedBuilds;
    private bool sentBuildProbes;
    private void HandleBuildRequest(int sender, SessionMessage message)
    {
        if (!players.TryGetValue(sender,out var player)) return;
        var loadout = message.Loadout;
        if (message.Color is < 0 or > 7 || message.Name is null || message.Name.Length > 20 || loadout is null ||
            !new BuildValidator(catalog).IsValid(loadout))
        {
            rejectedBuilds++; SendControl(sender,new("build_error",Message:"Odrzucono nieprawidłowy build."));
            GD.Print($"BUILD_REJECT peer={sender}"); return;
        }
        if(campaign is not null && !campaign.TrySetLoadout(sender,loadout,balanceRules,out _))
        {rejectedBuilds++;SendControl(sender,new("build_error",Message:"Brak wyposażenia lub punktów zdobytych w tej kampanii."));return;}
        players[sender] = player with { Name=CleanName(message.Name),Color=message.Color,Loadout=ValidateBuild(loadout,message.Color)!,Ready=false };
        PublishLobby();
    }
    private void ProbeBuilds()
    {
        if (sentBuildProbes || !arguments.ContainsKey("probe-builds")) return;
        sentBuildProbes = true;
        var baseline = PlayerLoadoutDto.Default with {PlayerLevel=6};
        foreach (var forged in new[] {
            baseline with {WeaponLevel=6}, baseline with {SkillRanks=new(){{"drive_accel",-1}}},
            baseline with {SkillRanks=new(){{"drive_accel",99}}}, baseline with {SkillRanks=new(){{"drive_speed",1}}},
            baseline with {SkillRanks=new(){{"unknown",1}}}})
            SendControl(1,new("loadout",Name:SelectedName(),Loadout:forged));
    }
    private PlayerLoadoutDto? ValidateBuild(PlayerLoadoutDto? value, int color, bool notify = false)
    {
        if (value is null) return null;
        var result = new BuildValidator(catalog).Validate(LoadoutDeclaration.FromLegacy(value,color),balanceRules);
        if (!result.Valid) { if (notify) ui.Notice(string.Join(" ",result.Errors),6); return null; }
        if (notify && result.Adjusted) ui.Notice($"Dostosowano build: {result.SpentPoints} SP / broń do poz. {balanceRules.MaxWeaponLevel}",5);
        return result.Effective!.ToLegacy();
    }
    private void SetBalance(LobbyBalanceMode mode)
    {
        if (inSession && !isHost || phase != RacePhase.Lobby) return;
        balanceRules = mode switch { LobbyBalanceMode.Stock => LobbyBalanceRules.Stock, LobbyBalanceMode.FairCap => LobbyBalanceRules.FairCap, _ => LobbyBalanceRules.OpenMayhem };
        if (isHost && inSession)
        {
            foreach (var p in players.Values.ToArray())
                players[p.Id] = p with { Loadout = ValidateBuild(p.Loadout,p.Color)!, Ready = p.Bot };
            PublishLobby();
        }
    }
    private PlayerLoadoutDto RequestedLoadout()
    {
        int primary = arguments.TryGetValue("weapon-level",out var level) ? Math.Clamp(int.Parse(level),1,5) : 1;
        int mine = arguments.TryGetValue("mine-level",out var mines) ? Math.Clamp(int.Parse(mines),1,5) : 1;
        if(gameMode==GameMode.Quick)return queuedBuild??profile.Presets[profile.SelectedPreset].Loadout with{CosmeticId=profile.SelectedCosmetic};
        if(gameMode==GameMode.Campaign)
        {
            var own=campaignView?.Players.FirstOrDefault(p=>p.Id==localId);
            if(own is not null)return own.Loadout with{WeaponId=ui.WeaponId,WeaponLevel=own.OwnedLevels.GetValueOrDefault(ui.WeaponId,1),CosmeticId=profile.SelectedCosmetic};
            return new("rook",ui.WeaponId,1,new(),1,1,profile.SelectedCosmetic);
        }
        return new("rook",ui.WeaponId,primary,new(),mine,CosmeticId:profile.SelectedCosmetic);
    }
}
