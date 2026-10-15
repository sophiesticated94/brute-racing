using System.Text.Json;
using Game.Core;
using Godot;

namespace Game.Client;

public partial class Main
{
    private PlayerInputCommand ReadInput(Racer r, uint sequence)
    {
        float steer = (KeyDown(Key.D, Key.Right) ? 1 : 0) - (KeyDown(Key.A, Key.Left) ? 1 : 0);
        float throttle = (KeyDown(Key.W, Key.Up) ? 1 : 0) - (KeyDown(Key.S, Key.Down) ? 1 : 0);
        float brake = 0;
        bool fire = Input.IsMouseButtonPressed(MouseButton.Left), mine = Input.IsMouseButtonPressed(MouseButton.Right), handbrake = Input.IsPhysicalKeyPressed(Key.Space);
        var pads = LocalPads();
        if (pads.Count > 0)
        {
            int pad = pads[0]; float stick = Input.GetJoyAxis(pad, JoyAxis.LeftX);
            if (Mathf.Abs(stick) > .15f) steer = stick;
            float gas = Mathf.Max(0, Input.GetJoyAxis(pad, JoyAxis.TriggerRight));
            float reverse = Mathf.Max(0, Input.GetJoyAxis(pad, JoyAxis.TriggerLeft));
            if (gas > .1f || reverse > .1f) throttle = gas - reverse;
            var aim = new Vector2(Input.GetJoyAxis(pad, JoyAxis.RightX), Input.GetJoyAxis(pad, JoyAxis.RightY));
            if (aim.Length() > .25f) desiredAim = Mathf.Atan2(-aim.X, -aim.Y);
            fire |= Input.IsJoyButtonPressed(pad, JoyButton.RightShoulder); mine |= Input.IsJoyButtonPressed(pad, JoyButton.LeftShoulder); handbrake |= Input.IsJoyButtonPressed(pad, JoyButton.A);
        }
        if (!headless && (pads.Count == 0 || Input.GetLastMouseVelocity().Length() > 1))
        {
            var mouse = GetViewport().GetMousePosition(); var origin = camera.ProjectRayOrigin(mouse); var direction = camera.ProjectRayNormal(mouse);
            var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(origin, origin + direction * 500, 1));
            Vector3 target;
            if (hit.Count > 0) target = (Vector3)hit["position"];
            else target = origin + direction * ((r.Car.GlobalPosition.Y - origin.Y) / Mathf.Min(-.001f, direction.Y));
            var aim = target - r.Car.GlobalPosition; desiredAim = Mathf.Atan2(-aim.X, -aim.Z);
        }
        return new(sequence, tick, steer, throttle, brake, desiredAim, handbrake, fire, mine);
    }
    private static bool KeyDown(Key a, Key b) => Input.IsPhysicalKeyPressed(a) || Input.IsPhysicalKeyPressed(b);
    private PlayerInputCommand BotInput(Racer r, uint sequence)
    {
        var car = r.Car; int nearest = track.NearestPath(car.GlobalPosition);
        int nextGate = isHost ? r.Progress.NextCheckpoint : r.NetworkState?.NextCheckpoint ?? 1;
        var gate = track.GatePoint(nextGate);
        var target = track.Path[(nearest + 6) % track.Path.Length];
        // A missed gate must be crossed rather than orbiting forever with an invalid lap.
        int gateIndex = nextGate * track.Path.Length / catalog.Track.Checkpoints;
        int distanceToGate = (gateIndex - nearest + track.Path.Length) % track.Path.Length;
        if (distanceToGate < 9) target = gate + track.GateDirection(nextGate) * 2;
        bool missedGate = distanceToGate > track.Path.Length - 16 && distanceToGate < track.Path.Length - 3;
        if (missedGate) target = gate - track.GateDirection(nextGate) * 5;
        var offset = target - car.GlobalPosition; offset.Y = 0;
        var forward = -car.GlobalBasis.Z; forward.Y = 0; forward = forward.Normalized();
        var desired = offset.Normalized(); float angle = Mathf.Atan2(forward.Cross(desired).Y, forward.Dot(desired));
        float steer = Mathf.Clamp(-angle * 1.7f, -1, 1);
        float speed = new Vector2(car.LinearVelocity.X, car.LinearVelocity.Z).Length();
        float targetSpeed = Mathf.Lerp(22, 11, Mathf.Clamp(Mathf.Abs(angle) / .8f, 0, 1));
        if (missedGate) targetSpeed = 7;
        // Separation steers away from cars immediately ahead without changing checkpoint logic.
        foreach (var other in racers.Values)
        {
            if (other == r || other.Car.Health <= 0) continue;
            var relative = other.Car.GlobalPosition - car.GlobalPosition;
            if (relative.LengthSquared() < 30 && relative.Dot(forward) > 1)
                steer += Mathf.Sign(relative.Dot(car.GlobalBasis.X)) * -.25f;
        }
        var enemy = racers.Values.Where(o => o != r && o.Car.Health > 0 && o.Respawn <= 0)
            .OrderBy(o => o.Car.GlobalPosition.DistanceSquaredTo(car.GlobalPosition)).FirstOrDefault();
        Vector3 aim = enemy is not null ? enemy.Car.GlobalPosition - car.GlobalPosition : forward;
        float yaw = Mathf.Atan2(-aim.X, -aim.Z);
        bool fire = !noWeapons && enemy is not null && aim.Length() < 35 && r.Invulnerable <= 0 && (tick + (uint)Math.Abs(r.Info.Id % 10)) % 90 < 42;
        bool mine = !noWeapons && speed > 8 && tick % (uint)(240 + Math.Abs(r.Info.Id % 7) * 19) == 0;
        return new(sequence, tick, Mathf.Clamp(steer, -1, 1), speed < targetSpeed ? 1 : 0, speed > targetSpeed + 1 ? .35f : 0, yaw, arguments.ContainsKey("exercise-mechanics") && speed>8 && tick%360 is >=180 and <240, fire, mine);
    }

}
