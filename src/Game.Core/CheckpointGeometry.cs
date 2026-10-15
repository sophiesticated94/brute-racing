using System.Numerics;

namespace Game.Core;

public static class CheckpointGeometry
{
    public static bool CrossesForward(Vector3 previous, Vector3 current, Vector3 center,
        Vector3 forward, float halfWidth, float halfHeight)
    {
        var lateral = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        return Vector3.Dot(previous - center, forward) < 0 && Vector3.Dot(current - center, forward) >= 0 &&
            Vector3.DistanceSquared(previous, current) <= 144 &&
            MathF.Abs(Vector3.Dot(current - center, lateral)) <= halfWidth &&
            MathF.Abs(current.Y - center.Y) <= halfHeight;
    }
}
