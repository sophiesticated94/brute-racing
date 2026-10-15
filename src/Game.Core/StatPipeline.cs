using System.Reflection;

namespace Game.Core;

public enum ModifierOperation { Add, Multiply, Override }
public sealed record StatModifier(string Stat, ModifierOperation Operation, float Value, int Priority = 0);
[Flags]
public enum MechanicsFlags { None = 0, DriftBoost = 1, DriftCooling = 2, CheckpointAmmo = 4, Regeneration = 8, EmergencyMine = 16, TurretJam = 32 }
public sealed record MechanicsStats(MechanicsFlags Flags = MechanicsFlags.None, float DriftBoostForce = 7,
    float DriftBoostDuration = .8f, float CheckpointAmmoFraction = .08f, float RegenPerSecond = 2,
    float RegenDelay = 5, float ProtectionBonus = 0, float DriftCoolingMultiplier = 1.7f);
public sealed record ComputedLoadoutStats(CarDefinition Vehicle, WeaponDefinition Primary, WeaponDefinition Mines, MechanicsStats Mechanics);

public static class StatLimits
{
    private static readonly Dictionary<string, (float Min, float Max)> Limits = new(StringComparer.Ordinal)
    {
        ["MaxSpeed"]=(10,42), ["Acceleration"]=(5,45), ["Braking"]=(5,65), ["ReverseSpeed"]=(3,16),
        ["Grip"]=(1,16), ["DriftFactor"]=(.12f,.8f), ["TurnSpeed"]=(.5f,4), ["Mass"]=(450,1400),
        ["MaxHealth"]=(50,500), ["Armor"]=(0,.65f), ["KnockbackResistance"]=(0,.8f),
        ["Spring"]=(10000,60000), ["Damping"]=(1000,10000), ["SuspensionLength"]=(.5f,1.3f), ["TurretTraverseSpeed"]=(1,8),
        ["Damage"]=(1,120), ["FireRate"]=(.1f,30), ["Range"]=(1,80), ["ProjectileSpeed"]=(0,70),
        ["SplashRadius"]=(0,10), ["KnockbackForce"]=(0,18000), ["AmmoCapacity"]=(0,1000),
        ["EnergyConsumption"]=(0,50), ["HeatGeneration"]=(0,60), ["CoolingRate"]=(5,80), ["EnergyRegen"]=(1,60),
        ["OverheatResume"]=(10,60), ["OverheatThreshold"]=(65,100), ["ArmingTime"]=(.15f,1),
        ["MaxActive"]=(1,12), ["ArmorPenetration"]=(0,.8f), ["ProjectileCount"]=(1,2), ["SlowFactor"]=(.45f,1), ["SlowDuration"]=(0,5),
        ["DriftBoostForce"]=(0,10), ["DriftBoostDuration"]=(.1f,1.5f), ["CheckpointAmmoFraction"]=(0,.2f),
        ["RegenPerSecond"]=(0,8), ["RegenDelay"]=(2,12), ["ProtectionBonus"]=(0,2), ["DriftCoolingMultiplier"]=(1,3)
    };
    public static float Clamp(string name, float value)
    {
        if (!float.IsFinite(value)) throw new InvalidDataException($"Niefinitywna statystyka: {name}");
        if (!Limits.TryGetValue(name, out var limit)) throw new InvalidDataException($"Nieznana statystyka: {name}");
        return Math.Clamp(value, limit.Min, limit.Max);
    }
    public static void ValidateWeapon(WeaponDefinition weapon)
    {
        if (string.IsNullOrWhiteSpace(weapon.Id) || weapon.UpgradeCost < 0 || weapon.Damage <= 0 || weapon.FireRate <= 0 || weapon.Range <= 0 || weapon.AmmoCapacity < 0 || weapon.EnergyConsumption < 0)
            throw new InvalidDataException("Nieprawidłowa definicja broni.");
        if(weapon.Level is <1 or >5 || ((int)weapon.Perks & ~31)!=0 || weapon.OverheatResume>=weapon.OverheatThreshold)throw new InvalidDataException("Nieprawidłowe mechaniki broni.");
        ValidateNumbers(weapon);
    }
    public static void ValidateNumbers<T>(T definition) where T:class
    {
        foreach(var p in NumericProperties(typeof(T)))
        {float value=Convert.ToSingle(p.GetValue(definition));if(Clamp(p.Name,value)!=value)throw new InvalidDataException($"Definicja poza ograniczeniami: {p.Name}");}
    }
    private static IEnumerable<PropertyInfo> NumericProperties(Type type) => type.GetProperties().Where(p =>
        (p.PropertyType == typeof(float) || p.PropertyType == typeof(int)) && p.Name is not "Level" and not "UpgradeCost");
    public static bool IsKnown(string name) => Limits.ContainsKey(name);
    public static CarDefinition ClampCar(CarDefinition value) => Apply(value with { }, "car", []);
    public static WeaponDefinition ClampWeapon(WeaponDefinition value) => Apply(value with { }, "primary", []);
    public static T Apply<T>(T copy, string target, IEnumerable<(string Source, StatModifier Modifier)> modifiers) where T : class
    {
        var effects = modifiers.Where(x => x.Modifier.Stat.StartsWith(target + ".", StringComparison.Ordinal))
            .OrderBy(x => x.Modifier.Priority).ThenBy(x => x.Source, StringComparer.Ordinal).ToArray();
        foreach (var property in NumericProperties(typeof(T)))
        {
            string key = target + "." + property.Name;
            double value = Convert.ToDouble(property.GetValue(copy));
            var own = effects.Where(x => x.Modifier.Stat == key).ToArray();
            value += own.Where(x => x.Modifier.Operation == ModifierOperation.Add).Sum(x => (double)x.Modifier.Value);
            foreach (var effect in own.Where(x => x.Modifier.Operation == ModifierOperation.Multiply)) value *= effect.Modifier.Value;
            foreach (var effect in own.Where(x => x.Modifier.Operation == ModifierOperation.Override)) value = effect.Modifier.Value;
            float final = Clamp(property.Name, (float)value);
            if (property.PropertyType == typeof(int)) property.SetValue(copy, (int)MathF.Round(final)); else property.SetValue(copy, final);
        }
        return copy;
    }
}
