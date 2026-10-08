using System.Diagnostics;
using CanvasForge.Core;

internal static class StartPreparationChecks
{
    private static void Require(bool value){if(!value)throw new Exception("Start preparation regression");}
    internal static Settings VerifiedSize3()
    {
        var s=Settings.Defaults();s.Set("adaptive_brush",false);s.Set("calibrated_strokes",true);s.Set("precision_brush_size","3");
        var c=s.Calibration;c.SetRect("canvas",new(0,0,256,256));c.SetSession(new(0,0),96,new(500,400));
        c.SetRect("size_track",new(300,100,450,120));s.SetCalibration(c);
        BrushSpan[] spans=[new(-1,-1,2),new(0,-1,2),new(1,-1,2)];var stamp=new BrushStamp(spans,spans,new(10,10,10));
        BrushFootprints.Save(s,[BrushFootprints.Build(s,3,3,[stamp,stamp,stamp])]);
        var axes=new List<SpatialAxis>();
        foreach(bool vertical in new[]{false,true})axes.Add(new(vertical,1,Enumerable.Range(0,3).Select(i=>
            new SpatialAnchor(vertical?new(30+i*60,30,30+i*60,50):new(30,30+i*60,50,30+i*60),0,new(10,10,10),12)).ToList()));
        var spatial=new SpatialProbeProfile(Guid.NewGuid().ToString("N"),ProbeSpatialCalibration.Context(s),DateTimeOffset.UtcNow,3,1,axes);
        ProbeSpatialCalibration.Save(s,spatial);
        s.Set("speed_probe_profile",new SpeedProbeProfile(SpeedCalibration.Context(s),DateTimeOffset.UtcNow,
            [new(3,StrokeMethod.Paced,false,8,SpeedCalibration.Margin(8),1,20,3,1,spatial.Id)]));
        Require(SpeedCalibration.Use(s)&&AdaptiveBrush.CalibrationCurrent(s)&&!s.Bool("adaptive_brush"));return s;
    }
    public static void Run(Action<string,Action> test)
    {
        test("Working Size remains readable before HEX control capture",()=>{
            var s=Settings.Defaults();s.Set("color_mode","HEX Direct");s.Set("coverage_mode","Fast");s.Set("cell_px",3);
            s.Set("brush_calibration_context","old palette geometry");s.Set("brush_calibration_points",new double[][]{[1,1],[20,20]});
            Require(PaintTimingPlan.DefaultSize(s)==3&&!SpeedCalibration.BrushReady(s,3));
        });
        test("Full setup includes Size 3 after a manual Size 1 selection",()=>{
            Require(SetupBrushSelection.Sizes("1",true).SequenceEqual(new double[]{1,3}));
            Require(SetupBrushSelection.Sizes("1",false).SequenceEqual(new double[]{1}));
            Require(SetupBrushSelection.Sizes("20",true).SequenceEqual(new double[]{3,20}));
        });
        test("Working Size remains independent of movement presets and image grid",()=>{
            var s=VerifiedSize3();
            foreach(var profile in SpeedProfile.All){s.Set("speed_profile",profile.Name);s.Set("cell_px",8);Require(PaintTimingPlan.DefaultSize(s)==3);}
            s.Set("precision_brush_size","1");Require(PaintTimingPlan.DefaultSize(s)==1&&!SpeedCalibration.BrushReady(s,1));
            s.Set("precision_brush_size","3");s.Set("adaptive_brush",true);s.Set("speed_profile","Rapid");Require(PaintTimingPlan.DefaultSize(s)==3&&!SpeedCalibration.BrushReady(s,1));
            AdaptiveBrush.Validate(s);
        });
        test("Timing and preview estimate use the actual Size 3 route",()=>{
            var s=VerifiedSize3();var batch=new PaintBatch(0,[new(40,40,60,40)],1);var speed=SpeedProfile.Get("Rapid");
            var sample=SpeedCalibration.Resolve(s,PaintTimingPlan.DefaultSize(s),batch.Segments[0]);Require(sample is not null);
            var work=PaintTimingPlan.Build(s,new(){{0,[batch]}},[0]).Single(w=>w.Motion);
            Require(work.RateKey==PaintTimingPlan.Route(s,batch,3)&&work.RateKey.Contains("3:probe_Paced"));
            Require(Math.Abs(work.PlannedSeconds-CalibratedMotion.Estimate(batch.Segments[0],sample!))<1e-9);
            Require(Math.Abs(work.PlannedSeconds-TransferSchedule.EstimateBatch(s,speed,batch))<1e-9);
        });
        test("Large startup timing avoids repeated proof parsing",()=>{
            var s=VerifiedSize3();var batch=new PaintBatch(0,[new(40,40,60,40)],1);
            var groups=new Dictionary<int,List<PaintBatch>>{{0,Enumerable.Repeat(batch,10000).ToList()}};
            var timer=Stopwatch.StartNew();var work=PaintTimingPlan.Build(s,groups,[0]);
            Require(work.Count==10003&&work.Count(w=>w.Motion)==10000&&work.Where(w=>w.Motion).All(w=>w.RateKey.Contains("3:probe_Paced")));
            Require(timer.Elapsed.TotalSeconds<5); // The old implementation takes over a minute for this case.
        });
        test("Startup timing respects cancellation before producing work",()=>{
            using var cancel=new CancellationTokenSource();cancel.Cancel();bool cancelled=false;
            try{PaintTimingPlan.Build(VerifiedSize3(),new(){{0,[]}},[0],token:cancel.Token);}catch(OperationCanceledException){cancelled=true;}
            Require(cancelled);
        });
        test("Stale speed evidence falls back without rewriting saved preferences",()=>{
            var s=VerifiedSize3();var c=s.Calibration;c.SetRect("size_track",new(300,100,460,120));s.SetCalibration(c);
            s.Set("input_engine","Experimental 1 ms");s.Set("input_frame_delay_ms",16);string before=s.Data.ToJsonString();
            var effective=DrawingWorkflow.Effective(s);
            Require(s.Data.ToJsonString()==before&&!SpeedCalibration.Use(effective)&&!effective.Bool("fast_transfer")&&!effective.Bool("calibrated_strokes"));
            Require(effective.Text("input_engine")=="Stable"&&effective.Number("input_frame_delay_ms")==20&&PaintTimingPlan.DefaultSize(effective)==3);
            Require(!AdaptiveBrush.CalibrationCurrent(effective));
        });
        test("Working Size validation rejects unsupported values",()=>{
            var s=Settings.Defaults();s.Set("precision_brush_size","2");bool rejected=false;
            try{s.Validate();}catch(InvalidDataException){rejected=true;}Require(rejected);
        });
    }
}
