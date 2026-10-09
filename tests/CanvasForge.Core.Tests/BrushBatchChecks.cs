using CanvasForge.Core;

internal static class BrushBatchChecks
{
    private static void Require(bool value){if(!value)throw new Exception("Brush batch regression");}
    private static Settings Config()
    {
        var s=Settings.Defaults();var c=s.Calibration;c.SetRect("canvas",new(0,0,1200,1200));
        c.SetRect("size_track",new(1300,100,1550,140));c.SetRect("brush_shapes",new(1300,30,1650,70));
        c.SetSession(new(0,0),96,new(1920,1440));s.SetCalibration(c);AdaptiveBrush.Prepare(s);return s;
    }
    private static BrushStamp Stamp(Rgb? color=null)=>new([new(-1,-1,2),new(0,-1,2),new(1,-1,2)],
        [new(-1,-1,2),new(0,-1,2),new(1,-1,2)],color??new(20,20,20));
    public static void Run(Action<string,Action> test)
    {
        test("Weak Size 1 cannot discard three complete Size 3 measurements",()=>{
            var batch=new BrushCalibrationBatch(Config(),3,[1,3]);
            batch.Add(1,1,Stamp());batch.Reject(1,2,new(new(69,8,80,new(145,140,130),new(76,74,72))));
            Require(batch.ShouldMeasure(1));batch.Reject(1,3,new(new(70,8,80,new(146,140,130),new(76,74,72))));
            for(int repeat=1;repeat<=3;repeat++)batch.Add(3,repeat,Stamp());
            Require(!batch.ShouldMeasure(1)&&batch.Profiles.Count==1&&batch.Profiles[0].Size==3&&batch.Profiles[0].Repeats==3);
            Require(batch.Rejected.Single().Contrast!.PeakDelta==69&&batch.Rejected[0].Repeat==2);
        });
        test("Incomplete inconsistent and repeated measurements never publish a Size",()=>{
            var batch=new BrushCalibrationBatch(Config(),3,[1,3]);batch.Add(1,1,Stamp());batch.Add(1,2,Stamp());Require(batch.Profiles.Count==0);
            for(int repeat=1;repeat<=3;repeat++)batch.Add(3,repeat,Stamp(repeat==3?new(80,80,80):new(20,20,20)));
            Require(batch.Profiles.Count==0&&batch.Rejected.Single().Size==3);
            try{batch.Add(1,2,Stamp());throw new Exception("Repeat accepted twice");}catch(InvalidOperationException){}
        });
        test("Partial saving preserves previous masks and allows verified Size 3 without certifying Size 1",()=>{
            var s=Config();var batch=new BrushCalibrationBatch(s,3,[3]);for(int repeat=1;repeat<=3;repeat++)batch.Add(3,repeat,Stamp());
            var old=BrushFootprints.Build(s,3,10,[Stamp(),Stamp(),Stamp()]);BrushFootprints.Save(s,[old]);BrushFootprints.Save(s,batch.Profiles);
            Require(BrushFootprints.Find(s,10)!.Id==old.Id&&AdaptiveBrush.CalibrationCurrent(s));
            Require(SpeedCalibration.BrushReady(s,3)&&!SpeedCalibration.BrushReady(s,1));
            var moved=s.Clone();var c=moved.Calibration;c.SetSession(new(1,0),96,new(1920,1440));moved.SetCalibration(c);Require(!SpeedCalibration.BrushReady(moved,3));
        });
        test("A Size 3 proof works independently and cannot verify missing Size 1",()=>{
            var s=Config();s.Set("calibrated_strokes",true);BrushFootprints.Save(s,[BrushFootprints.Build(s,3,3,[Stamp(),Stamp(),Stamp()])]);
            var fp=SpeedCalibration.Footprint(s,3);var tiles=ProbeSpatialCalibration.Tiles(s.Calibration.Rect("canvas"),fp.Outer);
            var model=new SpatialProbeProfile(Guid.NewGuid().ToString("N"),ProbeSpatialCalibration.Context(s),DateTimeOffset.UtcNow,3,fp.Outer,
                new[]{false,true}.Select(v=>new SpatialAxis(v,fp.Inner,tiles.Skip(v?3:0).Take(3).Select(t=>new SpatialAnchor(v?t.Vertical:t.Horizontal,0,new(20,20,20),12)).ToList())).ToList());
            ProbeSpatialCalibration.Save(s,model);Require(ProbeSpatialCalibration.Read(s,3) is not null);
            s.Set("speed_probe_profile",new SpeedProbeProfile(SpeedCalibration.Context(s),DateTimeOffset.UtcNow,[new(3,StrokeMethod.Paced,false,8,12,1,40,3,1,model.Id)]));
            Require(SpeedCalibration.Resolve(s,3,new(20,20,60,20)) is not null&&SpeedCalibration.Resolve(s,1,new(20,20,60,20)) is null);
            Require(AdaptiveBrush.CalibrationCurrent(s)&&!SpeedCalibration.BrushReady(s,1));
        });
    }
}
