using Godot;

namespace Game.Client;

public static class Art
{
    public static readonly Color[] Colors = [new("ffbe45"), new("50d7dd"), new("f57578"), new("ad98f9"), new("a8db6a"), new("e9e5d7"), new("ff914a"), new("5288d9")];
    public static StandardMaterial3D Material(Color color, bool glow = false) => new()
    {
        AlbedoColor = color, Roughness = .8f,
        EmissionEnabled = glow, Emission = color, EmissionEnergyMultiplier = glow ? 1.5f : 0
    };
    public static MeshInstance3D Box(Node3D parent, Vector3 size, Vector3 position, Color color, bool glow = false)
    {
        var mesh = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = Material(color, glow), Position = position };
        parent.AddChild(mesh); return mesh;
    }
    public static MeshInstance3D Cylinder(Node3D parent, float top, float bottom, float height, Vector3 position, Color color, int sides = 10)
    {
        var mesh = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = top, BottomRadius = bottom, Height = height, RadialSegments = sides }, MaterialOverride = Material(color), Position = position };
        parent.AddChild(mesh); return mesh;
    }
    public static StaticBody3D SolidBox(Node3D parent, Vector3 size, Vector3 position, Color color)
    {
        var body = new StaticBody3D { Position = position, CollisionLayer = 1, CollisionMask = 2 };
        parent.AddChild(body);
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        Box(body, size, Vector3.Zero, color); return body;
    }
    public static void Beam(Node3D parent, Vector3 from, Vector3 to, Color color, float width = .07f)
    {
        float length = from.DistanceTo(to);
        if (length < .02) return;
        var mesh = Box(parent, new(width, width, length), (from + to) * .5f, color, true);
        mesh.LookAt(to);
        var tween = mesh.CreateTween(); tween.TweenProperty(mesh, "scale", new Vector3(.05f, .05f, 1), .13);
        tween.TweenCallback(Callable.From(mesh.QueueFree));
    }
    public static void Burst(Node3D parent, Vector3 position, Color color, float radius = 2)
    {
        var sphere = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = .5f, Height = 1, RadialSegments = 12, Rings = 6 },
            MaterialOverride = Material(color, true), Position = position
        };
        parent.AddChild(sphere);
        var tween = sphere.CreateTween(); tween.SetParallel();
        tween.TweenProperty(sphere, "scale", Vector3.One * radius, .14);
        tween.Chain().TweenProperty(sphere, "scale", Vector3.One * .02f, .18);
        tween.Chain().TweenCallback(Callable.From(sphere.QueueFree));
        for (int i = 0; i < 7; i++)
        {
            float a = i * Mathf.Tau / 7;
            var spark = Box(parent, new(.15f, .15f, .4f), position, color, true);
            var t = spark.CreateTween(); t.SetParallel();
            t.TweenProperty(spark, "position", position + new Vector3(Mathf.Sin(a), .5f, Mathf.Cos(a)) * radius * 1.7f, .4);
            t.TweenProperty(spark, "scale", Vector3.Zero, .4);
            t.Chain().TweenCallback(Callable.From(spark.QueueFree));
        }
    }
    public static System.Numerics.Vector3 Numerics(Vector3 v) => new(v.X, v.Y, v.Z);
    public static Vector3 GodotVector(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
    public static System.Numerics.Quaternion Numerics(Quaternion q) => new(q.X, q.Y, q.Z, q.W);
    public static Quaternion GodotQuaternion(System.Numerics.Quaternion q) => new(q.X, q.Y, q.Z, q.W);
}
