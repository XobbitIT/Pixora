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
                    bool vertical=line.X1==line.X2;
                    if(!vertical&&line.Y1!=line.Y2||line.X2<line.X1||line.Y2<line.Y1)throw new InvalidOperationException("Unexpected measured stroke direction.");
                    int left=vertical?line.Y1-rect.Top:line.X1-rect.Left,right=vertical?line.Y2-rect.Top:line.X2-rect.Left;
                    int transverse=vertical?line.X1-rect.Left:line.Y1-rect.Top;
                    int Index(int along,int row)=>vertical?along*rect.Width+row:row*rect.Width+along;
                    var mask=vertical?BrushSweep.Transpose(p.Solid):p.Solid;
                    int start=right,end=left;bool adds=false;
                    foreach(var row in mask)
                    {
                        int rowIndex=transverse+row.Y,begin=left+row.Left,limit=right+row.Right;
                        int first=begin,last=limit-1;
                        while(first<limit&&covered[Index(first,rowIndex)])first++;
                        if(first==limit)continue;
                        while(last>first&&covered[Index(last,rowIndex)])last--;
                        adds=true;
                        start=Math.Min(start,Math.Clamp(first-row.Left,left,right));
                        end=Math.Max(end,Math.Clamp(last-row.Right+1,left,right));
                    }
                    if(trim&&!adds){removed++;continue;}
                    if(trim)
                    {
                        end=Math.Max(start,end);
                        if(start!=left||end!=right){trimmed++;left=start;right=end;}
                    }
                    int added=0,repeated=0;
                    var actualLine=vertical?new ScreenLine(line.X1,rect.Top+left,line.X1,rect.Top+right)
                        :new ScreenLine(rect.Left+left,line.Y1,rect.Left+right,line.Y1);
                    var local=new ScreenLine(actualLine.X1-rect.Left,actualLine.Y1-rect.Top,actualLine.X2-rect.Left,actualLine.Y2-rect.Top);
                    foreach(var row in BrushSweep.Project(p.Solid,local))
                    {
                        int begin=row.Y*rect.Width+row.Left,limit=row.Y*rect.Width+row.Right;
                        for(int i=begin;i<limit;i++){if(covered[i])repeated++;else added++;}
                        Array.Fill(covered,true,begin,limit-begin);
                    }
                    lines.Add(op with{Line=actualLine});
                    var key=(op.Size,op.ShapeSlot);var prior=stats.GetValueOrDefault(key,new(op.Size,op.ShapeSlot,0,0,0));
                    stats[key]=prior with{Strokes=prior.Strokes+1,AddedSolidPixels=prior.AddedSolidPixels+added,RepeatedSolidPixels=prior.RepeatedSolidPixels+repeated};
                }
            }
            return(groups,removed,trimmed,stats.Values.OrderBy(p=>p.ShapeSlot).ThenByDescending(p=>p.Size).ToList());
        }
        double Cost(Dictionary<int,List<BrushStroke>> groups)
        {
            var batches=TransferSchedule.Build(plan,s,groups,token);
            return PaintTimingPlan.Build(s,batches,TransferSchedule.Order(plan,batches),token:token,forSelection:true).Sum(w=>w.PlannedSeconds);
        }
        var candidate=Scan(true);double before=Cost(source.Groups),after=Cost(candidate.Groups);
        // Removing a connector can alter batching. Keep the original ordering
        // if the resulting complete dispatch costs more in the timing model.
        if(after>before+1e-9){candidate=Scan(false);after=before;}
        var d=source.Diagnostics!;var choice=d.Choice is {} c?c with{SelectedStrokes=candidate.Groups.Values.Sum(g=>g.Count),SelectedSeconds=after}:null;
        return source with{Groups=candidate.Groups,Diagnostics=d with{Choice=choice,StrokeSavings=new(source.Groups.Values.Sum(g=>g.Count),candidate.Removed,candidate.Trimmed,before,after,candidate.Stats)}};
    }
}
