using Godot;

namespace Game.Client;

public partial class RaceMap : Control
{
    private Vector2[] line = [];
    private (Vector2 Position, Color Color, bool Local)[] cars = [];
    public void SetTrack(Vector3[] path)
    {
        line = path.Append(path[0]).Select(p => new Vector2(p.X, p.Z) * 1.7f + new Vector2(112, 73)).ToArray();
    }
    public void UpdateCars((Vector3 Position, Color Color, bool Local)[] values)
    {
        cars = values.Select(v => (new Vector2(v.Position.X, v.Position.Z) * 1.7f + new Vector2(112, 73), v.Color, v.Local)).ToArray(); QueueRedraw();
    }
    public override void _Draw()
    {
        if (line.Length > 1)
        {
            DrawPolyline(line, new("48616a"), 14, true); DrawPolyline(line, new("233c48"), 10, true);
            DrawLine(line[0] + new Vector2(0, -7), line[0] + new Vector2(0, 7), new("e8eddf"), 2, true);
        }
        foreach (var car in cars)
        {
            if (car.Local) DrawCircle(car.Position, 6, new("f0f4de"));
            DrawCircle(car.Position, car.Local ? 4 : 3, car.Color);
        }
    }
}
