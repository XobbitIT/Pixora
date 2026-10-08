namespace CanvasForge.Core;

public enum EtaBasis { Planned, Measured, Mixed, Complete }
public sealed record TimedWork(string Id,string RateKey,double PlannedSeconds,bool Motion);
public sealed record EtaEstimate(double Seconds,EtaBasis Basis,int MotionSamples,int WindowSamples,
    int RemainingOperations,int UnmeasuredOperations,double MeanMotionMs,double MotionRatio,
    double OperationOverheadMs=0,int OperationOverheadSamples=0);
public sealed record EtaRate(string Key,bool Motion,int WindowSamples,double MeanSeconds,double Ratio);

// Forecasts remaining work, not elapsed / lifetime Done. A fresh instance is used
// on each START/RESUME. Pauses, countdowns and aborted strokes are not observations.
public sealed class RemainingTime
{
    public const int Warmup=20;
    public const int MotionWindow=50;
    public const int MinimumRouteSamples=5;
    public const int OverheadWindow=8;
    private sealed class Bucket(bool motion)
    {
        public bool Motion {get;}=motion;
        public int Count;
        public double Planned;
        public Queue<(double Planned,double Actual)> Samples {get;}=new();
        public double SamplePlanned,SampleActual;
        public void Observe(double planned,double actual)
        {
            Samples.Enqueue((planned,actual));SamplePlanned+=planned;SampleActual+=actual;
            if(Samples.Count>(Motion?MotionWindow:OverheadWindow))
            {var old=Samples.Dequeue();SamplePlanned-=old.Planned;SampleActual-=old.Actual;}
        }
    }
    private readonly Dictionary<string,TimedWork> pending=new();
    private readonly Dictionary<string,Bucket> buckets=new();
    private readonly Bucket recentMotion=new(true);
    private int motionSamples;
    private readonly Queue<double> operationOverhead=new();
    private double overheadSum;
    private string? completedMotion;
    private (string Id,double Started)? active;
    public RemainingTime(IEnumerable<TimedWork> work)
    {
        foreach(var item in work)
        {
            if(string.IsNullOrEmpty(item.Id)||string.IsNullOrEmpty(item.RateKey)||!double.IsFinite(item.PlannedSeconds)||item.PlannedSeconds<=0
                ||!pending.TryAdd(item.Id,item))throw new ArgumentException("Invalid or duplicate timed work.");
            if(!buckets.TryGetValue(item.RateKey,out var bucket))buckets[item.RateKey]=bucket=new(item.Motion);
            if(bucket.Motion!=item.Motion)throw new ArgumentException("Timing key mixes motion and overhead.");
            bucket.Count++;bucket.Planned+=item.PlannedSeconds;
        }
    }
    public bool Contains(string id)=>pending.ContainsKey(id);
    public void Begin(string id,double activeSeconds)
    {
        CheckSeconds(activeSeconds);
        if(!pending.ContainsKey(id))throw new ArgumentException("Unknown timed work.");
        active=(id,activeSeconds);
    }
    public bool Complete(string id,double actualSeconds)
    {
        CheckSeconds(actualSeconds);
        if(!pending.Remove(id,out var work))return false;
        Remove(work);var bucket=buckets[work.RateKey];bucket.Observe(work.PlannedSeconds,actualSeconds);
        if(work.Motion){motionSamples++;recentMotion.Observe(work.PlannedSeconds,actualSeconds);completedMotion=id;}
        return true;
    }
    // Called once after the completed stroke's checkpoint/report work. Controls,
    // color changes, Draw, pauses and failed attempts are excluded by the caller.
    public bool RecordOperationOverhead(string id,double seconds)
    {
        CheckSeconds(seconds);
        if(completedMotion!=id)return false;
        completedMotion=null;operationOverhead.Enqueue(seconds);overheadSum+=seconds;
        if(operationOverhead.Count>MotionWindow)overheadSum-=operationOverhead.Dequeue();
        return true;
    }
    public bool Skip(string id)
    {
        if(!pending.Remove(id,out var work))return false;
        Remove(work);return true;
    }
    private void Remove(TimedWork work)
    {
        var b=buckets[work.RateKey];b.Count--;b.Planned=b.Count==0?0:Math.Max(0,b.Planned-work.PlannedSeconds);
        if(active?.Id==work.Id)active=null;
    }
    private bool Measured(Bucket b)=>b.Motion?motionSamples>=Warmup&&b.Samples.Count>=MinimumRouteSamples:b.Samples.Count>0;
    private double Cost(Bucket b,double planned,int count)
    {
        if(b.Motion&&motionSamples>=Warmup&&b.Samples.Count>0)
        {
            // Rare Sizes can finish before collecting five samples. Blend only
            // their own observations; keep the Mixed label until fully measured.
            double confidence=Math.Min(1,b.Samples.Count/(double)MinimumRouteSamples);
            return planned*(1+confidence*(b.SampleActual/b.SamplePlanned-1));
        }
        return Measured(b)?count*b.SampleActual/b.Samples.Count:planned;
    }
    public EtaEstimate Estimate(double activeSeconds)
    {
        CheckSeconds(activeSeconds);double seconds=0;int operations=0,unknown=0;
        foreach(var bucket in buckets.Values)
        {
            seconds+=Cost(bucket,bucket.Planned,bucket.Count);
            if(bucket.Motion){operations+=bucket.Count;if(!Measured(bucket))unknown+=bucket.Count;}
        }
        if(active is { } running&&pending.TryGetValue(running.Id,out var current))
            seconds-=Math.Min(Cost(buckets[current.RateKey],current.PlannedSeconds,1),Math.Max(0,activeSeconds-running.Started));
        double overheadMean=operationOverhead.Count==0?0:overheadSum/operationOverhead.Count;
        if(motionSamples>=Warmup)seconds+=operations*overheadMean;
        var basis=pending.Count==0?EtaBasis.Complete:motionSamples<Warmup?EtaBasis.Planned:unknown>0?EtaBasis.Mixed:EtaBasis.Measured;
        return new(Math.Max(0,seconds),basis,motionSamples,recentMotion.Samples.Count,operations,unknown,
            recentMotion.Samples.Count==0?0:recentMotion.SampleActual*1000/recentMotion.Samples.Count,
            recentMotion.SamplePlanned==0?1:recentMotion.SampleActual/recentMotion.SamplePlanned,
            overheadMean*1000,operationOverhead.Count);
    }
    public List<EtaRate> Rates()=>buckets.Select(x=>new EtaRate(x.Key,x.Value.Motion,x.Value.Samples.Count,
        x.Value.Samples.Count==0?0:x.Value.SampleActual/x.Value.Samples.Count,
        x.Value.SamplePlanned==0?1:x.Value.SampleActual/x.Value.SamplePlanned)).ToList();
    private static void CheckSeconds(double seconds)
    {if(!double.IsFinite(seconds)||seconds<0)throw new ArgumentOutOfRangeException(nameof(seconds));}
}
