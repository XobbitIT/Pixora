namespace CanvasForge.Core;

public sealed record BrushCoverageContribution(double Size,int ShapeSlot,int Strokes,int AddedSolidPixels,int RepeatedSolidPixels);
public sealed record MeasuredStrokeSavings(int OriginalStrokes,int RemovedStrokes,int TrimmedStrokes,double BeforeSeconds,double AfterSeconds,
    IReadOnlyList<BrushCoverageContribution> Contributions);

// Remove/shorten only commands whose guaranteed paint is supplied by earlier
// commands of the same color. Possible/uncertain paint never removes work.
internal static class MeasuredStrokeOptimization
{
    public static MeasuredColorResult Apply(PaintPlan plan,Settings s,MeasuredColorResult source,CancellationToken token)
    {
        var rect=s.Calibration.Rect("canvas");var covered=new bool[rect.Width*rect.Height];
        var profiles=BrushFootprints.Read(s,true).ToDictionary(p=>p.Id);
        (Dictionary<int,List<BrushStroke>> Groups,int Removed,int Trimmed,List<BrushCoverageContribution> Stats) Scan(bool trim)
        {
            var groups=new Dictionary<int,List<BrushStroke>>();int removed=0,trimmed=0;
            var stats=new Dictionary<(double Size,int Shape),BrushCoverageContribution>();
            foreach(var(color,strokes) in source.Groups)
            {
                Array.Clear(covered);var lines=groups[color]=[];
                foreach(var op in strokes)
                {
                    token.ThrowIfCancellationRequested();var p=profiles[op.ProfileId!];var line=op.Line;
                    if(line.Y1!=line.Y2||line.X2<line.X1)throw new InvalidOperationException("Unexpected measured stroke direction.");
                    int left=line.X1-rect.Left,right=line.X2-rect.Left,y=line.Y1-rect.Top;
                    int start=right,end=left;bool adds=false;
                    foreach(var row in p.Solid)
                    {
                        int offset=(y+row.Y)*rect.Width,begin=offset+left+row.Left,limit=offset+right+row.Right;
                        int first=begin,last=limit-1;
                        while(first<limit&&covered[first])first++;
                        if(first==limit)continue;
                        while(last>first&&covered[last])last--;
                        adds=true;
                        start=Math.Min(start,Math.Clamp(first-offset-row.Left,left,right));
                        end=Math.Max(end,Math.Clamp(last-offset-row.Right+1,left,right));
                    }
                    if(trim&&!adds){removed++;continue;}
                    if(trim)
                    {
                        end=Math.Max(start,end);
                        if(start!=left||end!=right){trimmed++;left=start;right=end;}
                    }
                    int added=0,repeated=0;
                    foreach(var row in p.Solid)
                    {
                        int begin=(y+row.Y)*rect.Width+left+row.Left,limit=(y+row.Y)*rect.Width+right+row.Right;
                        for(int i=begin;i<limit;i++){if(covered[i])repeated++;else added++;}
                        Array.Fill(covered,true,begin,limit-begin);
                    }
                    lines.Add(op with{Line=new(rect.Left+left,line.Y1,rect.Left+right,line.Y1)});
                    var key=(op.Size,op.ShapeSlot);var prior=stats.GetValueOrDefault(key,new(op.Size,op.ShapeSlot,0,0,0));
                    stats[key]=prior with{Strokes=prior.Strokes+1,AddedSolidPixels=prior.AddedSolidPixels+added,RepeatedSolidPixels=prior.RepeatedSolidPixels+repeated};
                }
            }
            return(groups,removed,trimmed,stats.Values.OrderBy(p=>p.ShapeSlot).ThenByDescending(p=>p.Size).ToList());
        }
        double Cost(Dictionary<int,List<BrushStroke>> groups)
        {
            var batches=TransferSchedule.Build(plan,s,groups,token);
            return PaintTimingPlan.Build(s,batches,TransferSchedule.Order(plan,batches),token:token).Sum(w=>w.PlannedSeconds);
        }
        var candidate=Scan(true);double before=Cost(source.Groups),after=Cost(candidate.Groups);
        // Removing a connector can alter batching. Keep the original ordering
        // if the resulting complete dispatch costs more in the timing model.
        if(after>before+1e-9){candidate=Scan(false);after=before;}
        var d=source.Diagnostics!;var choice=d.Choice is {} c?c with{SelectedStrokes=candidate.Groups.Values.Sum(g=>g.Count),SelectedSeconds=after}:null;
        return source with{Groups=candidate.Groups,Diagnostics=d with{Choice=choice,StrokeSavings=new(source.Groups.Values.Sum(g=>g.Count),candidate.Removed,candidate.Trimmed,before,after,candidate.Stats)}};
    }
}
