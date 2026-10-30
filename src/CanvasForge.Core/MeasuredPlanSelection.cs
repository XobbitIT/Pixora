namespace CanvasForge.Core;

// Compare full dispatch costs with identical measured-mask union coverage.
internal static class MeasuredPlanSelection
{
    public static MeasuredColorResult? Build(PaintPlan plan,Settings source,CancellationToken token)
    {
        if(source.Bool("adaptive_brush")&&StrokeProfiles.Read(source).Length>0)return StrokeProfilePlan.Build(plan,source,token);
        var clock=System.Diagnostics.Stopwatch.StartNew();long allocated=GC.GetAllocatedBytesForCurrentThread();
        token.ThrowIfCancellationRequested();var s=BrushFootprints.Snapshot(source);
        if(s.Bool("adaptive_brush"))
        {
            s=BrushMotionEvidence.Route(s);s.Set("adaptive_brush",true);s.MeasurementSnapshot=false;
            s.Set("brush_footprints",BrushFootprints.Read(source,true).Where(p=>p.Size<=3||!source.Bool("require_stroke_profiles")&&BrushMotionEvidence.Current(source,p)).ToArray());
            s=BrushFootprints.Snapshot(s);
        }
        var legacy=MeasuredColorPlan.BuildGeometry(plan,s,token);
        if(legacy is null)return null;
        if(!s.Bool("adaptive_brush"))return MeasuredStrokeOptimization.Apply(plan,s,legacy,token);
        var profiles=DrawingWorkflow.PaintingProfiles(s,s.Bool("adaptive_auto_shape")).Where(p=>p.SolidCore.Valid&&p.Size<=s.Number("adaptive_max_size",20)).ToArray();
        double preferred=s.Number("precision_brush_size",3);int shape=s.Int("brush_shape_slot",3);
        var baseProfile=profiles.OrderByDescending(p=>p.Size==preferred&&p.ShapeSlot==shape)
            .ThenBy(p=>p.Size).ThenByDescending(p=>p.SolidPixels).ThenBy(p=>p.ShapeSlot).First();
        var ordinary=s.Clone();ordinary.Set("adaptive_brush",false);ordinary.Set("brush_shape_slot",baseProfile.ShapeSlot);
        ordinary.Set("brush_shape",baseProfile.ShapeSlot==3?"Round":baseProfile.ShapeSlot==4?"Square":$"Shape {baseProfile.ShapeSlot}");
        ordinary.Set("precision_brush_size",baseProfile.Size);
        var single=MeasuredColorPlan.BuildGeometry(plan,ordinary,token)!;
        var horizontal=MeasuredColorPlan.BuildSweptGeometry(plan,s,false,token)!;
        var vertical=MeasuredColorPlan.BuildSweptGeometry(plan,s,true,token)!;
        MeasuredColorResult[] alternatives=[single,legacy,horizontal,vertical];
        var union=new bool[single.UnplannedMask.Length];int gaps=0;
        for(int i=0;i<union.Length;i++)
        {if((i&1023)==0)token.ThrowIfCancellationRequested();union[i]=alternatives.All(a=>a.UnplannedMask[i]);if(union[i])gaps++;}
        var byId=profiles.ToDictionary(p=>p.Id);var rect=s.Calibration.Rect("canvas");
        Dictionary<int,List<BrushStroke>> Supplement(MeasuredColorResult first)
        {
            var covered=first.UnplannedMask.Select(missing=>!missing).ToArray();
            var groups=first.Groups.ToDictionary(g=>g.Key,g=>g.Value.ToList());
            foreach(var other in alternatives.Where(a=>!ReferenceEquals(a,first)).OrderByDescending(a=>a.CoveredPixels))
            foreach(var(color,strokes) in other.Groups)
            foreach(var stroke in strokes)
            {
                token.ThrowIfCancellationRequested();var p=byId[stroke.ProfileId!];var line=stroke.Line;
                var local=new ScreenLine(line.X1-rect.Left,line.Y1-rect.Top,line.X2-rect.Left,line.Y2-rect.Top);
                var runs=BrushSweep.Project(p.Solid,local);
                bool adds=runs.Any(r=>Enumerable.Range(r.Left,r.Right-r.Left).Any(x=>!covered[r.Y*rect.Width+x]));
                if(!adds)continue;groups[color].Add(stroke);
                foreach(var r in runs)Array.Fill(covered,true,r.Y*rect.Width+r.Left,r.Right-r.Left);
            }
            foreach(var color in groups.Keys.ToArray())groups[color]=groups[color].OrderBy(p=>p.ShapeSlot).ThenByDescending(p=>p.Size).ToList();
            return groups;
        }
        var candidates=new List<MeasuredColorResult>();
        var summed=alternatives.Select(a=>a.Diagnostics!).ToArray();
        var diagnostics=new MeasuredPlanDiagnostics(summed.Sum(d=>d.SafeMapsBuilt),summed.Sum(d=>d.SafeMapsReused),
            summed.Max(d=>d.PeakCachedBytes),clock.ElapsedMilliseconds)
            {AllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-allocated,SafeMapMilliseconds=summed.Sum(d=>d.SafeMapMilliseconds),
                WidePassMilliseconds=summed.Sum(d=>d.WidePassMilliseconds),ResidualPassMilliseconds=summed.Sum(d=>d.ResidualPassMilliseconds)};
        foreach(var alternative in alternatives)
            candidates.Add(MeasuredStrokeOptimization.Apply(plan,s,new(Supplement(alternative),single.TargetPixels,single.TargetPixels-gaps,union){Diagnostics=diagnostics},token));
        double Cost(Dictionary<int,List<BrushStroke>> groups)
        {
            var schedule=TransferSchedule.Build(plan,s,groups,token);
            return PaintTimingPlan.Build(s,schedule,TransferSchedule.Order(plan,schedule),token:token,forSelection:true).Sum(w=>w.PlannedSeconds);
        }
        var costs=candidates.Select(c=>Cost(c.Groups)).ToArray();int best=Array.IndexOf(costs,costs.Min());var selected=candidates[best];
        var mixed=plan.Counts.Keys.ToDictionary(c=>c,c=>candidates.MinBy(a=>Cost(new(){[c]=a.Groups[c]}))!.Groups[c]);
        double mixedCost=Cost(mixed);
        bool perColor=mixedCost<costs[best];
        if(perColor)selected=MeasuredStrokeOptimization.Apply(plan,s,selected with{Groups=mixed},token);
        double finalCost=Cost(selected.Groups);
        int singleColors=selected.Groups.Count(g=>g.Value.All(op=>op.Size==baseProfile.Size&&op.ShapeSlot==baseProfile.ShapeSlot));
        int wideBest=costs[2]<=costs[3]?2:3;
        var choice=new MeasuredPlanChoice(candidates[0].Groups.Values.Sum(g=>g.Count),candidates[wideBest].Groups.Values.Sum(g=>g.Count),selected.Groups.Values.Sum(g=>g.Count),
            costs[0],Math.Min(costs[2],costs[3]),finalCost,singleColors,plan.Counts.Count-singleColors,single.UnplannedPixels-gaps)
            {SelectedCandidate=perColor?"per-color-mask":new[]{"single-union","legacy-union","horizontal-mask","vertical-mask"}[best],
                HorizontalSeconds=costs[2],VerticalSeconds=costs[3]};
        return selected with{Diagnostics=selected.Diagnostics! with{Choice=choice,BuildMilliseconds=clock.ElapsedMilliseconds,
            AllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-allocated}};
    }
}
