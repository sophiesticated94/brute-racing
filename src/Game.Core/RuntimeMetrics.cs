namespace Game.Core;

public sealed class RuntimeMetrics
{
    private readonly List<double> frames=new(16384),ticks=new(16384),physics=new(16384);
    private readonly long allocatedStart=GC.GetTotalAllocatedBytes();
    private double elapsed;
    private int peakNodes,peakBodies,peakProjectiles,peakEffects;
    public void Frame(double seconds,double physicsSeconds,int nodes,int bodies,int projectiles,int effects)
    {
        if(!double.IsFinite(seconds)||seconds<=0)return;
        elapsed+=seconds;
        // Startup is reported by the legacy metrics; percentiles describe the running game after warmup.
        if(elapsed>=3){frames.Add(seconds*1000);physics.Add(physicsSeconds*1000);}
        peakNodes=Math.Max(peakNodes,nodes);peakBodies=Math.Max(peakBodies,bodies);
        peakProjectiles=Math.Max(peakProjectiles,projectiles);peakEffects=Math.Max(peakEffects,effects);
    }
    public void Tick(double seconds){if(elapsed>=3)ticks.Add(seconds*1000);}
    public object Report()=>new{warmupSeconds=3,frame=Distribution(frames),enginePhysics=Distribution(physics),sessionTick=Distribution(ticks),
        managedAllocatedBytes=GC.GetTotalAllocatedBytes()-allocatedStart,managedBytesPerSecond=elapsed>0?(GC.GetTotalAllocatedBytes()-allocatedStart)/elapsed:0,
        peakNodes,peakBodies,peakProjectiles,peakEffects};
    private static object Distribution(List<double> samples)
    {
        var sorted=samples.Order().ToArray();
        double Percentile(double p)=>sorted.Length==0?0:sorted[(int)Math.Clamp(Math.Ceiling(p*sorted.Length)-1,0,sorted.Length-1)];
        return new{samples=sorted.Length,meanMs=sorted.Length>0?sorted.Average():0,p95Ms=Percentile(.95),p99Ms=Percentile(.99),maxMs=Percentile(1)};
    }
}
