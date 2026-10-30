using System.Diagnostics;
using CanvasForge.Core;

namespace CanvasForge.App;

// Observe each real issued packet. For production packets, retain every issued
// point but read the cursor once; do not fabricate per-point observations.
internal sealed class EvidenceSeriesTraceInput(ICalibratedStrokeInput inner,Func<ScreenPoint> cursor,int maxPacketPoints=1):ITrackedStrokeInput
{
    internal sealed record Reading(string Phase,long StartedQpc,long FinishedQpc,double Seconds,ScreenPoint? Command,
        ScreenPoint? Cursor,bool CursorReadSucceeded,double RequestedWait)
    {public int? Win32Error{get;init;}public ScreenPoint[]? IssuedPoints{get;init;}}
    internal List<Reading> Samples{get;}=[];
    public double Seconds=>inner.Seconds;
    private ScreenPoint? command;
    private void Record(string phase,long started,double wait=0,ScreenPoint[]? points=null)
    {
        if(Samples.Count>=8192)throw new InvalidDataException("Evidence trace limit reached; sample is incomplete.");
        ScreenPoint actual;
        try{actual=cursor();}
        catch(Exception error){Samples.Add(new(phase,started,Stopwatch.GetTimestamp(),Seconds,command,null,false,wait)
            {Win32Error=(error as CursorReadException)?.Win32Error,IssuedPoints=points});throw;}
        Samples.Add(new(phase,started,Stopwatch.GetTimestamp(),Seconds,command,actual,true,wait){IssuedPoints=points});
    }
    public void Move(IReadOnlyList<ScreenPoint> points)
    {
        if(points.Count<1||points.Count>maxPacketPoints)throw new InvalidDataException("Evidence packet exceeds its frozen motion profile.");
        var issued=points.ToArray();long started=Stopwatch.GetTimestamp();command=issued[^1];inner.Move(points);Record("move-issued",started,points:issued);
    }
    public void Button(bool up)
    {long started=Stopwatch.GetTimestamp();inner.Button(up);Record(up?"released":"held",started);}
    public void Wait(double seconds)
    {long started=Stopwatch.GetTimestamp();inner.Wait(seconds);Record("wait-finished",started,seconds);}
    public void Shift(bool up)=>throw new InvalidDataException("Shift is not part of the evidence emitter.");
    public void ConfirmPosition(ScreenPoint expected)
    {
        long started=Stopwatch.GetTimestamp();
        if(inner is not IPositionVerifiedStrokeInput verified)throw new InvalidOperationException("Evidence needs guarded endpoint confirmation.");
        verified.ConfirmPosition(expected);Record("endpoint-confirmed",started);
    }
    public void BeginCursorTracking(ScreenPoint expected)
    {
        if(inner is not ITrackedStrokeInput tracked)throw new InvalidOperationException("Evidence needs tracked input.");
        tracked.BeginCursorTracking(expected);
    }
    public void EndCursorTracking(){if(inner is ITrackedStrokeInput tracked)tracked.EndCursorTracking();}
}
