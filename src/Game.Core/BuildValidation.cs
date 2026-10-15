namespace Game.Core;

public enum LobbyBalanceMode { Stock, FairCap, OpenMayhem }
public sealed record LobbyBalanceRules(LobbyBalanceMode Mode, int SkillBudget, int MaxWeaponLevel)
{
    public static LobbyBalanceRules Stock => new(LobbyBalanceMode.Stock,0,1);
    public static LobbyBalanceRules FairCap => new(LobbyBalanceMode.FairCap,15,3);
    public static LobbyBalanceRules OpenMayhem => new(LobbyBalanceMode.OpenMayhem,30,5);
    public bool IsValid => Enum.IsDefined(Mode) && SkillBudget is >= 0 and <= 30 && MaxWeaponLevel is >= 1 and <= 5 &&
        (Mode != LobbyBalanceMode.Stock || SkillBudget == 0 && MaxWeaponLevel == 1);
}
public sealed record LoadoutDeclaration(string CarId, int Color, string PrimaryWeaponId, int PrimaryLevel,
    int MineLevel, Dictionary<string,int> SkillRanks, int PlayerLevel = 1, string CosmeticId = "stock")
{
    public static LoadoutDeclaration FromLegacy(PlayerLoadoutDto value, int color = 0) =>
        new(value.CarId,color,value.WeaponId,value.WeaponLevel,value.MineLevel,value.SkillRanks is null ? null! : new(value.SkillRanks,StringComparer.Ordinal),value.PlayerLevel,value.CosmeticId);
    public PlayerLoadoutDto ToLegacy() => new(CarId,PrimaryWeaponId,PrimaryLevel,new(SkillRanks,StringComparer.Ordinal),MineLevel,PlayerLevel,CosmeticId);
}
public sealed record BuildValidationResult(bool Valid, string[] Errors, LoadoutDeclaration? Effective, bool Adjusted, int SpentPoints);

public sealed class BuildValidator(GameCatalog catalog)
{
    public bool IsValid(PlayerLoadoutDto? loadout) => loadout is not null && Validate(LoadoutDeclaration.FromLegacy(loadout),LobbyBalanceRules.OpenMayhem).Valid;
    public BuildValidationResult Validate(LoadoutDeclaration? declaration, LobbyBalanceRules rules)
    {
        var errors = new List<string>();
        if (declaration is null || !rules.IsValid) return new(false,["Nieprawidłowa deklaracja lub reguły."],null,false,0);
        var d = declaration;
        if (!catalog.Cars.ContainsKey(d.CarId ?? "")) errors.Add("Nieznane podwozie.");
        if (!catalog.Weapons.ContainsKey(d.PrimaryWeaponId ?? "") || d.PrimaryWeaponId == "mine") errors.Add("Nieprawidłowa broń główna.");
        if (d.Color is < 0 or > 7 || d.PrimaryLevel is < 1 or > 5 || d.MineLevel is < 1 or > 5 || d.PlayerLevel is < 1 or > 6) errors.Add("Nieprawidłowy poziom lub kolor.");
        if (d.SkillRanks is null || d.SkillRanks.Count > catalog.Skills.Nodes.Count) errors.Add("Nieprawidłowe rangi.");
        else
        {
            foreach (var rank in d.SkillRanks)
            {
                if (!catalog.Skills.Nodes.TryGetValue(rank.Key,out var node) || rank.Value < 0 || rank.Value > node.MaxRank) { errors.Add($"Nieprawidłowa ranga {rank.Key}."); continue; }
                if (rank.Value == 0) continue;
                if (d.PlayerLevel < node.RequiredPlayerLevel) errors.Add($"Wymagany poziom gracza: {node.Name}.");
                if (node.Prerequisites.Any(p => !d.SkillRanks.TryGetValue(p.NodeId,out int value) || value < p.MinRank)) errors.Add($"Brak wymagań: {node.Name}.");
            }
            var active = d.SkillRanks.Where(r => r.Value > 0 && catalog.Skills.Nodes.ContainsKey(r.Key)).Select(r => catalog.Skills.Nodes[r.Key]);
            if (active.Where(n => !string.IsNullOrEmpty(n.ExclusiveGroup)).GroupBy(n => n.ExclusiveGroup,StringComparer.Ordinal).Any(g => g.Count() > 1)) errors.Add("Wykluczające się specjalizacje.");
        }
        if (errors.Count > 0) return new(false,errors.ToArray(),null,false,0);
        int originalCost = catalog.Skills.Cost(d.SkillRanks!);
        if (originalCost > 30 || originalCost > Math.Min(30,(d.PlayerLevel-1)*6)) return new(false,["Przekroczony budżet poziomu gracza."],null,false,originalCost);
        var ranks = d.SkillRanks!.Where(r => r.Value > 0).ToDictionary(r => r.Key,r => r.Value,StringComparer.Ordinal);
        int spent = originalCost;
        while (spent > rules.SkillBudget)
        {
            string last = catalog.Skills.TopologicalOrder.AsEnumerable().Reverse().First(id => ranks.GetValueOrDefault(id) > 0);
            ranks[last]--; if (ranks[last] == 0) ranks.Remove(last);
            spent = catalog.Skills.Cost(ranks);
        }
        var effective = d with { PrimaryLevel = Math.Min(d.PrimaryLevel,rules.MaxWeaponLevel), MineLevel = Math.Min(d.MineLevel,rules.MaxWeaponLevel), SkillRanks = ranks };
        bool adjusted = spent != originalCost || effective.PrimaryLevel != d.PrimaryLevel || effective.MineLevel != d.MineLevel;
        return new(true,[],effective,adjusted,spent);
    }
}
