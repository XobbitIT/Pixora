namespace CanvasForge.Core;

// A failed reference never permits fast input. At most one fresh reference per
// pair, with a shared run budget; masks, color tolerance and envelope stay fixed.
public sealed class ProbeControlRetry
{
    public const int MaximumRetries=8;
    private int remaining;
    public ProbeControlRetry(int spareAreas)=>remaining=Math.Clamp(spareAreas,0,MaximumRetries);
    public (T Item,ProbeAnalysisResult Result) Measure<T>(Func<T> next,Func<T,ProbeAnalysisResult> measure,
        Func<bool> freshAreaAvailable,Action<ProbeAnalysisResult>? retrying=null)
    {
        var item=next();var result=measure(item);
        if(remaining>0&&result.Failure==ProbeFailure.UncertainPixels&&result.CoreCoverage is {Covered:>0,Missing:0,Unknown:>0,Reference:not null}
            &&freshAreaAvailable())
        {
            remaining--;retrying?.Invoke(result);item=next();result=measure(item);
        }
        return(item,result);
    }
}
