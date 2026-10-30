namespace CanvasForge.Core;

public sealed record RunTimeSummary(double Wall,double Colors,double Motion,double Service,double Pause)
{
    public double Countdown{get;init;}
    public double Finish{get;init;}
    public double Active=>Wall-Pause-Countdown;
}

// A monotonic, exclusive phase clock. InputCosts are nested diagnostics and are
// never added to these totals. Countdown and finish have their own buckets;
// service includes setup, audit and recovery. Pause suspends the active bucket.
public sealed class RunTimeBreakdown
{
    private readonly Func<double> now;
    private readonly double started;
    private double last,colors,motion,service,pause,countdown,finish;
    public RunTimeBreakdown(Func<double> clock){now=clock;started=last=now();}
    private string phase="service",resumePhase="service";
    public void Switch(string next)
    {
        if(next is not ("color" or "motion" or "service" or "pause" or "countdown" or "finish"))throw new ArgumentException("Unknown timing phase.");
        Accumulate();phase=next;
    }
    private void Accumulate()
    {
        double current=now(),elapsed=current-last;
        if(!double.IsFinite(current)||elapsed<0)throw new InvalidOperationException("Timing clock must be monotonic.");
        switch(phase){case "color":colors+=elapsed;break;case "motion":motion+=elapsed;break;case "pause":pause+=elapsed;break;case "countdown":countdown+=elapsed;break;case "finish":finish+=elapsed;break;default:service+=elapsed;break;}
        last=current;
    }
    public void Pause(){resumePhase=phase;Switch("pause");}
    public void Resume()=>Switch(resumePhase);
    public RunTimeSummary Read(){Accumulate();return new(last-started,colors,motion,service,pause){Countdown=countdown,Finish=finish};}
}
