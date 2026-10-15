using Game.Core;

namespace Game.Core.Tests;

public class WeaponTests
{
    [Fact]
    public void EveryWeaponHasFiveExplicitLevelsAndLevelOneIsUnchanged()
    {
        var catalog = DomainTests.Catalog();
        Assert.All(catalog.WeaponLevels.Values, levels => Assert.Equal([1,2,3,4,5], levels.Select(w => w.Level)));
        Assert.Equal(7, catalog.Weapons["machine_gun"].Damage); Assert.Equal(180, catalog.Weapons["machine_gun"].AmmoCapacity);
        Assert.Equal(23, catalog.Weapons["laser"].CoolingRate);
        foreach (var id in catalog.Weapons.Keys)
            Assert.True(catalog.WeaponLevels[id][4].Damage > catalog.WeaponLevels[id][0].Damage);
    }
    [Fact]
    public void HighLevelWeaponsUnlockRealMechanics()
    {
        var catalog = DomainTests.Catalog();
        Assert.True(catalog.WeaponLevels["machine_gun"][3].ArmorPenetration > 0);
        Assert.True(catalog.WeaponLevels["laser"][4].Perks.HasFlag(WeaponPerkFlags.LaserPierce));
        Assert.Equal(2, catalog.WeaponLevels["rocket"][4].ProjectileCount);
        Assert.True(catalog.WeaponLevels["mine"][4].Perks.HasFlag(WeaponPerkFlags.EmpField));
        Assert.True(catalog.WeaponLevels["mine"][4].ArmingTime < .5f);
        var stats = VehicleStatsCalculator.CalculateBuild(catalog, new("rook", "rocket", 5, new(), 5));
        Assert.Equal(5, stats.Primary.Level); Assert.Equal(5, stats.Mines.Level);
    }
    [Fact]
    public void ModifierOrderAndCapsAreDeterministic()
    {
        var car = DomainTests.Catalog().Cars["rook"];
        (string, StatModifier)[] effects = [
            ("z",new("car.MaxHealth",ModifierOperation.Override,200,1)),
            ("b",new("car.MaxHealth",ModifierOperation.Multiply,2)),
            ("a",new("car.MaxHealth",ModifierOperation.Add,30))];
        Assert.Equal(200, StatLimits.Apply(car with {}, "car", effects).MaxHealth);
        Assert.Equal(StatLimits.Apply(car with {},"car",effects), StatLimits.Apply(car with {},"car",effects.Reverse()));
        Assert.Equal(500, StatLimits.Apply(car with {}, "car", [("large",new("car.MaxHealth",ModifierOperation.Multiply,100))]).MaxHealth);
        Assert.Throws<InvalidDataException>(() => StatLimits.Clamp("FireRate", float.NaN));
        Assert.Equal(.1f, StatLimits.Clamp("FireRate",0));
    }
}
