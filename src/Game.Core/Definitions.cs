using System.Text.Json;

namespace Game.Core;

public sealed record CarDefinition(string Id, string Name, float MaxSpeed, float Acceleration,
    float Braking, float ReverseSpeed, float Grip, float DriftFactor, float TurnSpeed,
    float Mass, float MaxHealth, float Armor, float KnockbackResistance,
    float Spring, float Damping, float SuspensionLength, float TurretTraverseSpeed = 4.5f);

public sealed record WeaponDefinition(string Id, string Name, float Damage, float FireRate,
    float Range, float ProjectileSpeed, float SplashRadius, float KnockbackForce,
    int AmmoCapacity, float EnergyConsumption, float HeatGeneration,
    int Level = 1, int UpgradeCost = 0, float CoolingRate = 23, float EnergyRegen = 18,
    float OverheatResume = 30, float OverheatThreshold = 95, float ArmingTime = .5f,
    int MaxActive = 4, float ArmorPenetration = 0, int ProjectileCount = 1,
    float SlowFactor = 1, float SlowDuration = 0, WeaponPerkFlags Perks = WeaponPerkFlags.None);

[Flags]
public enum WeaponPerkFlags { None = 0, ArmorPierce = 1, LaserPierce = 2, Salvo = 4, EmpField = 8, FastArming = 16 }

public sealed record TrackDefinition(string Id, string Name, int Laps, float RadiusX,
    float RadiusZ, float Width, int Segments, int Checkpoints);

public sealed class GameCatalog
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, IncludeFields = true };
    public Dictionary<string, CarDefinition> Cars { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, WeaponDefinition> Weapons { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, WeaponDefinition[]> WeaponLevels { get; } = new(StringComparer.Ordinal);
    public SkillGraph Skills { get; private set; } = new([]);
    public EconomyDefinition Economy { get; private set; } = new();
    public Dictionary<string,CosmeticDefinition> Cosmetics {get;} = new(StringComparer.Ordinal);
    public static string[] Files => ["cars/rook.json", "weapons/weapons.json", "tracks/cinder.json", "skills/skills.json", "rules/economy.json", "cosmetics/catalog.json"];
    public TrackDefinition Track { get; private set; } = null!;

    public static GameCatalog Load(Func<string, string> read)
    {
        var catalog = new GameCatalog();
        var car = JsonSerializer.Deserialize<CarDefinition>(read("cars/rook.json"), Json)!;
        if (string.IsNullOrWhiteSpace(car.Id) || !float.IsFinite(car.MaxSpeed) || car.MaxSpeed <= 0 || car.Mass <= 0 ||
            car.MaxHealth <= 0 || car.Spring <= 0 || car.Damping < 0 || car.SuspensionLength <= 0 ||
            car.Armor is < 0 or >= 1 || car.Grip <= 0 || car.Acceleration <= 0 || car.Braking <= 0)
            throw new InvalidDataException("Nieprawidłowa definicja samochodu.");
        catalog.Cars.Add(car.Id, car);
        foreach (var group in JsonSerializer.Deserialize<WeaponDefinition[]>(read("weapons/weapons.json"), Json)!.GroupBy(w => w.Id, StringComparer.Ordinal))
        {
            var levels = group.OrderBy(w => w.Level).ToArray();
            if (levels.Length != 5 || !levels.Select(w => w.Level).SequenceEqual(Enumerable.Range(1, 5))) throw new InvalidDataException("Broń wymaga poziomów 1–5.");
            foreach (var weapon in levels) StatLimits.ValidateWeapon(weapon);
            catalog.Weapons.Add(group.Key, levels[0]); catalog.WeaponLevels.Add(group.Key, levels);
        }
        foreach (var required in new[] { "machine_gun", "laser", "rocket", "mine" })
            if (!catalog.Weapons.ContainsKey(required)) throw new InvalidDataException($"Brak broni: {required}");
        catalog.Track = JsonSerializer.Deserialize<TrackDefinition>(read("tracks/cinder.json"), Json)!;
        if (catalog.Track.Checkpoints < 3 || catalog.Track.Segments < catalog.Track.Checkpoints ||
            catalog.Track.Width < 6 || catalog.Track.RadiusX <= catalog.Track.Width || catalog.Track.RadiusZ <= catalog.Track.Width || catalog.Track.Laps < 1)
            throw new InvalidDataException("Nieprawidłowa definicja toru.");
        catalog.Skills = new(JsonSerializer.Deserialize<SkillNodeDefinition[]>(read("skills/skills.json"),Json)!);
        catalog.Economy = JsonSerializer.Deserialize<EconomyDefinition>(read("rules/economy.json"),Json)!;
        if(catalog.Economy.XpPerLevel<=0 || catalog.Economy.SkillPointsPerLevel!=6 || catalog.Economy.BaseCredits<0 || catalog.Economy.CreditsRoundStep<0 || catalog.Economy.CampaignCoins<0)
            throw new InvalidDataException("Nieprawidłowa ekonomia.");
        foreach(var item in JsonSerializer.Deserialize<CosmeticDefinition[]>(read("cosmetics/catalog.json"),Json)!)
        {
            if(string.IsNullOrWhiteSpace(item.Id) || item.Price<0 || !new[]{"stock","stripe","hood","checker","trail"}.Contains(item.Type) ||
                !System.Text.RegularExpressions.Regex.IsMatch(item.Color??"","^[0-9a-fA-F]{6}$")) throw new InvalidDataException("Nieprawidłowa kosmetyka.");
            catalog.Cosmetics.Add(item.Id,item);
        }
        if(!catalog.Cosmetics.ContainsKey("stock"))throw new InvalidDataException("Brak fabrycznej kosmetyki.");
        return catalog;
    }
}

public sealed record PlayerLoadoutDto(string CarId, string WeaponId, int WeaponLevel,
    Dictionary<string, int> SkillRanks, int MineLevel = 1, int PlayerLevel = 1, string CosmeticId = "stock")
{
    public static PlayerLoadoutDto Default => new("rook", "machine_gun", 1, new());
}

public static class VehicleStatsCalculator
{
    public static CarDefinition Calculate(GameCatalog catalog, PlayerLoadoutDto loadout)
        => CalculateBuild(catalog, loadout).Vehicle;

    public static ComputedLoadoutStats CalculateBuild(GameCatalog catalog, PlayerLoadoutDto loadout)
    {
        if (!new BuildValidator(catalog).IsValid(loadout)) throw new ArgumentException("Nieprawidłowy build.", nameof(loadout));
        var effects = catalog.Skills.Modifiers(loadout.SkillRanks).ToArray();
        return new(StatLimits.Apply(catalog.Cars[loadout.CarId] with {},"car",effects),
            StatLimits.Apply(catalog.WeaponLevels[loadout.WeaponId][loadout.WeaponLevel - 1] with {},"primary",effects),
            StatLimits.Apply(catalog.WeaponLevels["mine"][loadout.MineLevel - 1] with {},"mines",effects),
            StatLimits.Apply(new MechanicsStats(Flags:catalog.Skills.Mechanics(loadout.SkillRanks)),"mechanics",effects));
    }
}
