namespace Game.Core;

public enum RacePhase : byte { Lobby, Countdown, Racing, Podium }

public sealed class RaceProgress(int checkpointCount, int totalLaps)
{
    public int NextCheckpoint { get; private set; } = 1;
    public int LastCheckpoint { get; private set; }
    public int CompletedLaps { get; private set; }
    public int PassedGates { get; private set; }
    public double? FinishTime { get; private set; }
    public bool Finished => FinishTime.HasValue;

    public bool Pass(int checkpoint, float forwardSpeed, double raceTime)
    {
        if (Finished || checkpoint != NextCheckpoint || forwardSpeed <= 0.25f) return false;
        LastCheckpoint = checkpoint;
        PassedGates++;
        NextCheckpoint = (checkpoint + 1) % checkpointCount;
        if (checkpoint == 0 && ++CompletedLaps >= totalLaps) FinishTime = raceTime;
        return true;
    }
}

public readonly record struct Standing(int PlayerId, int PassedGates, float DistanceToNext, double? FinishTime);

public static class RaceRanking
{
    public static int[] Rank(IEnumerable<Standing> racers) => racers
        .OrderBy(x => x.FinishTime.HasValue ? 0 : 1)
        .ThenBy(x => x.FinishTime ?? double.MaxValue)
        .ThenByDescending(x => x.PassedGates)
        .ThenBy(x => x.DistanceToNext)
        .ThenBy(x => x.PlayerId).Select(x => x.PlayerId).ToArray();
}

public sealed class RaceClock
{
    public RacePhase Phase { get; private set; } = RacePhase.Lobby;
    public double Elapsed { get; private set; }
    public double Remaining { get; private set; }
    private double? firstFinish;
    public void Start() { Phase = RacePhase.Countdown; Remaining = 3; Elapsed = 0; firstFinish = null; }
    public void NotifyFinish() { if (Phase == RacePhase.Racing) firstFinish ??= Elapsed; }
    public void Update(double dt, bool allFinished)
    {
        if (Phase == RacePhase.Countdown)
        {
            Remaining -= dt;
            if (Remaining <= 0) { Phase = RacePhase.Racing; Remaining = 0; }
        }
        else if (Phase == RacePhase.Racing)
        {
            Elapsed += dt;
            Remaining = firstFinish.HasValue ? Math.Max(0, 30 - (Elapsed - firstFinish.Value)) : 0;
            if (allFinished || (firstFinish.HasValue && Remaining <= 0)) Phase = RacePhase.Podium;
        }
    }
}
