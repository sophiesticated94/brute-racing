using Game.Core;
using Godot;

namespace Game.Client;

public partial class ArcadeCar : RigidBody3D
{
    public int PlayerId;
    public CarDefinition Stats = null!;
    public PlayerInputCommand Command;
    public bool Simulating = true;
    public bool Driving;
    public float SurfaceGrip = 1;
    public float TurretYaw;
    public float Health = 120;
    public float Invulnerability;
    public Node3D Visual { get; private set; } = null!;
    public Marker3D Muzzle { get; private set; } = null!;
    public Marker3D MineMount { get; private set; } = null!;
    public Vector3 VisualOffset;
    public bool Grounded { get; private set; }
    public uint LastSimulatedSequence { get; private set; }
    private Node3D turret = null!;
    private readonly List<RayCast3D> suspension = [];
    private readonly List<MeshInstance3D> wheels = [];
    private double smokeTime;
    private double skidTime;
    private float stuckTime;
    private Vector3? resetPosition;
    private Quaternion resetRotation;
    private (Vector3 Position, Quaternion Rotation, Vector3 Velocity, Vector3 Angular)? reconciliation;

    public void Reconcile(Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angular)
    {
        if (GlobalPosition.DistanceTo(position) < 4) VisualOffset += GlobalPosition - position;
        else VisualOffset = Vector3.Zero;
        VisualOffset = VisualOffset.LimitLength(3);
        reconciliation = (position, rotation, velocity, angular);
    }

    public void Build(CarDefinition stats, Color color, bool simulate)
    {
        Stats = stats; Health = stats.MaxHealth; Simulating = simulate;
        Mass = stats.Mass; GravityScale = 1.8f; LinearDamp = .12f; AngularDamp = 2.5f;
        ContinuousCd = true; ContactMonitor = false; MaxContactsReported = 0;
        CollisionLayer = 2; CollisionMask = 3;
        CenterOfMassMode = CenterOfMassModeEnum.Custom; CenterOfMass = new(0, -.25f, 0);
        AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(1.85f, .65f, 3.5f) } });
        AddChild(new CollisionShape3D { Position = new(0, .55f, .1f), Shape = new BoxShape3D { Size = new(1.65f, .65f, 1.5f) } });
        Visual = new Node3D(); AddChild(Visual);
        Art.Box(Visual, new(1.95f, .55f, 3.55f), Vector3.Zero, color);
        Art.Box(Visual, new(1.7f, .3f, 1.1f), new(0, .35f, .15f), color.Darkened(.13f));
        Art.Box(Visual, new(1.55f, .3f, .8f), new(0, .51f, -.03f), new("1e384b"));
        Art.Box(Visual, new(.28f, .015f, 3.5f), new(-.4f, .29f, 0), new("23394a"));
        Art.Box(Visual, new(.28f, .015f, 3.5f), new(.4f, .29f, 0), new("23394a"));
        Art.Box(Visual, new(2.05f, .25f, .28f), new(0, -.1f, -1.82f), new("20323e"));
        Art.Box(Visual, new(2.05f, .25f, .28f), new(0, -.1f, 1.82f), new("20323e"));
        for (int side = -1; side <= 1; side += 2)
        {
            Art.Box(Visual, new(.45f, .15f, .06f), new(side * .62f, .04f, -1.82f), new("fff4b4"), true);
            Art.Box(Visual, new(.4f, .13f, .06f), new(side * .62f, .03f, 1.83f), new("f06956"), true);
            foreach (float z in new[] { -1.15f, 1.15f })
            {
                var wheel = Art.Cylinder(Visual, .4f, .4f, .35f, new(side * 1.03f, -.42f, z), new("152833"), 10);
                wheel.Rotation = new(0, 0, Mathf.Pi / 2); wheels.Add(wheel);
                var hub = Art.Cylinder(Visual, .19f, .19f, .36f, new(side * 1.04f, -.42f, z), new("78979b"), 8);
                hub.Rotation = wheel.Rotation;
                var ray = new RayCast3D { Position = new(side * .8f, 0, z), TargetPosition = new(0, -stats.SuspensionLength - .3f, 0), CollisionMask = 1, Enabled = true };
                AddChild(ray); ray.AddException(this); suspension.Add(ray);
            }
        }
        turret = new Node3D { Position = new(0, .8f, 0) }; Visual.AddChild(turret);
        Art.Cylinder(turret, .43f, .5f, .25f, Vector3.Zero, new("304955"), 10);
        Art.Box(turret, new(.25f, .22f, 1.3f), new(0, .13f, -.6f), new("1b2e3c"));
        Art.Box(turret, new(.34f, .3f, .16f), new(0, .13f, -1.3f), new("a6b8b5"));
        Muzzle = new Marker3D { Position = new(0, .13f, -1.45f) }; turret.AddChild(Muzzle);
        MineMount = new Marker3D { Position = new(0, -.1f, 2.3f) }; AddChild(MineMount);
        Freeze = !simulate;
    }
    public void ResetAt(Vector3 position, float yaw)
    {
        resetPosition = position; resetRotation = new Quaternion(Vector3.Up, yaw);
        if (!Simulating) { GlobalTransform = new Transform3D(new Basis(resetRotation), position); resetPosition = null; }
        Sleeping = false; VisualOffset = Vector3.Zero; stuckTime = 0;
    }
    public override void _IntegrateForces(PhysicsDirectBodyState3D state)
    {
        if (reconciliation is { } fix)
        {
            state.Transform = new(new Basis(fix.Rotation), fix.Position);
            state.LinearVelocity = fix.Velocity; state.AngularVelocity = fix.Angular;
            reconciliation = null;
        }
        if (resetPosition is { } p)
        {
            state.Transform = new(new Basis(resetRotation), p); state.LinearVelocity = Vector3.Zero; state.AngularVelocity = Vector3.Zero; resetPosition = null;
        }
        if (!Simulating) return;
        LastSimulatedSequence = Command.Sequence;
        var basis = state.Transform.Basis.Orthonormalized(); var up = basis.Y; var forward = -basis.Z; var right = basis.X;
        var velocity = state.LinearVelocity;
        int contacts = 0;
        foreach (var ray in suspension)
        {
            ray.ForceRaycastUpdate();
            if (!ray.IsColliding()) continue;
            var hit = ray.GetCollisionPoint(); float distance = ray.GlobalPosition.DistanceTo(hit);
            float compression = Stats.SuspensionLength - distance;
            if (compression <= 0) continue;
            contacts++;
            var offset = ray.GlobalPosition - state.Transform.Origin;
            var pointVelocity = velocity + state.AngularVelocity.Cross(offset);
            float springForce = compression * Stats.Spring - pointVelocity.Dot(up) * Stats.Damping;
            state.ApplyForce(up * Mathf.Clamp(springForce, 0, Stats.Mass * 50), offset);
        }
        Grounded = contacts > 0;
        float dt = (float)state.Step;
        if (Driving && Health > 0)
        {
            float speed = velocity.Dot(forward);
            if (Grounded)
            {
                float throttle = Command.Throttle;
                float limit = throttle >= 0 ? Stats.MaxSpeed : Stats.ReverseSpeed;
                float motor = (Mathf.Abs(speed) < limit || Mathf.Sign(throttle) != Mathf.Sign(speed)) ? throttle * Stats.Acceleration : 0;
                state.ApplyCentralForce(forward * motor * Stats.Mass);
                if (Command.Brake > 0 || (throttle < 0 && speed > 1))
                    state.ApplyCentralForce(-forward * Mathf.Sign(speed) * Mathf.Min(Mathf.Abs(speed) / dt, Stats.Braking) * Stats.Mass * Mathf.Max(Command.Brake, -throttle));
                float grip = Stats.Grip * SurfaceGrip * (Command.Handbrake ? Stats.DriftFactor : 1);
                state.ApplyCentralForce(-right * velocity.Dot(right) * grip * Stats.Mass);
                float turn = -Command.Steering * Stats.TurnSpeed * Mathf.Clamp(Mathf.Abs(speed) / 7, 0, 1) * (speed < -1 ? -1 : 1);
                var angular = state.AngularVelocity;
                angular.Y = Mathf.Lerp(angular.Y, turn, Mathf.Clamp(dt * 9, 0, 1)); state.AngularVelocity = angular;
                state.ApplyCentralForce(-Vector3.Up * Stats.Mass * Mathf.Abs(speed) * .15f);
            }
            var correction = up.Cross(Vector3.Up);
            state.ApplyTorque(correction * Stats.Mass * (Grounded ? 14 : 8));
            if (up.Dot(Vector3.Up) < .2f && velocity.Length() < 3) stuckTime += dt; else stuckTime = 0;
            if (stuckTime > 2)
            {
                var yaw = state.Transform.Basis.GetEuler().Y;
                state.Transform = new(new Basis(new Quaternion(Vector3.Up, yaw)), state.Transform.Origin + Vector3.Up * 1.4f);
                state.AngularVelocity = Vector3.Zero; stuckTime = 0;
            }
        }
        else if (Grounded) state.ApplyCentralForce(-velocity * Stats.Mass * 6);
    }
    public override void _Process(double delta)
    {
        if (Visual is null) return;
        VisualOffset = VisualOffset.Lerp(Vector3.Zero, 1 - Mathf.Exp(-(float)delta * 12));
        Visual.Position = VisualOffset;
        float bodyYaw = GlobalBasis.GetEuler().Y;
        turret.Rotation = new(0, Mathf.Wrap(TurretYaw - bodyYaw, -Mathf.Pi, Mathf.Pi), 0);
        bool visible = Health > 0 && (Invulnerability <= 0 || (Time.GetTicksMsec() / 95) % 2 == 0);
        Visual.Visible = visible;
        skidTime -= delta;
        if (Driving && Grounded && Health > 0 && skidTime <= 0 && (Command.Handbrake || Mathf.Abs(LinearVelocity.Dot(GlobalBasis.X)) > 3) && LinearVelocity.Length() > 5)
        {
            skidTime = .07;
            foreach (float side in new[] { -.9f, .9f })
            {
                var mark = Art.Box(GetParent<Node3D>(), new(.2f, .012f, .9f), GlobalTransform * new Vector3(side, -.69f, 1.2f), new("213037"));
                mark.Quaternion = Quaternion;
                var fade = mark.CreateTween(); fade.TweenInterval(3); fade.TweenProperty(mark, "scale", Vector3.Zero, 1); fade.TweenCallback(Callable.From(mark.QueueFree));
            }
        }
        smokeTime -= delta;
        if (Health > 0 && Health < Stats.MaxHealth * .35f && smokeTime <= 0)
        {
            smokeTime = .18;
            var puff = Art.Box(GetParent<Node3D>(), new(.3f, .3f, .3f), GlobalPosition + Vector3.Up, new("4e5156"));
            var t = puff.CreateTween(); t.SetParallel(); t.TweenProperty(puff, "position", puff.Position + Vector3.Up * 2, .8); t.TweenProperty(puff, "scale", Vector3.Zero, .8); t.Chain().TweenCallback(Callable.From(puff.QueueFree));
        }
    }
}
