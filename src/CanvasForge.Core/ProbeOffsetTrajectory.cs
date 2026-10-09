namespace CanvasForge.Core;

// Diagnostics only. A slice with several valid core centres has no unique offset;
// never pick a centre or bridge such slices when counting trajectory transitions.
public sealed record ProbeOffsetTrajectory(int ResolvedSlices,int AmbiguousSlices,int UnresolvedSlices,
    int ComparablePairs,int Transitions,int MaximumJump,int[] DominantOffsets,int DominantCount,
    int? MinimumOffset,int? MaximumOffset,int?[] OffsetsBySlice,
    int? LongestStableRun=null,double? TransitionRate=null)
{
    public static ProbeOffsetTrajectory Measure(IReadOnlyList<int[]> supportedOffsets,int maximumOffset=ProbeSpatialCalibration.MaxOffset)
    {
        ArgumentNullException.ThrowIfNull(supportedOffsets);
        if(maximumOffset is <0 or >512)throw new ArgumentOutOfRangeException(nameof(maximumOffset));
        var resolved=new int?[supportedOffsets.Count];int ambiguous=0,unresolved=0;
        for(int i=0;i<supportedOffsets.Count;i++)
        {
            var candidates=supportedOffsets[i];
            if(candidates is null||candidates.Length>2*maximumOffset+1
                ||candidates.Any(o=>o < -maximumOffset || o > maximumOffset))
                throw new ArgumentException("Invalid diagnostic offsets.");
            var unique=candidates.Distinct().ToArray();
            if(unique.Length==1)resolved[i]=unique[0];
            else if(unique.Length>1)ambiguous++;
            else unresolved++;
        }
        var values=resolved.OfType<int>().ToArray();int pairs=0,transitions=0,jump=0;
        for(int i=1;i<resolved.Length;i++)
            if(resolved[i-1] is int a&&resolved[i] is int b)
            {pairs++;if(a!=b)transitions++;jump=Math.Max(jump,Math.Abs(b-a));}
        int run=0,longest=0;
        for(int i=0;i<resolved.Length;i++)
        {
            // Ambiguous/missing slices break a run, even if the next offset is equal.
            run=resolved[i] is int value?(i>0&&resolved[i-1]==value?run+1:1):0;
            longest=Math.Max(longest,run);
        }
        var counts=values.GroupBy(x=>x).ToArray();int dominant=counts.Length==0?0:counts.Max(x=>x.Count());
        return new(values.Length,ambiguous,unresolved,pairs,transitions,jump,
            counts.Where(x=>x.Count()==dominant).Select(x=>x.Key).Order().ToArray(),dominant,
            values.Length==0?null:values.Min(),values.Length==0?null:values.Max(),resolved,
            longest,pairs==0?null:(double)transitions/pairs);
    }
}
