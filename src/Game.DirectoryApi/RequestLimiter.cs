namespace Game.DirectoryApi;

public sealed record RequestLimits(int Capacity=120,double RefillPerSecond=20,int EnqueueCapacity=8,double EnqueueRefillPerSecond=1);
public sealed class RequestLimiter(TimeProvider time,RequestLimits limits)
{
    private sealed class Bucket(double now,int general,int enqueue)
    {public double Updated=now,General=general,Enqueue=enqueue;}
    private readonly object gate=new();
    private readonly Dictionary<string,Bucket> buckets=[];
    public bool Accept(string ip,bool enqueue,out int retryAfter)
    {
        lock(gate)
        {
            double now=time.GetUtcNow().ToUnixTimeMilliseconds()/1000.0;
            if(!buckets.TryGetValue(ip,out var bucket))
            {
                if(buckets.Count>=10000)
                {
                    foreach(var old in buckets.Where(p=>now-p.Value.Updated>120).Select(p=>p.Key).ToArray())buckets.Remove(old);
                    if(buckets.Count>=10000){retryAfter=1;return false;}
                }
                buckets[ip]=bucket=new(now,limits.Capacity,limits.EnqueueCapacity);
            }
            double elapsed=Math.Max(0,now-bucket.Updated);bucket.Updated=now;
            bucket.General=Math.Min(limits.Capacity,bucket.General+elapsed*limits.RefillPerSecond);
            bucket.Enqueue=Math.Min(limits.EnqueueCapacity,bucket.Enqueue+elapsed*limits.EnqueueRefillPerSecond);
            retryAfter=(int)Math.Ceiling(Math.Max((1-bucket.General)/limits.RefillPerSecond,enqueue?(1-bucket.Enqueue)/limits.EnqueueRefillPerSecond:0));
            if(bucket.General<1 || enqueue&&bucket.Enqueue<1){retryAfter=Math.Max(1,retryAfter);return false;}
            bucket.General--;if(enqueue)bucket.Enqueue--;retryAfter=0;return true;
        }
    }
}
