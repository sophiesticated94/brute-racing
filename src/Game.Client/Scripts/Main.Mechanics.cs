using Game.Core;
using Godot;

namespace Game.Client;

public partial class Main
{
    private void UpdateMechanics(Racer r, float dt)
    {
        var mechanics = r.Build.Mechanics; var car = r.Car;
        bool drifting = car.Command.Handbrake && car.Grounded && car.LinearVelocity.Length() > 5;
        if (drifting) r.DriftCharge = Mathf.Min(3,r.DriftCharge+dt);
        if (!drifting && r.WasDrifting)
        {
            if (r.DriftCharge >= .8f && mechanics.Flags.HasFlag(MechanicsFlags.DriftBoost))
            {
                car.ApplyCentralImpulse(-car.GlobalBasis.Z * car.Stats.Mass * mechanics.DriftBoostForce);
                r.BoostRemaining = mechanics.DriftBoostDuration;
                Emit("boost",r.Info.Id,0,car.GlobalPosition,Vector3.Zero);
            }
            r.DriftCharge = 0;
        }
        r.WasDrifting = drifting; r.BoostRemaining = Mathf.Max(0,r.BoostRemaining-dt);
        if (mechanics.Flags.HasFlag(MechanicsFlags.Regeneration) && now-r.LastDamage > mechanics.RegenDelay)
            car.Health = Mathf.Min(car.Stats.MaxHealth,car.Health+mechanics.RegenPerSecond*dt);
    }
}
