using CanvasForge.Core;

namespace CanvasForge.App;

internal sealed class StrokeCursorMismatchException(ScreenPoint expected,ScreenPoint actual,string phase)
    : InvalidOperationException("Cursor moved away from the last issued paint position.")
{
    internal ScreenPoint Expected {get;}=expected;
    internal ScreenPoint Actual {get;}=actual;
    internal string Phase {get;}=phase;
}

// Read-only checks, with no capture, disk IO, sleeping, repositioning or retry.
// They detect displacement between checks; they cannot undo an external stroke
// or guarantee that the cursor did not move away and back between two reads.
internal sealed class StrokeCursorGuard(Func<ScreenPoint> cursor,Action release)
{
    private ScreenPoint? expected;
    private IReadOnlyList<ScreenPoint>? pending;
    private int observedIndex;
    internal bool Active=>expected.HasValue;
    internal void Begin(ScreenPoint point)
    {
        if(Active){End();release();throw new InvalidOperationException("Nested stroke cursor tracking.");}
        expected=point;
    }
    internal void End(){expected=null;pending=null;observedIndex=-1;}
    internal void Check(string phase,bool requireEndpoint=false)
    {
        if(expected is not {} point)return;
        ScreenPoint actual;
        try{actual=cursor();}
        catch{End();release();throw;}
        if(pending is {} packet)
        {
            // SendInput reports insertion, not receipt by the game. A read may
            // still observe the old point or forward progress within this
            // exact packet. Never interpret those issued points as external
            // displacement, or accept arbitrary nearby coordinates.
            if(actual==packet[^1]){expected=actual;pending=null;observedIndex=-1;return;}
            if(!requireEndpoint)
            {
                if(observedIndex<0&&actual==point)return;
                for(int i=Math.Max(0,observedIndex);i<packet.Count;i++)
                    if(actual==packet[i]){observedIndex=i;return;}
            }
            point=packet[^1];
        }
        else if(actual==point)return;
        End();release();
        throw new StrokeCursorMismatchException(point,actual,phase);
    }
    // Called only AFTER a successful transport call. A rejected/partial send
    // must never advance the expected point and authorize the next packet.
    internal void Emitted(IReadOnlyList<ScreenPoint> points)
    {
        if(!Active)return;
        if(pending is not null||points.Count==0)
        {End();release();throw new InvalidOperationException("Unacknowledged or empty stroke cursor packet.");}
        // Dense emission owns a fresh immutable array for each packet. Retain
        // it only until acknowledgement; no extra per-point allocations.
        pending=points;observedIndex=-1;
    }
}
