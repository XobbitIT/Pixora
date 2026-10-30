namespace CanvasForge.Core;

// Fixed-length full-stroke kernels. Neither endpoint nor interior paint is
// inferred from a dot. Small residual commands retain their existing route.
public static class StrokeProfilePlan
{
    public static MeasuredColorResult? Build(PaintPlan plan,Settings s,CancellationToken token=default)
    {
        var timer=System.Diagnostics.Stopwatch.StartNew();var rect=s.Calibration.Rect("canvas");
        var baseDot=DrawingWorkflow.PaintingProfiles(s).Where(p=>p.Size<=3&&p.SolidCore.Valid)
            .OrderByDescending(p=>p.Size==s.Number("precision_brush_size",3)).ThenBy(p=>p.Size).FirstOrDefault();
        if(baseDot is null)return null;
        var ordinary=BrushMotionEvidence.Route(s);ordinary.Set("adaptive_brush",false);ordinary.Set("precision_brush_size",baseDot.Size);
        ordinary.Set("brush_shape_slot",baseDot.ShapeSlot);
        var baseline=MeasuredColorPlan.BuildGeometry(plan,ordinary,token);if(baseline is null)return null;
        int w=rect.Width,h=rect.Height;var labels=new int[w*h];Array.Fill(labels,-1);
        var xb=Coverage.Partition(0,w,plan.Width);var yb=Coverage.Partition(0,h,plan.Height);
        for(int y=0;y<plan.Height;y++)for(int x=0;x<plan.Width;x++)for(int py=yb[y];py<yb[y+1];py++)
            Array.Fill(labels,plan.Indices[y*plan.Width+x],py*w+xb[x],xb[x+1]-xb[x]);
        var covered=new bool[w*h];var groups=plan.Counts.Keys.ToDictionary(c=>c,_=>new List<BrushStroke>());
        var contributions=new List<BrushCoverageContribution>();int removed=0;
        foreach(var p in StrokeProfiles.Read(s).Where(p=>p.Size>baseDot.Size&&p.Size<=s.Number("adaptive_max_size",20))
            .OrderByDescending(p=>p.Solid.Sum(r=>r.Right-r.Left)))
        {
            var bounds=BrushFootprints.Bounds(p.Possible);var core=BrushFootprints.Core(p.Solid);
            int across=Math.Max(1,p.Vertical?core.Width:core.Height),along=Math.Max(1,p.Vertical?core.Height:core.Width),count=0,added=0,repeated=0;
            for(int y=Math.Max(0,-bounds.Top);y<=Math.Min(h-1,h-bounds.Bottom);y+=p.Vertical?along:across)
            for(int x=Math.Max(0,-bounds.Left);x<=Math.Min(w-1,w-bounds.Right);x+=p.Vertical?across:along)
            {
                token.ThrowIfCancellationRequested();
                if(x+(p.Vertical?0:p.Length)>=w||y+(p.Vertical?p.Length:0)>=h)continue;
                int color=labels[(y+p.Solid[0].Y)*w+x+p.Solid[0].Left];if(color<0||!groups.ContainsKey(color))continue;
                bool safe=true,adds=false;
                foreach(var row in p.Possible)
                {for(int px=row.Left;px<row.Right;px++)if(labels[(y+row.Y)*w+x+px]!=color){safe=false;break;}if(!safe)break;}
                if(!safe)continue;
                foreach(var row in p.Solid)for(int px=row.Left;px<row.Right;px++)adds|=!covered[(y+row.Y)*w+x+px];
                if(!adds)continue;
                foreach(var row in p.Solid)for(int px=row.Left;px<row.Right;px++){int i=(y+row.Y)*w+x+px;if(covered[i])repeated++;else added++;covered[i]=true;}
                var line=new ScreenLine(rect.Left+x,rect.Top+y,rect.Left+x+(p.Vertical?0:p.Length),rect.Top+y+(p.Vertical?p.Length:0));
                groups[color].Add(new(line,p.Size,Math.Max(Math.Abs(bounds.Top),Math.Abs(bounds.Left)),0,p.Shape,p.DotId){StrokeProfileId=p.Id});count++;
            }
            contributions.Add(new(p.Size,p.Shape,count,added,repeated));
        }
        int baseCount=0,baseAdded=0,baseRepeated=0;
        foreach(var(color,ops) in baseline.Groups)foreach(var op in ops)
        {
            token.ThrowIfCancellationRequested();
            int start=op.Line.X1-rect.Left,end=op.Line.X2-rect.Left,y=op.Line.Y1-rect.Top;
            var needed=new List<int>();var pending=new HashSet<int>();
            for(int x=start;x<=end;x++)
            {
                if(!baseDot.Solid.Any(r=>Enumerable.Range(r.Left,r.Right-r.Left).Any(px=>!covered[(y+r.Y)*w+x+px])))continue;
                needed.Add(x);
                foreach(var row in baseDot.Solid)for(int px=row.Left;px<row.Right;px++)
                {int index=(y+row.Y)*w+x+px;if(!covered[index])pending.Add(index);covered[index]=true;}
            }
            if(needed.Count==0){removed++;continue;}
            for(int i=0;i<needed.Count;i++)
            {
                int left=needed[i],right=left;
                while(i+1<needed.Count&&needed[i+1]-right<=TransferSchedule.MaximumConnector)right=needed[++i];
                var local=new ScreenLine(left,y,right,y);
                groups[color].Add(op with{Line=new(rect.Left+left,rect.Top+y,rect.Left+right,rect.Top+y)});baseCount++;
                foreach(var row in BrushSweep.Project(baseDot.Solid,local))for(int x=row.Left;x<row.Right;x++)
                {int index=row.Y*w+x;if(pending.Remove(index)||!covered[index])baseAdded++;else baseRepeated++;covered[index]=true;}
            }
        }
        contributions.Add(new(baseDot.Size,baseDot.ShapeSlot,baseCount,baseAdded,baseRepeated));
        var missing=labels.Select((c,i)=>c>=0&&!covered[i]).ToArray();int target=labels.Count(c=>c>=0),countCovered=target-missing.Count(x=>x);
        var candidate=new MeasuredColorResult(groups,target,countCovered,missing){Diagnostics=new(0,0,0,timer.ElapsedMilliseconds)
            {StrokeSavings=new(baseline.Groups.Values.Sum(g=>g.Count),removed,0,0,0,contributions)}};
        double Cost(MeasuredColorResult a){var schedule=TransferSchedule.Build(plan,s,a.Groups,token);return PaintTimingPlan.Build(s,schedule,TransferSchedule.Order(plan,schedule),token:token,forSelection:true).Sum(w=>w.PlannedSeconds);}
        // A wide route cannot trade away coverage or cost more than the base
        // candidate under the same target. Report the chosen actual commands.
        double before=Cost(baseline),after=Cost(candidate);
        int baselineCommands=baseline.Groups.Values.Sum(g=>g.Count),candidateCommands=groups.Values.Sum(g=>g.Count);
        bool use=countCovered>=baseline.CoveredPixels&&after<before;
        var choice=new MeasuredPlanChoice(baselineCommands,candidateCommands,use?candidateCommands:baselineCommands,before,after,use?after:before,
            use?groups.Count(g=>g.Value.All(op=>op.StrokeProfileId is null)):groups.Count,use?groups.Count(g=>g.Value.Any(op=>op.StrokeProfileId is not null)):0,
            use?countCovered-baseline.CoveredPixels:0){SelectedCandidate=use?"full-stroke-heldout":"base-full-stroke-not-faster"};
        if(!use)return baseline with{Diagnostics=baseline.Diagnostics! with{Choice=choice}};
        return candidate with{Diagnostics=candidate.Diagnostics! with{Choice=choice,StrokeSavings=candidate.Diagnostics!.StrokeSavings! with{BeforeSeconds=before,AfterSeconds=after}}};
    }
}
