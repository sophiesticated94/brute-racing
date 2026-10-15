using Godot;

namespace Game.Client;

public partial class AimReticle : Control
{
    public override void _Process(double delta)
    {
        Position = GetViewport().GetMousePosition(); QueueRedraw();
    }
    public override void _Draw()
    {
        var color = new Color("fff1bb");
        DrawArc(Vector2.Zero, 9, 0, Mathf.Tau, 32, new Color(.05f, .1f, .14f, .9f), 4, true);
        DrawArc(Vector2.Zero, 9, 0, Mathf.Tau, 32, color, 1.5f, true);
        for (int i = 0; i < 4; i++)
        {
            var axis = new Vector2(Mathf.Cos(i * Mathf.Pi / 2), Mathf.Sin(i * Mathf.Pi / 2));
            DrawLine(axis * 12, axis * 17, color, 2, true);
        }
    }
}
