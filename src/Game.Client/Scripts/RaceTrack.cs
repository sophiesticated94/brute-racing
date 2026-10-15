using Game.Core;
using Godot;

namespace Game.Client;

public partial class RaceTrack : Node3D
{
    public TrackDefinition Definition { get; private set; } = null!;
    public Vector3[] Path { get; private set; } = [];
    public float Height(float angle)
    {
        float distance = Mathf.Abs(Mathf.Wrap(angle - 4.05f, -Mathf.Pi, Mathf.Pi));
        return .2f + Mathf.Max(0, 1 - distance / .62f) * 3.6f;
    }
    public Vector3 Point(float angle) => new(Mathf.Sin(angle) * Definition.RadiusX, Height(angle), Mathf.Cos(angle) * Definition.RadiusZ);
    public Vector3 Tangent(float angle) => (Point(angle + .005f) - Point(angle - .005f)).Normalized();
    public Vector3 GatePoint(int gate) => Point(gate * Mathf.Tau / Definition.Checkpoints);
    public Vector3 GateDirection(int gate) => Tangent(gate * Mathf.Tau / Definition.Checkpoints);
    public float YawAt(int gate) { var t = GateDirection(gate); return Mathf.Atan2(-t.X, -t.Z); }
    public Vector3 Spawn(int slot)
    {
        var tangent = GateDirection(0); var right = tangent.Cross(Vector3.Up).Normalized();
        return GatePoint(0) - tangent * (4 + slot / 2 * 4.5f) + right * (slot % 2 == 0 ? -2.3f : 2.3f) + Vector3.Up * .95f;
    }
    public float SurfaceGrip(Vector3 position)
    {
        float angle = Mathf.PosMod(Mathf.Atan2(position.X / Definition.RadiusX, position.Z / Definition.RadiusZ), Mathf.Tau);
        return angle is > 1.2f and < 1.8f ? .36f : 1;
    }
    public int NearestPath(Vector3 position)
    {
        int result = 0; float closest = float.MaxValue;
        for (int i = 0; i < Path.Length; i++)
        {
            float d = position.DistanceSquaredTo(Path[i]); if (d < closest) { closest = d; result = i; }
        }
        return result;
    }
    public void Build(TrackDefinition definition)
    {
        Definition = definition;
        var sand = new Color("b67b57"); var asphalt = new Color("283e48");
        Art.SolidBox(this, new(220, 1, 175), new(0, -1, 0), sand);
        Art.Cylinder(this, 26, 29, 1.3f, new(0, -.3f, 0), new("c59265"), 32);
        Path = Enumerable.Range(0, definition.Segments).Select(i => Point(i * Mathf.Tau / definition.Segments)).ToArray();
        for (int i = 0; i < definition.Segments; i++)
        {
            float angle = (i + .5f) * Mathf.Tau / definition.Segments;
            var a = Path[i]; var b = Path[(i + 1) % Path.Length]; var center = (a + b) * .5f;
            var color = angle is > 1.2f and < 1.8f ? new Color("4f7476") : asphalt;
            var road = new StaticBody3D { Position = center - Vector3.Up * .275f, CollisionLayer = 1, CollisionMask = 2 };
            AddChild(road); road.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(definition.Width, .55f, a.DistanceTo(b) + .15f) } });
            road.LookAt(road.Position + (b - a), Vector3.Up);
            var lateralA = Tangent(i * Mathf.Tau / definition.Segments).Cross(Vector3.Up).Normalized() * definition.Width / 2;
            var lateralB = Tangent((i + 1) * Mathf.Tau / definition.Segments).Cross(Vector3.Up).Normalized() * definition.Width / 2;
            var meshBuilder = new SurfaceTool(); meshBuilder.Begin(Mesh.PrimitiveType.Triangles);
            foreach (var vertex in new[] { a - lateralA, b + lateralB, a + lateralA, a - lateralA, b - lateralB, b + lateralB })
            { meshBuilder.SetNormal(Vector3.Up); meshBuilder.AddVertex(vertex + Vector3.Up * .015f); }
            AddChild(new MeshInstance3D { Mesh = meshBuilder.Commit(), MaterialOverride = Art.Material(color) });
            for (int side = -1; side <= 1; side += 2)
            {
                var edge = road.GlobalTransform * new Vector3(side * (definition.Width / 2 + .15f), .6f, 0);
                var rail = Art.SolidBox(this, new(.5f, .75f, a.DistanceTo(b) + .2f), edge, i % 4 < 2 ? new("e7c9a0") : new("dd6850"));
                rail.Quaternion = road.Quaternion;
                var curb = Art.Box(road, new(.55f, .03f, a.DistanceTo(b) + .1f), new(side * (definition.Width / 2 - .35f), .29f, 0), i % 4 < 2 ? new("f7dfad") : new("ed7457"));
            }
            if (i % 3 == 0) Art.Box(road, new(.13f, .02f, .9f), new(0, .29f, 0), new("6b8588"));
        }
        // Finish line and gantry: no collision across the road.
        for (int x = 0; x < 10; x++) for (int z = 0; z < 2; z++)
            Art.Box(this, new(.65f, .025f, 1.05f), new(-.65f + z * .65f, .23f, definition.RadiusZ - 4.7f + x * 1.05f), (x + z) % 2 == 0 ? new("f0ecd9") : new("172931"));
        Art.Box(this, new(.55f, 6, .55f), new(0, 3, definition.RadiusZ - 6.8f), new("e6bd7b"));
        Art.Box(this, new(.55f, 6, .55f), new(0, 3, definition.RadiusZ + 6.8f), new("e6bd7b"));
        Art.Box(this, new(.65f, .8f, 14.2f), new(0, 6, definition.RadiusZ), new("182e3e"));
        for (int z = -1; z <= 1; z++) Art.Box(this, new(.68f, .2f, .7f), new(.02f, 5.95f, definition.RadiusZ + z * 2), new("ffbd48"), true);
        for (int gate = 0; gate < definition.Checkpoints; gate++)
        {
            // These volumes describe each gate in the scene. Crossing is tested against
            // its plane in Game.Core; native overlap monitoring would count bumpers early.
            var area = new Area3D { Name = $"Gate_{gate:00}", Position = GatePoint(gate) + Vector3.Up * 2,
                CollisionLayer = 0, CollisionMask = 6, Monitoring = false, Monitorable = false };
            AddChild(area); area.Rotation = new(0, YawAt(gate), 0);
            area.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(definition.Width + .3f, 8, 1.3f) } });
        }
        var rng = new Random(413);
        for (int i = 0; i < 85; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.Tau;
            bool inner = i % 3 == 0;
            float scale = inner ? .35f + (float)rng.NextDouble() * .3f : 1.28f + (float)rng.NextDouble() * .65f;
            var p = new Vector3(Mathf.Sin(a) * definition.RadiusX * scale, 0, Mathf.Cos(a) * definition.RadiusZ * scale);
            float height = 1 + (float)rng.NextDouble() * (inner ? 5 : 11);
            var rock = Art.Cylinder(this, height * .25f, height * .65f, height, p + Vector3.Up * (height / 2 - .35f),
                i % 3 == 0 ? new("a5664b") : new("cc9266"), 5);
            rock.Rotation = new(.1f, a, .07f);
            if (i % 9 == 0)
            {
                Art.Cylinder(this, .15f, .22f, 3, p + new Vector3(2, 1.3f, 0), new("457c67"), 6);
                Art.Box(this, new(1.3f, .24f, .3f), p + new Vector3(2.4f, 1.8f, 0), new("457c67"));
            }
        }
        // Inner paddock and solar canopies.
        for (int i = 0; i < 4; i++)
        {
            Art.Box(this, new(5, 2.8f, 3), new(-12 + i * 7, 1, -8), new("d4b78d"));
            var canopy = Art.Box(this, new(5.8f, .2f, 4), new(-12 + i * 7, 2.8f, -8), new("305464")); canopy.RotationDegrees = new(8, 0, 0);
        }
    }
}
