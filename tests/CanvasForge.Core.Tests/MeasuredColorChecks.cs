using System.Diagnostics;
using System.Text.Json;
using CanvasForge.Core;

internal static class MeasuredColorChecks
{
    private sealed record Mask(double Size,int ShapeSlot,BrushSpan[] Possible,BrushSpan[] Solid);
    private static void Require(bool ok,string message="Measured color regression"){if(!ok)throw new Exception(message);}
    internal static Settings Config(bool mixed=true)
    {
        var s=Settings.Defaults();var c=s.Calibration;c.SetRect("canvas",new(0,0,400,320));c.SetSession(new(0,0),96,new(1920,1080));
        c.SetRect("size_track",new(500,200,750,230));c.SetRect("brush_shapes",new(500,100,850,140));s.SetCalibration(c);
        s.SetPalette([new(new(220,30,30),new(600,400),"main"),new(new(30,220,30),new(640,400),"main")]);
        s.Set("adaptive_brush",mixed);s.Set("precision_brush_size","3");s.Set("fast_transfer",false);AdaptiveBrush.Prepare(s);
        var masks=JsonSerializer.Deserialize<Mask[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","beta49-size3-10-20-masks.json")))!;
        BrushFootprints.Save(s,masks.Select(p=>BrushFootprints.Build(s,p.ShapeSlot,p.Size,Enumerable.Repeat(new BrushStamp(p.Possible,p.Solid,new(20,20,20)),3).ToArray())).ToArray());return s;
    }
    internal static PaintPlan Plan(Func<int,int,int>? label=null)
    {
        label??=(x,y)=>x<200?0:1;var indices=new int[400*320];var strokes=new Dictionary<int,List<Stroke>>{{0,[]},{1,[]}};
        for(int y=0;y<320;y++)
        {
            for(int x=0;x<400;x++)indices[y*400+x]=label(x,y);
            for(int x=0;x<400;x++){int color=indices[y*400+x],begin=x;while(x+1<400&&indices[y*400+x+1]==color)x++;if(color>=0)strokes[color].Add(new(color,begin,y,x,y));}
        }
        return new PaintPlan{Width=400,Height=320,Palette=[new(new(220,30,30),null,"main"),new(new(30,220,30),null,"main")],Indices=indices,Strokes=strokes,
            Counts=Enumerable.Range(0,2).ToDictionary(i=>i,i=>indices.Count(x=>x==i)),Identity="measured-colors",Mode=ColorMode.RustPalette,Preview=new(400,320)};
    }
    private static void Verify(PaintPlan plan,Settings s,Dictionary<int,List<PaintBatch>> groups)
    {
        foreach(var (color,ops) in groups)foreach(var op in ops)
        {
            var p=BrushFootprints.Find(s,op.Size,op.ShapeSlot)!;Require(p is not null&&p.Id==op.ProfileId);
            foreach(var l in op.Segments)
            {
                Require(l.X1==l.X2||l.Y1==l.Y2);
                for(int k=0;k<=TransferSchedule.Length(l);k++)
                {
                    int x=l.X1+k*Math.Sign(l.X2-l.X1),y=l.Y1+k*Math.Sign(l.Y2-l.Y1);
                    Require(BrushFootprints.Safe(p!,x,y,400,320,(px,py)=>plan.Indices[py*400+px]==color),"Physical footprint crosses a different color");
                }
            }
        }
    }
    public static void Run(Action<string,Action> test)
    {
        test("Measured mixed painting no longer requires Size 1 or Speed Probe",()=>{
            var s=Config();Require(AdaptiveBrush.CalibrationCurrent(s)&&PaintTimingPlan.DefaultSize(s)==3&&!SpeedCalibration.Current(s));AdaptiveBrush.Validate(s);
            s.Set("adaptive_auto_shape",true);s.Set("brush_shape_slot",4);s.Set("brush_shape","Square");Require(AdaptiveBrush.CalibrationCurrent(s)&&PaintTimingPlan.DefaultSize(s)==3);AdaptiveBrush.Validate(s);
        });
        test("Normal Size 3 preserves real asymmetric masks inside every color",()=>{
            var s=Config(false);var plan=Plan();var r=MeasuredColorPlan.TryBuild(plan,s)!;
            Require(r.TargetPixels==128000&&r.CoveredPixels>0&&r.UnplannedPixels>0&&r.Groups.Values.SelectMany(x=>x).All(x=>x.Size==3&&x.ProfileId is not null));
            Verify(plan,s,TransferSchedule.Build(plan,s,r.Groups));
        });
        test("Mixed planning combines verified wide Sizes and Size 3 details",()=>{
            var s=Config();var plan=Plan();var r=MeasuredColorPlan.TryBuild(plan,s)!;var sizes=r.Groups.Values.SelectMany(x=>x).Select(x=>x.Size).Distinct().ToArray();
            Require(sizes.Contains(3)&&sizes.Any(x=>x>=10)&&!sizes.Contains(1));Verify(plan,s,TransferSchedule.Build(plan,s,r.Groups));
        });
        test("A sub-footprint color region is reported without unsafe fine fallback",()=>{
            var s=Config();var plan=Plan((x,y)=>x==200&&y==160?1:-1);var r=MeasuredColorPlan.TryBuild(plan,s)!;
            Require(r.TargetPixels==1&&r.CoveredPixels==0&&r.UnplannedPixels==1&&r.Groups.Values.All(x=>x.Count==0));
        });
        test("A genuine one-pixel measurement restores fine targets in mixed painting",()=>{
            var s=Config();BrushSpan[] dot=[new(0,0,1)];BrushFootprints.Save(s,[BrushFootprints.Build(s,3,1,Enumerable.Repeat(new BrushStamp(dot,dot,new(20,20,20)),3).ToArray())]);
            var plan=Plan((x,y)=>x==200&&y==160?1:-1);var r=MeasuredColorPlan.TryBuild(plan,s)!;
            Require(r.CoveredPixels==1&&r.UnplannedPixels==0&&r.Groups[1].Single().Size==1&&PaintTimingPlan.DefaultSize(s)==1);
        });
        test("Weak Size 1 evidence does not turn into a solid proof",()=>{
            var s=Config();BrushSpan[] possible=[new(0,0,1)];BrushFootprints.Save(s,[BrushFootprints.Build(s,3,1,Enumerable.Repeat(new BrushStamp(possible,[],new(20,20,20)),3).ToArray())]);
            Require(AdaptiveBrush.CalibrationCurrent(s)&&PaintTimingPlan.DefaultSize(s)==3&&!SpeedCalibration.BrushReady(s,1));
            s.Set("adaptive_brush",false);s.Set("precision_brush_size","1");Require(CalibrationReliability.PaintingProblem(s) is not null);
        });
        test("Changed capture invalidates mixed masks instead of weakening accuracy",()=>{
            var s=Config();var c=s.Calibration;c.SetRect("canvas",new(0,0,401,320));s.SetCalibration(c);
            Require(!AdaptiveBrush.CalibrationCurrent(s)&&MeasuredColorPlan.TryBuild(Plan(),s) is null);
        });
        test("Fast connectors preserve measured boundaries and IDs",()=>{
            var s=Config();s.Set("fast_transfer",true);var plan=Plan((x,y)=>x<200&&y<300?0:1);var r=MeasuredColorPlan.TryBuild(plan,s)!;
            Verify(plan,s,TransferSchedule.Build(plan,s,r.Groups));
        });
        test("Possible-only pixels are not silently declared guaranteed coverage",()=>{
            var s=Config(false);var r=MeasuredColorPlan.TryBuild(Plan(),s)!;Require(r.UnplannedMask.Count(x=>x)==r.UnplannedPixels&&r.CoveredPixels+r.UnplannedPixels==r.TargetPixels);
        });
        test("Measured color planning observes cancellation before allocation",()=>{
            using var c=new CancellationTokenSource();c.Cancel();bool stopped=false;try{MeasuredColorPlan.TryBuild(Plan(),Config(),c.Token);}catch(OperationCanceledException){stopped=true;}Require(stopped);
        });
        test("Repair uses a verified Size 3 with the same safe physical projection",()=>{
            var s=Config();var p=BrushFootprints.Find(s,3)!;var expected=Enumerable.Repeat(true,400*320).ToArray();var missing=new bool[expected.Length];missing[160*400+200]=true;
            var r=CoverageAudit.PlanRepair(missing,expected,new(0,0,400,320),[p]);Require(r.Operations.Count>0&&r.Operations.All(x=>x.Size==3)&&r.UnreachablePixels==0);
            foreach(var op in r.Operations)Require(BrushFootprints.Safe(p,op.Line.X1,op.Line.Y1,400,320,(x,y)=>expected[y*400+x]));
        });
        test("Prepared schedule ETA equals timing for the same measured plan",()=>{
            var s=Config();var plan=Plan();var groups=TransferSchedule.Build(plan,s);Require(Math.Abs(Coverage.EstimateSeconds(plan,s)-Coverage.EstimateSeconds(plan,s,preparedGroups:groups))<1e-9);
        });
        test("Live masks reduce synthetic fill operations without raw edge commands",()=>{
            var s=Config();var plan=Plan();var clock=Stopwatch.StartNew();var result=MeasuredColorPlan.TryBuild(plan,s)!;
            int baseline=Coverage.Build(plan,s).Values.Sum(x=>x.Count),operations=result.Groups.Values.Sum(x=>x.Count);
            Require(operations<baseline);Console.WriteLine("MEASURED_PLAN_BENCHMARK "+JsonSerializer.Serialize(new{baseline,operations,result.TargetPixels,result.CoveredPixels,result.UnplannedPixels,milliseconds=clock.Elapsed.TotalMilliseconds,scope="synthetic two-color fill with actual beta49 masks; not a Rust speed test"}));
        });
    }
}
