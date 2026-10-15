using Game.Core;
using Godot;

namespace Game.Client;

public partial class Main
{
    private void UpdateAimLine(Racer r)
    {
        if (headless || now < nextAimLine) return;
        nextAimLine = now + .05;
        if (aimLine is null)
        {
            aimLine = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(aimLine);
        }
        aimLine.Visible = r.Car.Health > 0 && phase == RacePhase.Racing;
        var direction = new Vector3(-Mathf.Sin(r.Car.TurretYaw), 0, -Mathf.Cos(r.Car.TurretYaw));
        var mesh = new ImmediateMesh(); mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, Art.Material(new("dfbc68"), true));
        Vector3 previous = default;
        float range = r.Build.Primary.Range;
        for (int i = 0; i <= 16; i++)
        {
            var p = r.Car.GlobalPosition + direction * (1.7f + range * i / 16);
            var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(p + Vector3.Up * 20, p - Vector3.Up * 20, 1));
            if (hit.Count > 0) p = (Vector3)hit["position"] + Vector3.Up * .08f;
            if (i > 0) { mesh.SurfaceAddVertex(previous); mesh.SurfaceAddVertex(p); }
            previous = p;
        }
        mesh.SurfaceEnd(); aimLine.Mesh = mesh;
    }
}
