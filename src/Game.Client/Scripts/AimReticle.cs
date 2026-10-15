using Godot;

namespace Game.Client;

public partial class AimReticle : Control
{
    private double hitTime;
    public void Hit() => hitTime = .18;
    public override void _Process(double delta)
    {
        hitTime -= delta; Position = GetViewport().GetMousePosition(); QueueRedraw();
    }
    public override void _Draw()
    {
        var color = new Color("fff1bb");
        if (hitTime > 0)
            foreach (var axis in new[] { new Vector2(1, 1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(-1, -1) })
                DrawLine(axis * 5, axis * 10, new("ff7861"), 3, true);
        DrawArc(Vector2.Zero, 9, 0, Mathf.Tau, 32, new Color(.05f, .1f, .14f, .9f), 4, true);
        DrawArc(Vector2.Zero, 9, 0, Mathf.Tau, 32, color, 1.5f, true);
        for (int i = 0; i < 4; i++)
        {
            var axis = new Vector2(Mathf.Cos(i * Mathf.Pi / 2), Mathf.Sin(i * Mathf.Pi / 2));
            DrawLine(axis * 12, axis * 17, color, 2, true);
        }
    }
}
