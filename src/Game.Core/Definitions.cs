using System.Text.Json;

namespace Game.Core;

public sealed record CarDefinition(string Id, string Name, float MaxSpeed, float Acceleration,
    float Braking, float ReverseSpeed, float Grip, float DriftFactor, float TurnSpeed,
    float Mass, float MaxHealth, float Armor, float KnockbackResistance,
    float Spring, float Damping, float SuspensionLength);

public sealed record WeaponDefinition(string Id, string Name, float Damage, float FireRate,
    float Range, float ProjectileSpeed, float SplashRadius, float KnockbackForce,
    int AmmoCapacity, float EnergyConsumption, float HeatGeneration);

public sealed record TrackDefinition(string Id, string Name, int Laps, float RadiusX,
    float RadiusZ, float Width, int Segments, int Checkpoints);

public sealed class GameCatalog
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, IncludeFields = true };
    public Dictionary<string, CarDefinition> Cars { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, WeaponDefinition> Weapons { get; } = new(StringComparer.Ordinal);
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
        foreach (var weapon in JsonSerializer.Deserialize<WeaponDefinition[]>(read("weapons/weapons.json"), Json)!)
        {
            if (string.IsNullOrWhiteSpace(weapon.Id) || !float.IsFinite(weapon.Damage) || weapon.Damage <= 0 ||
                weapon.FireRate <= 0 || weapon.Range <= 0 || weapon.AmmoCapacity < 0 || weapon.EnergyConsumption < 0)
                throw new InvalidDataException("Nieprawidłowa definicja broni.");
            catalog.Weapons.Add(weapon.Id, weapon);
        }
        foreach (var required in new[] { "machine_gun", "laser", "rocket", "mine" })
            if (!catalog.Weapons.ContainsKey(required)) throw new InvalidDataException($"Brak broni: {required}");
        catalog.Track = JsonSerializer.Deserialize<TrackDefinition>(read("tracks/cinder.json"), Json)!;
        if (catalog.Track.Checkpoints < 3 || catalog.Track.Segments < catalog.Track.Checkpoints ||
            catalog.Track.Width < 6 || catalog.Track.RadiusX <= catalog.Track.Width || catalog.Track.RadiusZ <= catalog.Track.Width || catalog.Track.Laps < 1)
            throw new InvalidDataException("Nieprawidłowa definicja toru.");
        return catalog;
    }
}

public sealed record PlayerLoadoutDto(string CarId, string WeaponId, int WeaponLevel,
    Dictionary<string, int> SkillRanks)
{
    public static PlayerLoadoutDto Default => new("rook", "machine_gun", 1, new());
}

public sealed class BuildValidator(GameCatalog catalog)
{
    public bool IsValid(PlayerLoadoutDto? loadout) => loadout is not null &&
        catalog.Cars.ContainsKey(loadout.CarId ?? "") &&
        catalog.Weapons.ContainsKey(loadout.WeaponId ?? "") && loadout.WeaponId != "mine" &&
        loadout.WeaponLevel == 1 && loadout.SkillRanks is { Count: 0 };
}

public static class VehicleStatsCalculator
{
    public static CarDefinition Calculate(GameCatalog catalog, PlayerLoadoutDto loadout)
    {
        if (!new BuildValidator(catalog).IsValid(loadout)) throw new ArgumentException("Nieprawidłowy build.", nameof(loadout));
        return catalog.Cars[loadout.CarId] with { };
    }
}
