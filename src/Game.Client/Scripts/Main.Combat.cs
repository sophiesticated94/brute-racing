using System.Text.Json;
using Game.Core;
using Godot;

namespace Game.Client;

public partial class Main
{
    private void Refill(Racer r)
    {
        r.Ammo = catalog.Weapons[r.Info.Loadout.WeaponId].AmmoCapacity;
        r.Mines = catalog.Weapons["mine"].AmmoCapacity; r.Energy = 100; r.Heat = 0;
        r.Overheated = false; r.PrimaryCooldown = r.MineCooldown = 0;
    }

    private void UpdateRacer(Racer r, float dt)
    {
        var car = r.Car;
        r.PrimaryCooldown = Mathf.Max(0, r.PrimaryCooldown - dt); r.MineCooldown = Mathf.Max(0, r.MineCooldown - dt);
        r.Invulnerable = Mathf.Max(0, r.Invulnerable - dt); car.Invulnerability = r.Invulnerable;
        car.CollisionMask = r.Invulnerable > 0 || r.Respawn > 0 ? 1u : 3u;
        car.CollisionLayer = r.Respawn > 0 ? 0u : r.Invulnerable > 0 ? 4u : 2u;
        if (r.Respawn > 0)
        {
            r.Respawn -= dt; car.Driving = false;
            if (r.Respawn <= 0)
            {
                var p = track.GatePoint(r.Progress.LastCheckpoint) + track.GateDirection(r.Progress.LastCheckpoint) * 2 + Vector3.Up;
                car.ResetAt(p, track.YawAt(r.Progress.LastCheckpoint)); car.Health = car.Stats.MaxHealth; r.Invulnerable = 2; Refill(r);
                Emit("respawn", r.Info.Id, 0, p, Vector3.Zero);
            }
            return;
        }
        car.Command = r.Gate.Read(now); car.Driving = phase == RacePhase.Racing && !r.Progress.Finished;
        car.SurfaceGrip = track.SurfaceGrip(car.GlobalPosition);
        car.TurretYaw = Mathf.RotateToward(car.TurretYaw, car.Command.AimYaw, dt * car.Stats.TurretTraverseSpeed);
        r.Energy = Mathf.Min(100, r.Energy + 18 * dt); r.Heat = Mathf.Max(0, r.Heat - 23 * dt);
        if (r.Heat < 30 && r.Overheated) { r.Overheated = false; if (r.Info.Id == localId) ui.Notice("LASER GOTOWY", 2); }
        if (!car.Driving) { r.PreviousPosition = car.GlobalPosition; return; }
        CheckGateCrossing(r);
        if (car.GlobalPosition.Y < -5 || car.GlobalPosition.Length() > 150) { Destroy(r); return; }
        if (car.LinearVelocity.Length() < 1.4f && (r.Info.Bot || autoPlay)) r.Unstuck += dt; else r.Unstuck = 0;
        if (r.Unstuck > 4)
        {
            r.Unstuck = 0; car.ResetAt(track.GatePoint(r.Progress.LastCheckpoint) + track.GateDirection(r.Progress.LastCheckpoint) * 2 + Vector3.Up, track.YawAt(r.Progress.LastCheckpoint));
        }
        if (noWeapons) return;
        if (car.Command.Fire && r.PrimaryCooldown <= 0 && !r.Overheated) Fire(r);
        if (car.Command.Mine && r.MineCooldown <= 0 && r.Mines > 0)
        {
            var def = catalog.Weapons["mine"]; r.Mines--; r.MineCooldown = 1 / def.FireRate;
            var position = car.GlobalPosition + car.GlobalBasis.Z * 2.5f;
            var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(position + Vector3.Up * 2, position - Vector3.Up * 5, 1));
            position.Y = hit.Count > 0 ? ((Vector3)hit["position"]).Y + .15f : position.Y - .5f;
            Emit("mine", r.Info.Id, ++projectileSequence, position, Vector3.Zero);
        }
    }
    private void Fire(Racer r)
    {
        var def = catalog.Weapons[r.Info.Loadout.WeaponId];
        if (def.AmmoCapacity > 0 && r.Ammo <= 0 || r.Energy < def.EnergyConsumption) return;
        if (def.AmmoCapacity > 0) r.Ammo--;
        r.Energy -= def.EnergyConsumption; r.Heat = Mathf.Min(100, r.Heat + def.HeatGeneration);
        if (r.Heat >= 95) { r.Overheated = true; if (r.Info.Id == localId) ui.Notice("LASER PRZEGRZANY", 2); }
        r.PrimaryCooldown = 1 / def.FireRate; shots++;
        var direction = new Vector3(-Mathf.Sin(r.Car.TurretYaw), 0, -Mathf.Cos(r.Car.TurretYaw));
        var origin = r.Car.GlobalPosition + Vector3.Up * .92f + direction * 1.5f;
        if (def.Id == "rocket") { Emit("rocket", r.Info.Id, ++projectileSequence, origin, direction * def.ProjectileSpeed); return; }
        var end = origin + direction * def.Range;
        var query = PhysicsRayQueryParameters3D.Create(origin, end, 3); query.Exclude = new Godot.Collections.Array<Rid> { r.Car.GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count > 0)
        {
            end = (Vector3)hit["position"];
            if (hit["collider"].AsGodotObject() is ArcadeCar victim && racers.TryGetValue(victim.PlayerId, out var target))
                Damage(target, def.Damage, direction * def.KnockbackForce, r.Info.Id);
        }
        Emit(def.Id, r.Info.Id, 0, origin, end);
    }
    private void UpdateOrdnance(float dt)
    {
        foreach (var projectile in ordnance.ToArray())
        {
            if (!GodotObject.IsInstanceValid(projectile.Node)) { ordnance.Remove(projectile); continue; }
            projectile.Age += dt;
            if (!headless && !projectile.Mine && (int)(projectile.Age * 12) > projectile.SmokeFrame)
            {
                projectile.SmokeFrame = (int)(projectile.Age * 12);
                var puff = Art.Box(effects, new(.25f, .25f, .25f), projectile.Node.GlobalPosition, new("a9ad9d"));
                var fade = puff.CreateTween(); fade.SetParallel();
                fade.TweenProperty(puff, "position", puff.Position + Vector3.Up, .55);
                fade.TweenProperty(puff, "scale", Vector3.Zero, .55); fade.Chain().TweenCallback(Callable.From(puff.QueueFree));
            }
            if (!isHost)
            {
                if (!projectile.Mine) projectile.Node.Position += projectile.Velocity * dt;
                if (projectile.Age > 30) RemoveOrdnance(projectile);
                continue;
            }
            bool detonate = false;
            var position = projectile.Node.GlobalPosition;
            if (projectile.Mine)
            {
                if (projectile.Age >= .5f)
                    detonate = racers.Values.Any(r => r.Car.Health > 0 && r.Respawn <= 0 && (r.Info.Id != projectile.Owner || projectile.Age > 1.5f) && r.Car.GlobalPosition.DistanceTo(position) < catalog.Weapons["mine"].Range);
                if (projectile.Age > 25) { Emit("expire", projectile.Owner, projectile.Id, position, Vector3.Zero); continue; }
            }
            else
            {
                var next = position + projectile.Velocity * dt;
                var query = PhysicsRayQueryParameters3D.Create(position, next, 3);
                if (racers.TryGetValue(projectile.Owner, out var owner)) query.Exclude = new Godot.Collections.Array<Rid> { owner.Car.GetRid() };
                var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
                if (hit.Count > 0) { position = (Vector3)hit["position"]; detonate = true; }
                else projectile.Node.GlobalPosition = next;
                if (projectile.Age > 2) detonate = true;
            }
            if (detonate)
            {
                var def = catalog.Weapons[projectile.Mine ? "mine" : "rocket"];
                foreach (var r in racers.Values)
                {
                    var offset = r.Car.GlobalPosition - position; float distance = offset.Length();
                    if (distance > def.SplashRadius || r.Respawn > 0) continue;
                    float falloff = 1 - distance / def.SplashRadius;
                    Damage(r, def.Damage * falloff, (offset.Normalized() + Vector3.Up * .55f).Normalized() * def.KnockbackForce * falloff, projectile.Owner);
                }
                Emit("explosion", projectile.Owner, projectile.Id, position, Vector3.Zero, def.SplashRadius);
            }
        }
    }
    private void RemoveOrdnance(Ordnance p) { ordnance.Remove(p); p.Node.QueueFree(); }
    private void Damage(Racer r, float amount, Vector3 impulse, int attacker)
    {
        if (r.Respawn > 0 || r.Invulnerable > 0 || r.Progress.Finished || phase != RacePhase.Racing) return;
        float actualDamage = Mathf.Min(r.Car.Health, amount * (1 - r.Car.Stats.Armor));
        r.Car.Health -= actualDamage;
        r.Car.ApplyCentralImpulse(impulse * (1 - r.Car.Stats.KnockbackResistance));
        Emit("hit", attacker, r.Info.Id, r.Car.GlobalPosition + Vector3.Up * .5f, Vector3.Zero, actualDamage);
        if (r.Car.Health <= 0) Destroy(r);
    }
    private void Destroy(Racer r)
    {
        if (r.Respawn > 0) return;
        destructions++; r.Car.Health = 0; r.Respawn = 2.5f; r.Car.Driving = false;
        Emit("destroy", r.Info.Id, 0, r.Car.GlobalPosition, Vector3.Zero);
    }
    private void CheckGateCrossing(Racer r)
    {
        var car = r.Car; int gate = r.Progress.NextCheckpoint;
        var position = car.GlobalPosition; var previous = r.PreviousPosition; r.PreviousPosition = position;
        var center = track.GatePoint(gate); var forward = track.GateDirection(gate);
        float before = (previous - center).Dot(forward), after = (position - center).Dot(forward);
        // The plane test counts a real forward crossing, rather than an early contact
        // by the front bumper. Teleports never count as traversing a checkpoint.
        if (!CheckpointGeometry.CrossesForward(Art.Numerics(previous), Art.Numerics(position), Art.Numerics(center), Art.Numerics(forward), catalog.Track.Width / 2 + .5f, 5)) return;
        if (!r.Progress.Pass(gate, (after - before) * 60, raceElapsed)) return;
        checkpointPasses++;
        Emit("checkpoint", r.Info.Id, gate, car.GlobalPosition, Vector3.Zero, r.Progress.CompletedLaps);
        if (r.Progress.Finished) clock.NotifyFinish();
    }

    private void Emit(string kind, int actor, int target, Vector3 position, Vector3 end, float value = 0)
    {
        var e = new RaceEvent(raceId, ++eventSequence, kind, actor, target, Art.Numerics(position), Art.Numerics(end), value);
        ApplyEvent(e); Send(0, PacketCodec.EncodeJson(PacketKind.Event, e), true, 0);
    }
}
