namespace CanvasForge.Core;

public sealed record EditorRetryResult(EditorEvidenceResult Result,int Captures,bool Recovered);
public static class EditorEvidenceRetry
{
    public const int Captures=6;
    public const double IntervalSeconds=.1;
    public static bool FrameOnly(EditorEvidenceResult result)=>result.State==EditorEvidenceState.Ambiguous
        &&result.FailedGroups.SequenceEqual(new[]{"canvas-frame"});
    public static EditorRetryResult Run(EditorEvidenceResult first,Action release,Action wait,Func<EditorEvidenceResult> capture)
    {
        if(first.State==EditorEvidenceState.Present)return new(first,1,false);
        release();if(!FrameOnly(first))return new(first,1,false);
        var result=first;int clean=0;
        for(int i=1;i<Captures;i++)
        {
            wait();result=capture();
            if(result.State==EditorEvidenceState.Present)clean++;
            else {clean=0;if(!FrameOnly(result))return new(result,i+1,false);}
        }
        return new(result,Captures,clean>=2);
    }
}
