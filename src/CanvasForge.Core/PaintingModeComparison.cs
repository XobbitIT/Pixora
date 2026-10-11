namespace CanvasForge.Core;

public sealed record PaintingModeCandidate(bool Adaptive,int Strokes,int CoveredPixels,int TargetPixels,double Seconds,bool[] UnplannedMask);
public sealed record PaintingModeComparison(PaintingModeCandidate Ordinary,PaintingModeCandidate Adaptive,bool? RecommendedAdaptive)
{
    public static PaintingModeComparison? Build(PaintPlan plan,Settings settings,CancellationToken token=default)
    {
        PaintingModeCandidate? Candidate(bool adaptive)
        {
            var s=settings.Clone();s.Set("adaptive_brush",adaptive);
            var result=MeasuredColorPlan.TryBuild(plan,s,token);if(result is null)return null;
            var groups=TransferSchedule.Build(plan,s,result.Groups,token);var order=TransferSchedule.Order(plan,groups);
            return new(adaptive,result.Groups.Values.Sum(g=>g.Count),result.CoveredPixels,result.TargetPixels,
                PaintTimingPlan.Build(s,groups,order,token:token).Sum(w=>w.PlannedSeconds),result.UnplannedMask);
        }
        var ordinary=Candidate(false);var mixed=Candidate(true);if(ordinary is null||mixed is null)return null;
        // Count alone cannot establish coverage dominance: compare every pixel.
        bool Dominates(PaintingModeCandidate a,PaintingModeCandidate b)=>a.UnplannedMask.Length==b.UnplannedMask.Length
            &&!a.UnplannedMask.Where((missing,i)=>missing&&!b.UnplannedMask[i]).Any();
        bool? recommendation=ordinary.Seconds<=mixed.Seconds&&Dominates(ordinary,mixed)?false:
            mixed.Seconds<=ordinary.Seconds&&Dominates(mixed,ordinary)?true:null;
        return new(ordinary,mixed,recommendation);
    }
}
