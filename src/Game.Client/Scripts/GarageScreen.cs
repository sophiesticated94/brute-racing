using Game.Core;
using Godot;

namespace Game.Client;

public sealed record GarageContext(PlayerProfile Profile,PlayerLoadoutDto Build,CampaignPlayerView? Participant,bool Editable,LobbyBalanceRules Rules);

public partial class GarageScreen : CanvasLayer
{
    public event Action<string,string,int>? Command;
    private GameCatalog catalog=null!;
    private GarageContext context=null!;
    private Control root=null!;
    private SkillCanvas graph=null!;
    private Label heading=null!,detail=null!,stats=null!,notice=null!;
    private Button rankButton=null!,respecButton=null!,buyPrimary=null!,buyMines=null!,buyCosmetic=null!;
    private OptionButton presets=null!,weapons=null!,cosmetics=null!;
    private ConfirmationDialog confirmation=null!;
    private Action? pendingConfirm;
    private string selected="drive_accel";
    private bool refreshing;
    public bool IsOpen=>root?.Visible==true;
    public void Configure(GameCatalog value){catalog=value;graph.Configure(value.Skills);}
    public void Open(GarageContext value){root.Visible=true;Refresh(value);}
    public void Close()=>root.Visible=false;
    public void Notify(string text)=>notice.Text=text;
    public override void _Ready()
    {
        Layer=20;root=new Control();AddChild(root);root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(new ColorRect{Color=new(.025f,.055f,.08f,.99f),AnchorRight=1,AnchorBottom=1,MouseFilter=Control.MouseFilterEnum.Ignore});
        var margin=new MarginContainer{AnchorRight=1,AnchorBottom=1,OffsetLeft=26,OffsetRight=-26,OffsetTop=20,OffsetBottom=-20};root.AddChild(margin);
        var all=new VBoxContainer();all.AddThemeConstantOverride("separation",14);margin.AddChild(all);
        var top=new HBoxContainer();all.AddChild(top);heading=Text(top,"GARAŻ",25);heading.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
        Button(top,"WRÓĆ",Close);
        notice=Text(all,"Środkowy lub prawy przycisk: przesuwanie • Kółko: przybliżanie",15,new("9eb8bc"));
        var body=new HBoxContainer{SizeFlagsVertical=Control.SizeFlags.ExpandFill};body.AddThemeConstantOverride("separation",20);all.AddChild(body);
        graph=new SkillCanvas{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,SizeFlagsVertical=Control.SizeFlags.ExpandFill};body.AddChild(graph);
        graph.Selected+=id=>{selected=id;UpdateDetail();};
        var scroll=new ScrollContainer{CustomMinimumSize=new(365,0),SizeFlagsVertical=Control.SizeFlags.ExpandFill};body.AddChild(scroll);
        var right=new VBoxContainer{CustomMinimumSize=new(345,0),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};right.AddThemeConstantOverride("separation",11);scroll.AddChild(right);
        Text(right,"ARCHIWUM DO QUICK",17,new("ffbe45"));presets=new OptionButton();right.AddChild(presets);
        presets.ItemSelected+=index=>{if(!refreshing)Command?.Invoke("preset","",(int)index);};
        for(int i=0;i<3;i++){int slot=i;Button(right,$"ZAPISZ KOŃCOWY BUILD → SLOT {i+1}",()=>Confirm($"Zastąpić archiwalny build w slocie {slot+1}?",()=>Command?.Invoke("archive","",slot)));}
        Text(right,"WYPOSAŻENIE KAMPANII",17,new("ffbe45"));weapons=new OptionButton();foreach(var name in new[]{"Karabin","Laser","Rakiety"})weapons.AddItem(name);right.AddChild(weapons);
        weapons.ItemSelected+=index=>{if(!refreshing)Command?.Invoke("equip",new[]{"machine_gun","laser","rocket"}[(int)index],0);};
        buyPrimary=Button(right,"ULEPSZ BROŃ",()=>Command?.Invoke("buy",context.Build.WeaponId,0));
        buyMines=Button(right,"ULEPSZ MINY",()=>Command?.Invoke("buy","mine",0));
        stats=Text(right,"",16);
        right.AddChild(new HSeparator());detail=Text(right,"Wybierz węzeł",16);detail.AutowrapMode=TextServer.AutowrapMode.WordSmart;
        rankButton=Button(right,"ROZWIŃ RANGĘ",()=>Command?.Invoke("rank",selected,0));
        respecButton=Button(right,"RESETUJ PUNKTY",()=>Confirm("Zresetować wszystkie rangi? Otrzymasz pełny zwrot SP.",()=>Command?.Invoke("respec","",0)));
        Text(right,"KOSMETYKA • BEZ WPŁYWU NA SIŁĘ",16,new("ffbe45"));cosmetics=new OptionButton();right.AddChild(cosmetics);
        cosmetics.ItemSelected+=_=>UpdateCosmetic();
        buyCosmetic=Button(right,"KUP / ZAŁÓŻ",()=>Command?.Invoke("cosmetic",catalog.Cosmetics.Keys.ElementAt((int)cosmetics.Selected),0));
        for(int i=0;i<5;i++)right.MoveChild(right.GetChild(0),right.GetChildCount()-1);
        right.MoveChild(detail,0);right.MoveChild(rankButton,1);right.MoveChild(respecButton,2);
        confirmation=new ConfirmationDialog{Title="Potwierdzenie"};AddChild(confirmation);confirmation.GetOkButton().Text="POTWIERDŹ";confirmation.GetCancelButton().Text="ANULUJ";
        confirmation.Confirmed+=()=>{var action=pendingConfirm;pendingConfirm=null;action?.Invoke();};
        root.Visible=false;
    }
    public void Refresh(GarageContext value)
    {
        context=value;refreshing=true;
        var participant=value.Participant;
        heading.Text=participant is null?$"ARCHIWUM / KOSMETYKA   •   MONETY {value.Profile.Coins}":$"GARAŻ   •   XP {participant.Xp}   POZIOM {participant.Level}   KREDYTY {participant.Credits}   SP {participant.AvailablePoints}   •   MONETY {value.Profile.Coins}";
        presets.Clear();for(int i=0;i<3;i++)presets.AddItem($"{i+1}. {value.Profile.Presets[i].Name}");presets.Select(value.Profile.SelectedPreset);
        weapons.Select(Array.IndexOf(new[]{"machine_gun","laser","rocket"},value.Build.WeaponId));weapons.Disabled=!value.Editable;
        var build=VehicleStatsCalculator.CalculateBuild(catalog,value.Build);
        stats.Text=$"ROOK • {SkillCanvas.BranchName("Drive")}\nPrędkość {build.Vehicle.MaxSpeed*3.6f:0} km/h\nPrzyspieszenie {build.Vehicle.Acceleration:0.0}\nZdrowie {build.Vehicle.MaxHealth:0}   Pancerz {build.Vehicle.Armor:P0}\nOdporność na odrzut {build.Vehicle.KnockbackResistance:P0}\nWieżyczka {Mathf.RadToDeg(build.Vehicle.TurretTraverseSpeed):0}°/s\n{build.Primary.Name} {build.Primary.Level}/5 • obrażenia {build.Primary.Damage:0.0}\nStrzały/s {build.Primary.FireRate:0.0}   Amunicja {build.Primary.AmmoCapacity}\nChłodzenie {build.Primary.CoolingRate:0.0}/s\nMiny {build.Mines.Level}/5 • aktywne {build.Mines.MaxActive}\nSiła buildu {FitnessCalculator.Calculate(catalog,value.Build).Total:0} • {catalog.Skills.Cost(value.Build.SkillRanks)} SP";
        SetPurchase(buyPrimary,build.Primary,participant,value.Editable);SetPurchase(buyMines,build.Mines,participant,value.Editable);
        graph.UpdateBuild(value.Build,participant?.AvailablePoints??0);UpdateDetail();respecButton.Disabled=!value.Editable;
        int selection=(int)cosmetics.Selected;cosmetics.Clear();foreach(var item in catalog.Cosmetics.Values)cosmetics.AddItem($"{item.Name} • {(value.Profile.OwnedCosmetics.Contains(item.Id)?"POSIADANE":$"{item.Price} monet")}");
        cosmetics.Select(Math.Clamp(selection,0,catalog.Cosmetics.Count-1));UpdateCosmetic();refreshing=false;
    }
    private void SetPurchase(Button button,WeaponDefinition weapon,CampaignPlayerView? owner,bool editable)
    {
        int level=owner?.OwnedLevels.GetValueOrDefault(weapon.Id)??weapon.Level;
        int cost=level<5?catalog.WeaponLevels[weapon.Id][level].UpgradeCost:0;
        button.Text=level>=5?$"{weapon.Name.ToUpperInvariant()} • MAKSIMUM":$"{weapon.Name.ToUpperInvariant()} → {level+1}/5 • {cost} KR";
        button.Disabled=!editable || level>=5 || (owner?.Credits??0)<cost;
    }
    private void UpdateDetail()
    {
        if(context is null || !catalog.Skills.Nodes.TryGetValue(selected,out var node))return;
        int rank=context.Build.SkillRanks.GetValueOrDefault(selected);
        var requirements=node.Prerequisites.Length==0?"Brak":string.Join(", ",node.Prerequisites.Select(p=>$"{catalog.Skills.Nodes[p.NodeId].Name} {p.MinRank}"));
        string current=rank>0?EffectText(node.RankEffects[rank-1]):"Brak efektu";
        string next=rank<node.MaxRank?EffectText(node.RankEffects[rank]):"Osiągnięto maksimum";
        detail.Text=$"{node.Name}\n{rank}/{node.MaxRank} • wymagany poziom {node.RequiredPlayerLevel}\n{node.Description}\nWymagania: {requirements}\nOBECNIE: {current}\nNASTĘPNIE: {next}";
        rankButton.Text=rank<node.MaxRank?$"ROZWIŃ • {node.RankCosts[rank]} SP":"MAKSIMUM";
        var copy=new Dictionary<string,int>(context.Build.SkillRanks,StringComparer.Ordinal);copy[selected]=rank+1;
        var candidate=context.Build with {SkillRanks=copy};
        var result=new BuildValidator(catalog).Validate(LoadoutDeclaration.FromLegacy(candidate),context.Rules);
        rankButton.Disabled=!context.Editable || !result.Valid || result.Adjusted || rank>=node.MaxRank || (context.Participant?.AvailablePoints??0)<node.RankCosts[Math.Min(rank,node.MaxRank-1)];
    }
    private static string EffectText(SkillRankEffect effect)
    {
        var parts=effect.Modifiers.Select(m=>$"{StatName(m.Stat.Split('.')[1])} {(m.Operation==ModifierOperation.Multiply?$"{(m.Value-1)*100:+0.#;-0.#;0}%":m.Operation==ModifierOperation.Add?$"{m.Value:+0.##;-0.##;0}":$"= {m.Value:0.##}")}").ToList();
        foreach(var flag in Enum.GetValues<MechanicsFlags>().Where(f=>f!=MechanicsFlags.None && effect.Mechanics.HasFlag(f)))parts.Add(flag switch{MechanicsFlags.DriftBoost=>"Impuls po drifcie",MechanicsFlags.DriftCooling=>"Chłodzenie w drifcie",MechanicsFlags.CheckpointAmmo=>"Odzysk amunicji",MechanicsFlags.Regeneration=>"Samonaprawa",MechanicsFlags.TurretJam=>"Zakłócenie wieżyczki",_=>"Awaryjna mina"});
        return string.Join(", ",parts);
    }
    private static string StatName(string key)=>key switch{"MaxSpeed"=>"Prędkość","Acceleration"=>"Przyspieszenie","Grip"=>"Przyczepność","DriftFactor"=>"Kontrola driftu","Mass"=>"Masa","MaxHealth"=>"Zdrowie","Armor"=>"Pancerz","KnockbackResistance"=>"Odporność na odrzut","TurretTraverseSpeed"=>"Obrót wieżyczki","Damage"=>"Obrażenia","FireRate"=>"Szybkostrzelność","AmmoCapacity"=>"Magazynek","ProjectileSpeed"=>"Prędkość pocisku","EnergyConsumption"=>"Koszt energii","HeatGeneration"=>"Generowane ciepło","CoolingRate"=>"Chłodzenie","EnergyRegen"=>"Regeneracja energii","OverheatResume"=>"Temperatura restartu","SplashRadius"=>"Promień wybuchu","MaxActive"=>"Limit min","ArmingTime"=>"Czas uzbrojenia","SlowFactor"=>"Mnożnik prędkości","SlowDuration"=>"Czas spowolnienia","ArmorPenetration"=>"Penetracja pancerza","ProtectionBonus"=>"Czas osłony","RegenDelay"=>"Czas do naprawy","CheckpointAmmoFraction"=>"Odzysk magazynka",_=>"Wzmocnienie"};
    private void UpdateCosmetic()
    {
        if(context is null || cosmetics.Selected<0)return;
        var item=catalog.Cosmetics.Values.ElementAt((int)cosmetics.Selected);bool owned=context.Profile.OwnedCosmetics.Contains(item.Id);
        buyCosmetic.Text=owned?context.Profile.SelectedCosmetic==item.Id?"ZAŁOŻONO":"ZAŁÓŻ":$"KUP • {item.Price} MONET";
        buyCosmetic.Disabled=owned?context.Profile.SelectedCosmetic==item.Id:context.Profile.Coins<item.Price;
    }
    private void Confirm(string text,Action action){pendingConfirm=action;confirmation.DialogText=text;confirmation.PopupCentered();}
    private static Label Text(Node parent,string text,int size,Color? color=null)
    {var label=new Label{Text=text};label.AddThemeFontSizeOverride("font_size",size);label.AddThemeColorOverride("font_color",color??new("e8eddf"));parent.AddChild(label);return label;}
    private static Button Button(Node parent,string text,Action action)
    {var button=new Button{Text=text,CustomMinimumSize=new(0,36)};button.AddThemeFontSizeOverride("font_size",14);parent.AddChild(button);button.Pressed+=action;return button;}
}
