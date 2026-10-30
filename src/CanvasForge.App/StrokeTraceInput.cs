using CanvasForge.Core;

namespace CanvasForge.App;

// Opt-in diagnostic sampling adds API/clock cost, so it is never a speed proof.
// Records stay in memory while painting input is held; disk IO is after release.
internal sealed class StrokeTraceInput(ICalibratedStrokeInput inner,Func<ScreenPoint> cursor,int limit=256):ITrackedStrokeInput
{
    internal sealed record Sample(string Phase,double Seconds,ScreenPoint[] Commands,ScreenPoint Cursor,double RequestedWait=0);
    internal List<Sample> Samples{get;}=[];
    internal bool Truncated{get;private set;}
    private int points;
    public double Seconds=>inner.Seconds;
    private void Record(string phase,ScreenPoint[] commands,double wait=0)
    {
        if(Samples.Count>=limit||points+commands.Length>4096){Truncated=true;return;}
        points+=commands.Length;Samples.Add(new(phase,Seconds,commands,cursor(),wait));
    }
    public void Move(IReadOnlyList<ScreenPoint> commands){inner.Move(commands);Record("move",commands.ToArray());}
    public void Button(bool up){inner.Button(up);Record(up?"released":"held",[]);}
    public void Shift(bool up){inner.Shift(up);Record(up?"shift-up":"shift-down",[]);}
    public void Wait(double seconds){inner.Wait(seconds);Record("wait",[],seconds);}
    public void ConfirmPosition(ScreenPoint expected)
    {
        if(inner is IPositionVerifiedStrokeInput verified)verified.ConfirmPosition(expected);
        Record("endpoint-confirmed",[expected]);
    }
    public void BeginCursorTracking(ScreenPoint expected)
    {if(inner is ITrackedStrokeInput tracked)tracked.BeginCursorTracking(expected);}
    public void EndCursorTracking()
    {if(inner is ITrackedStrokeInput tracked)tracked.EndCursorTracking();}
}
