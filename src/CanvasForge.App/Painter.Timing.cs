using CanvasForge.Core;

namespace CanvasForge.App;

internal enum PaintPhase { Countdown,Preparing,Drawing,Auditing,Finishing,Completed }
internal sealed partial class Painter
{
    private RemainingTime? timing;
    private int timingDone,timingTotal;
    private PaintPhase timingPhase;
    private string timingStatus="";
    private double nextEtaLog;
    private EtaBasis? lastEtaBasis;
    private double ActiveSeconds=>clock.Elapsed.TotalSeconds-pausedSeconds;
    private void BeginTiming(string id,PaintPhase phase,string message)
    {
        if(timing!.Contains(id))timing.Begin(id,ActiveSeconds);
        bool changed=phase!=timingPhase;
        timingPhase=phase;timingStatus=message;
        if(changed||phase!=PaintPhase.Drawing)ReportTiming();
    }
    private void ReportTiming(string? message=null,double countdown=0)
    {
        if(message is not null)timingStatus=message;
        var estimate=timing!.Estimate(ActiveSeconds);
        if(countdown>0)estimate=estimate with{Seconds=estimate.Seconds+countdown};
        report(new(timingDone,timingTotal,ActiveSeconds,estimate.Seconds,timingStatus,estimate,timingPhase));
        if(ActiveSeconds>=nextEtaLog||lastEtaBasis!=estimate.Basis||timingPhase==PaintPhase.Completed)
        {
            nextEtaLog=ActiveSeconds+2;lastEtaBasis=estimate.Basis;
            Log("eta_update",new{revision="rolling-timing-v1",done=timingDone,total=timingTotal,
                phase=timingPhase.ToString(),activeSeconds=ActiveSeconds,estimate,rates=timing.Rates(),
                warmup=RemainingTime.Warmup,motionWindow=RemainingTime.MotionWindow});
        }
    }
}
