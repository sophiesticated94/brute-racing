using Game.Core;

namespace Game.Core.Tests;

public class RefinementTests
{
    [Fact]
    public void VisualDecayHasTheSameResultAcrossFrameRates()
    {
        float sixty = 1, thirty = 1;
        for (int i = 0; i < 60; i++) sixty *= 1 - MovementTuning.Decay(1f / 60);
        for (int i = 0; i < 30; i++) thirty *= 1 - MovementTuning.Decay(1f / 30);
        Assert.Equal(sixty, thirty, 5);
        Assert.InRange(1 - MovementTuning.Decay(.1f), .3678f, .368f);
    }
    [Fact]
    public void DiagnosticsMeasureRttJitterAndSnapshotAge()
    {
        var d = new NetworkDiagnostics(); d.ObserveRtt(.1); d.ObserveRtt(.12); d.ObserveSnapshot(2);
        Assert.Equal(104, d.RttMs, 5); Assert.Equal(20, d.MaxJitterMs, 5);
        Assert.Equal(72, d.SnapshotAgeMs(2.02), 5);
        d.ObserveRtt(double.NaN); Assert.Equal(2, d.PingSamples);
    }
}
