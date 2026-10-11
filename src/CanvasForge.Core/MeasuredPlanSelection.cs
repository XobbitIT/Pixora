namespace CanvasForge.Core;

// Compare complete, safe alternatives rather than assuming a larger Size is
// faster. Both alternatives cover the union of their measured solid footprints;
// neither adds raw commands at edges or labels possible-only paint as covered.
internal static class MeasuredPlanSelection
{
    public static MeasuredColorResult? Build(PaintPlan plan,Settings source,CancellationToken token)
    {
        var clock=System.Diagnostics.Stopwatch.StartNew();long allocated=GC.GetAllocatedBytesForCurrentThread();
        token.ThrowIfCancellationRequested();var s=BrushFootprints.Snapshot(source);
        var mixed=MeasuredColorPlan.BuildGeometry(plan,s,token);
        if(mixed is null)return null;
        if(!s.Bool("adaptive_brush"))return MeasuredStrokeOptimization.Apply(plan,s,mixed,token);
        var profiles=BrushFootprints.Read(s,s.Bool("adaptive_auto_shape")).Where(p=>p.SolidCore.Valid&&p.Size<=s.Number("adaptive_max_size",20)).ToArray();
        if(profiles.Length<2)return MeasuredStrokeOptimization.Apply(plan,s,mixed,token);
        double preferred=s.Number("precision_brush_size",3);int shape=s.Int("brush_shape_slot",3);
        var baseProfile=profiles.OrderByDescending(p=>p.Size==preferred&&p.ShapeSlot==shape)
            .ThenBy(p=>p.Size).ThenByDescending(p=>p.SolidPixels).ThenBy(p=>p.ShapeSlot).First();
        var ordinary=s.Clone();ordinary.Set("adaptive_brush",false);ordinary.Set("brush_shape_slot",baseProfile.ShapeSlot);
        ordinary.Set("brush_shape",baseProfile.ShapeSlot==3?"Round":baseProfile.ShapeSlot==4?"Square":$"Shape {baseProfile.ShapeSlot}");
        ordinary.Set("precision_brush_size",baseProfile.Size);
        var single=MeasuredColorPlan.BuildGeometry(plan,ordinary,token)!;
        var byId=profiles.ToDictionary(p=>p.Id);var rect=s.Calibration.Rect("canvas");
        Dictionary<int,List<BrushStroke>> Supplement(MeasuredColorResult first,MeasuredColorResult second)
        {
            var covered=first.UnplannedMask.Select(missing=>!missing).ToArray();
            var groups=first.Groups.ToDictionary(g=>g.Key,g=>g.Value.ToList());
            foreach(var(color,strokes) in second.Groups)
            {
                foreach(var stroke in strokes)
                {
                    token.ThrowIfCancellationRequested();var p=byId[stroke.ProfileId!];var line=stroke.Line;
                    // The geometry primitive emits horizontal strokes only.
                    if(line.Y1!=line.Y2)throw new InvalidOperationException("Unexpected measured geometry direction.");
                    int x1=Math.Min(line.X1,line.X2)-rect.Left,x2=Math.Max(line.X1,line.X2)-rect.Left,y=line.Y1-rect.Top;
                    bool contributes=false;int start=x2,end=x1;
                    foreach(var row in p.Solid)
                    {
                        int rowStart=(y+row.Y)*rect.Width,begin=rowStart+x1+row.Left,limit=rowStart+x2+row.Right;
                        int firstPixel=begin,last=limit-1;
                        while(firstPixel<limit&&covered[firstPixel])firstPixel++;
                        if(firstPixel==limit)continue;
                        while(last>firstPixel&&covered[last])last--;
                        contributes=true;
                        // A target x permits centers [x-Right+1, x-Left].
                        // The shortest interval meeting every such constraint
                        // avoids traversing already covered ends of the stroke.
                        start=Math.Min(start,Math.Clamp(firstPixel-rowStart-row.Left,x1,x2));
                        end=Math.Max(end,Math.Clamp(last-rowStart-row.Right+1,x1,x2));
                    }
                    if(!contributes)continue;end=Math.Max(start,end);
                    groups[color].Add(stroke with{Line=new(rect.Left+start,line.Y1,rect.Left+end,line.Y1)});
                    foreach(var row in p.Solid)Array.Fill(covered,true,(y+row.Y)*rect.Width+start+row.Left,end-start+row.Right-row.Left);
                }
                groups[color]=groups[color].OrderBy(p=>p.ShapeSlot).ThenByDescending(p=>p.Size).ToList();
            }
            return groups;
        }
        var a=Supplement(single,mixed);var b=Supplement(mixed,single);
        var scheduleA=TransferSchedule.Build(plan,s,a,token);var scheduleB=TransferSchedule.Build(plan,s,b,token);
        var order=TransferSchedule.Order(plan,scheduleA);
        var workA=PaintTimingPlan.Build(s,scheduleA,order,token:token);var workB=PaintTimingPlan.Build(s,scheduleB,order,token:token);
        // Assign transition costs to their color using the same ETA schedule.
        static Dictionary<int,double> ColorCosts(List<TimedWork> work,IReadOnlyList<int> colors)
        {
            var costs=colors.ToDictionary(c=>c,_=>0d);int current=-1;
            foreach(var item in work)
            {
                if(item.Id.StartsWith("color:",StringComparison.Ordinal))current=int.Parse(item.Id[6..],System.Globalization.CultureInfo.InvariantCulture);
                if(current>=0&&item.Id!=PaintTimingPlan.Finish)costs[colors[current]]+=item.PlannedSeconds;
            }
            return costs;
        }
        var costA=ColorCosts(workA,order);var costB=ColorCosts(workB,order);
        var selected=order.ToDictionary(c=>c,c=>costA[c]<=costB[c]?a[c]:b[c]);
        var chosenSchedule=TransferSchedule.Build(plan,s,selected,token);
        double secondsA=workA.Sum(w=>w.PlannedSeconds),secondsB=workB.Sum(w=>w.PlannedSeconds);
        double seconds=PaintTimingPlan.Build(s,chosenSchedule,order,token:token).Sum(w=>w.PlannedSeconds);
        // A different preceding brush can change cross-color transition costs.
        // Keep the cheaper whole-plan candidate if per-color selection loses.
        if(seconds>Math.Min(secondsA,secondsB))
        {selected=secondsA<=secondsB?a:b;seconds=Math.Min(secondsA,secondsB);}
        var missing=new bool[mixed.UnplannedMask.Length];int gaps=0;
        for(int i=0;i<missing.Length;i++)
        {
            if((i&1023)==0)token.ThrowIfCancellationRequested();
            missing[i]=single.UnplannedMask[i]&&mixed.UnplannedMask[i];if(missing[i])gaps++;
        }
        int singleColors=order.Count(c=>ReferenceEquals(selected[c],a[c]));
        var choice=new MeasuredPlanChoice(single.Groups.Values.Sum(g=>g.Count),mixed.Groups.Values.Sum(g=>g.Count),selected.Values.Sum(g=>g.Count),
            secondsA,secondsB,seconds,singleColors,order.Count-singleColors,single.UnplannedPixels-gaps);
        var md=mixed.Diagnostics!;var sd=single.Diagnostics!;
        var result=new MeasuredColorResult(selected,mixed.TargetPixels,mixed.TargetPixels-gaps,missing){Diagnostics=new(md.SafeMapsBuilt+sd.SafeMapsBuilt,md.SafeMapsReused+sd.SafeMapsReused,
            Math.Max(md.PeakCachedBytes,sd.PeakCachedBytes),clock.ElapsedMilliseconds){AllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-allocated,
            SafeMapMilliseconds=md.SafeMapMilliseconds+sd.SafeMapMilliseconds,WidePassMilliseconds=md.WidePassMilliseconds+sd.WidePassMilliseconds,
            ResidualPassMilliseconds=md.ResidualPassMilliseconds+sd.ResidualPassMilliseconds,Choice=choice}};
        return MeasuredStrokeOptimization.Apply(plan,s,result,token);
    }
}
