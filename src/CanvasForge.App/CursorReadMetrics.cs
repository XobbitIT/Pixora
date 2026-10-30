using System.Diagnostics;
using CanvasForge.Core;

namespace CanvasForge.App;

internal readonly record struct CursorReadCosts(long Reads,long FailedReads,double Seconds);
internal sealed class CursorReadMetrics
{
    private long reads,failed;
    private double seconds;
    internal CursorReadCosts Costs=>new(reads,failed,seconds);
    internal ScreenPoint Read(Func<ScreenPoint> read)
    {
        long started=Stopwatch.GetTimestamp();reads++;
        try{return read();}
        catch{failed++;throw;}
        finally{seconds+=Stopwatch.GetElapsedTime(started).TotalSeconds;}
    }
}
