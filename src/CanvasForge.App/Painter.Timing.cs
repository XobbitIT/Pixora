using CanvasForge.Core;

namespace CanvasForge.App;

internal enum PaintPhase { Countdown,Preparing,Drawing,Auditing,Finishing,Completed }
internal sealed partial class Painter
{
    private RemainingTime? timing;
    private RunTimeBreakdown? runTimes;
    private int timingDone,timingTotal;
    private PaintPhase timingPhase;
    private string timingStatus="";
    private double nextEtaLog;
    private EtaBasis? lastEtaBasis;
    private double countdownSeconds;
    private double? countdownStarted;
    private double ActiveSeconds=>clock.Elapsed.TotalSeconds-pausedSeconds-countdownSeconds
        -(countdownStarted is {} start?clock.Elapsed.TotalSeconds-start:0);
    private void BeginTiming(string id,PaintPhase phase,string message)
    {
        runTimes?.Switch(phase==PaintPhase.Finishing?"finish":id.StartsWith("color:")?"color":id.StartsWith("motion:")?"motion":"service");
        if(timing!.Contains(id))timing.Begin(id,ActiveSeconds);
        bool changed=phase!=timingPhase;
        timingPhase=phase;timingStatus=message;
        if(changed||phase!=PaintPhase.Drawing)ReportTiming();
    }
    private void ReportTiming(string? message=null,double countdown=0)
    {
        if(message is not null)timingStatus=message;
        double active=ActiveSeconds;
        var activeEstimate=timing!.Estimate(active);
        var estimate=activeEstimate;
        if(countdown>0)estimate=estimate with{Seconds=estimate.Seconds+countdown,LowerSeconds=estimate.LowerSeconds+countdown,UpperSeconds=estimate.UpperSeconds+countdown};
        report(new(timingDone,timingTotal,ActiveSeconds,estimate.Seconds,timingStatus,estimate,timingPhase,FinalCoverage,finalAuditDirectory,runTimes?.Read()));
        if(ActiveSeconds>=nextEtaLog||lastEtaBasis!=estimate.Basis||timingPhase==PaintPhase.Completed)
        {
            nextEtaLog=ActiveSeconds+2;lastEtaBasis=estimate.Basis;
            Log("eta_update",new{revision="rolling-timing-v4-explicit-clock",done=timingDone,total=timingTotal,
                phase=timingPhase.ToString(),activeSeconds=active,estimate,activeEstimate,countdownRemainingSeconds=countdown,
                estimateClock=countdown>0?"active-remaining-plus-countdown":"active-remaining",
                excludesPause=true,projectedActiveFinishSeconds=active+activeEstimate.Seconds,
                projectedActiveFinishLowerSeconds=active+activeEstimate.LowerSeconds,
                projectedActiveFinishUpperSeconds=active+activeEstimate.UpperSeconds,rates=timing.Rates(),
                warmup=RemainingTime.Warmup,motionWindow=RemainingTime.MotionWindow});
        }
    }
}
