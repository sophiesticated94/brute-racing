namespace Game.Core;

public static class MovementTuning
{
    public const float Gravity = 9.8f;
    public const float GravityScale = 1.8f;
    public const float LinearDamping = .12f;
    public const float AngularDamping = 2.5f;
    public const float SteeringResponse = 9;
    public const float SteeringFullSpeed = 7;
    public const float Downforce = .15f;
    public const float VisualTimeConstant = .1f;
    public const float TeleportDistance = 3.5f;
    public static float Decay(float delta) => 1 - MathF.Exp(-delta / VisualTimeConstant);
    public static float TurnTarget(float steering, float speed, float turnSpeed) =>
        -steering * turnSpeed * Math.Clamp(MathF.Abs(speed) / SteeringFullSpeed, 0, 1) * (speed < -1 ? -1 : 1);
}

public sealed class NetworkDiagnostics
{
    public double RttMs { get; private set; }
    public double JitterMs { get; private set; }
    public double MaxJitterMs { get; private set; }
    public int PingSamples { get; private set; }
    private double lastRtt, lastSnapshot = double.NaN;
    public void ObserveRtt(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || seconds > 30) return;
        double ms = seconds * 1000;
        if (PingSamples++ == 0) RttMs = ms;
        else
        {
            double jitter = Math.Abs(ms - lastRtt); JitterMs += (jitter - JitterMs) * .2;
            MaxJitterMs = Math.Max(MaxJitterMs, jitter); RttMs += (ms - RttMs) * .2;
        }
        lastRtt = ms;
    }
    public void ObserveSnapshot(double now) => lastSnapshot = now;
    public double SnapshotAgeMs(double now) => double.IsNaN(lastSnapshot) ? 0 : Math.Max(0, (now - lastSnapshot) * 1000 + RttMs / 2);
}
