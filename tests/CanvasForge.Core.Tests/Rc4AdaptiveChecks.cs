using CanvasForge.Core;

internal static class Rc4AdaptiveChecks
{
    private static void Require(bool ok,string reason){if(!ok)throw new Exception(reason);}
    private static HashSet<ScreenPoint> Oracle(BrushSpan[] mask,ScreenLine line)
    {
        var result=new HashSet<ScreenPoint>();
        for(int k=0;k<=TransferSchedule.Length(line);k++)
        foreach(var p in BrushFootprints.Points(mask))
            result.Add(new(line.X1+k*Math.Sign(line.X2-line.X1)+p.X,line.Y1+k*Math.Sign(line.Y2-line.Y1)+p.Y));
        return result;
    }
    internal static HashSet<ScreenPoint> Coverage(Settings s,MeasuredColorResult r,bool verify=false,PaintPlan? plan=null)
    {
        var result=new HashSet<ScreenPoint>();var rect=s.Calibration.Rect("canvas");
        var profiles=BrushFootprints.Read(s,true).ToDictionary(p=>p.Id);
        foreach(var (color,ops) in r.Groups)foreach(var op in ops)
        {
            var p=profiles[op.ProfileId!];var l=op.Line;
            Require(l.X1==l.X2||l.Y1==l.Y2,"Diagonal unmeasured command");
            var local=new ScreenLine(l.X1-rect.Left,l.Y1-rect.Top,l.X2-rect.Left,l.Y2-rect.Top);
            if(verify)foreach(var q in Oracle(p.Possible,local))
                Require(q.X>=0&&q.Y>=0&&q.X<rect.Width&&q.Y<rect.Height&&plan!.Indices[q.Y*rect.Width+q.X]==color,"Possible mask crosses target color");
            result.UnionWith(Oracle(p.Solid,local));
        }
        return result;
    }
    public static void Run(Action<string,Action> test)
    {
        test("RC4 exact sweeps preserve asymmetric disconnected masks on both axes and reverse paths",()=>{
            BrushSpan[] mask=[new(-3,2,4),new(-1,-4,-2),new(-1,1,3),new(1,-3,4)];
            foreach(var l in new[]{new ScreenLine(10,20,19,20),new(10,20,10,33),new(19,20,10,20),new(10,33,10,20),new(10,20,10,20)})
                Require(BrushFootprints.Points(BrushSweep.Project(mask,l)).ToHashSet().SetEquals(Oracle(mask,l)),"Sweep inflated or lost the certified mask");
            Require(BrushSweep.Transpose(BrushSweep.Transpose(mask)).SequenceEqual(mask),"Axis transform changed command offsets");
        });
        foreach(bool vertical in new[]{false,true})test("RC4 full-mask geometry retains physical offsets and safety on "+(vertical?"vertical":"horizontal")+" passes",()=>{
            var s=MeasuredColorChecks.Config();var plan=MeasuredColorChecks.Plan((x,y)=>x<10||y<10||x>=390||y>=310?-1:(x/43+y/31)%2);
            var r=MeasuredColorPlan.BuildSweptGeometry(plan,s,vertical)!;var coverage=Coverage(s,r,true,plan);
            Require(coverage.Count==r.CoveredPixels,"Coverage count differs from raster oracle");
            for(int i=0;i<r.UnplannedMask.Length;i++)Require((plan.Indices[i]>=0&&!coverage.Contains(new(i%400,i/400)))==r.UnplannedMask[i],"Missing-pixel mask differs from actual solid union");
            Require(BrushFootprints.Read(s,true).All(p=>p.Context==BrushFootprints.Context(s,p.ShapeSlot)),"Profile context mutated by axis transform");
        });
        test("RC4 Adaptive selects time and preserves union of single legacy horizontal and vertical coverage",()=>{
            var s=MeasuredColorChecks.Config();var plan=MeasuredColorChecks.Plan();
            var single=s.Clone();single.Set("adaptive_brush",false);
            var expected=Coverage(s,MeasuredColorPlan.BuildGeometry(plan,single)!);
            expected.UnionWith(Coverage(s,MeasuredColorPlan.BuildGeometry(plan,s)!));
            expected.UnionWith(Coverage(s,MeasuredColorPlan.BuildSweptGeometry(plan,s,false)!));
            expected.UnionWith(Coverage(s,MeasuredColorPlan.BuildSweptGeometry(plan,s,true)!));
            var r=MeasuredColorPlan.TryBuild(plan,s)!;var actual=Coverage(s,r,true,plan);var c=r.Diagnostics!.Choice!;
            Require(actual.SetEquals(expected)&&actual.Count==r.CoveredPixels,"Optimization lost or invented solid paint");
            Require(c.SelectedSeconds<=Math.Min(c.SingleUnionSeconds,Math.Min(c.HorizontalSeconds,c.VerticalSeconds))+1e-9,"Selected route costs more than a same-coverage alternative");
            Require(r.Groups.Values.SelectMany(g=>g).All(p=>p.Size>=2&&p.ProfileId is not null),"Uncertified fallback inserted");
        });
        test("RC4 thin certified core permits full-mask planning without weakening a brush proof",()=>{
            var s=MeasuredColorChecks.Config();var points=Enumerable.Range(-6,13).Select(y=>new ScreenPoint(0,y)).ToList();
            for(int y=-6;y<=6;y+=2)for(int x=2;x<=8;x++)points.Add(new(x,y));
            BrushSpan[] solid=BrushFootprints.Spans(points);
            var p=BrushFootprints.Build(s,3,20,Enumerable.Repeat(new BrushStamp(solid,solid,new(20,20,20)),3).ToArray());
            Require(p.SolidCore.Width==1&&p.SolidCore.Height==13,"Fixture must retain a naturally narrow rectangle");
            BrushFootprints.Save(s,[p]);
            var plan=MeasuredColorChecks.Plan();var r=MeasuredColorPlan.BuildSweptGeometry(plan,s,false)!;
            Require(r.Groups.Values.SelectMany(g=>g).Any(op=>op.Size==20),"Narrow rectangle still discards full certified mask");
            Coverage(s,r,true,plan);
        });
        test("RC4 Adaptive works with only Size 3 and rejects stale proof without raw fine fallback",()=>{
            var s=MeasuredColorChecks.Config();s.Set("brush_footprints",BrushFootprints.Read(s).Where(p=>p.Size==3).ToArray());var plan=MeasuredColorChecks.Plan();
            var r=MeasuredColorPlan.TryBuild(plan,s)!;Require(r.Groups.Values.SelectMany(g=>g).All(p=>p.Size==3),"Missing wide proof fabricated");
            var c=s.Calibration;c.SetSession(new(0,0),144,new(1920,1080));s.SetCalibration(c);
            Require(MeasuredColorPlan.TryBuild(plan,s) is null,"Stale mask accepted");
        });
    }
}
