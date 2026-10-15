using System.Numerics;

namespace Game.Core;

public readonly record struct PredictionState(Vector3 Position, Vector3 Velocity, float Yaw, float AngularSpeed = 0);
public readonly record struct PredictedInput(PlayerInputCommand Command, bool Grounded, float SurfaceGrip, float GroundHeight);

// A bounded local approximation for replaying unacknowledged commands. Jolt on the host
// resolves collisions; replay deliberately does not rewind other cars or the world.
public static class MovementPrediction
{
    public static PredictionState Step(PredictionState state, PredictedInput input, CarDefinition car, float dt = 1f / 60)
    {
        var c = input.Command; float yaw = state.Yaw, angular = state.AngularSpeed;
        var forward = new Vector3(-MathF.Sin(yaw), 0, -MathF.Cos(yaw));
        var right = new Vector3(MathF.Cos(yaw), 0, -MathF.Sin(yaw));
        var velocity = state.Velocity;
        if (input.Grounded)
        {
            float speed = Vector3.Dot(velocity, forward);
            if (MathF.Abs(speed) < (c.Throttle >= 0 ? car.MaxSpeed : car.ReverseSpeed) || MathF.Sign(c.Throttle) != MathF.Sign(speed))
                velocity += forward * c.Throttle * car.Acceleration * dt;
            velocity -= right * Vector3.Dot(velocity, right) * car.Grip * input.SurfaceGrip * (c.Handbrake ? car.DriftFactor : 1) * dt;
            if (c.Brake > 0 || (c.Throttle < 0 && speed > 1))
                velocity -= forward * MathF.Sign(speed) * MathF.Min(MathF.Abs(speed) / dt, car.Braking) * MathF.Max(c.Brake, -c.Throttle) * dt;
            float turn = -c.Steering * car.TurnSpeed * Math.Clamp(MathF.Abs(speed) / 7, 0, 1) * (speed < -1 ? -1 : 1);
            angular += (turn - angular) * Math.Clamp(dt * 9, 0, 1);
            float springAcceleration = 4 * car.Spring / car.Mass;
            float equilibriumHeight = car.SuspensionLength - 9.8f * 1.8f / springAcceleration;
            velocity.Y += ((input.GroundHeight + equilibriumHeight - state.Position.Y) * springAcceleration - velocity.Y * 4 * car.Damping / car.Mass) * dt;
        }
        else velocity.Y -= 9.8f * 1.8f * dt;
        velocity *= 1 - .12f * dt;
        angular *= 1 - 2.5f * dt;
        yaw += angular * dt;
        return new(state.Position + velocity * dt, velocity, yaw, angular);
    }
}
