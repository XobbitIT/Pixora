using System.IO.Compression;
using CanvasForge.Core;

internal static class BrushCalibrationRegressionChecks
{
    private static void Require(bool ok){if(!ok)throw new Exception("Brush calibration regression");}
    private static Settings Config()
    {
        var s=Settings.Defaults();s.Set("brush_shape_slot",4);s.Set("brush_shape","Square");
        var c=s.Calibration;c.SetRect("canvas",new(0,0,1200,1200));c.SetRect("size_track",new(1300,100,1550,140));
        c.SetRect("brush_shapes",new(1300,30,1650,70));c.SetSession(new(0,0),96,new(1920,1440));s.SetCalibration(c);AdaptiveBrush.Prepare(s);return s;
    }
    private static PixelImage Frame(string run,int repeat,string name)
    {
        using var file=File.OpenRead(Path.Combine(AppContext.BaseDirectory,"Fixtures",$"beta36-{run}-{repeat}-{name}.rgba.gz"));
        using var gzip=new GZipStream(file,CompressionMode.Decompress);using var bytes=new MemoryStream();gzip.CopyTo(bytes);
        Require(bytes.Length==96*96*4);return new(96,96,bytes.ToArray());
    }
    public static void Run(Action<string,Action> test)
    {
        test("Stationary dot sends one press and a held movement at the identical command",()=>{
            var input=new DotInput();var point=new ScreenPoint(515,196);
            var trace=BrushDotMotion.Draw(point,input,()=>input.Cursor);
            Require(input.Downs==1&&input.Ups==1&&!input.Held&&input.Moves.SequenceEqual(new[]{point,point}));
            Require(trace.Steps.Length==4&&trace.Steps.All(s=>s.Cursor==point)&&Math.Abs(input.Seconds-.256)<1e-9);
        });
        test("Unsettled cursor aborts before any paint button is pressed",()=>{
            var input=new DotInput();try{BrushDotMotion.Draw(new(10,20),input,()=>new(13,20));throw new Exception("Wrong location painted");}
            catch(InvalidOperationException e){Require(e.Message==BrushDotMotion.PositionError);}
            Require(input.Downs==0&&input.Ups==0);
        });
        test("One pixel normalization is recorded without recentering the command",()=>{
            var input=new DotInput();var trace=BrushDotMotion.Draw(new(10,20),input,()=>new(11,20));
            Require(trace.Command==new ScreenPoint(10,20)&&trace.Steps.All(s=>s.Cursor==new ScreenPoint(11,20)));
        });
        test("Cursor drift during a dot releases the button and rejects the observation",()=>{
            var input=new DotInput();int reads=0;
            try{BrushDotMotion.Draw(new(10,20),input,()=>++reads==1?new(10,20):new(11,20));throw new Exception("Drift accepted");}
            catch(InvalidOperationException e){Require(e.Message==BrushDotMotion.DriftError);}
            Require(input.Downs==1&&input.Ups==1&&!input.Held&&input.Moves.Count==1);
        });
        test("Cancellation on a held wait or move always releases the paint button",()=>{
            foreach(bool move in new[]{false,true})
            {
                var input=new DotInput{FailMove=move,FailWait=!move};
                try{BrushDotMotion.Draw(new(10,20),input,()=>input.Cursor);throw new Exception("Cancellation ignored");}
                catch(OperationCanceledException){}
                Require(input.Downs==1&&input.Ups==1&&!input.Held);
            }
        });
        test("Control reuse requires previous numeric proof and unchanged observed sliders",()=>{
            Dictionary<string,double> known=new(){["size"]=1,["interval"]=.01,["opacity"]=1};
            Require(BrushControlReuse.Required(true,known,1,.01,1,(_,_)=>true).Length==0);
            Require(BrushControlReuse.Required(true,known,3,.01,1,(_,_)=>true).SequenceEqual(new[]{"size"}));
            Require(BrushControlReuse.Required(true,known,1,.01,1,(k,_)=>k!="opacity").SequenceEqual(new[]{"opacity"}));
            Require(BrushControlReuse.Required(false,known,1,.01,1,(_,_)=>true).Length==3);
            known.Remove("interval");Require(BrushControlReuse.Required(true,known,1,.01,1,(_,_)=>true).SequenceEqual(new[]{"interval"}));
        });
        test("Recorded strong beta36 dots with displaced opaque cores remain unusable",()=>{
            var s=Config();var batch=new BrushCalibrationBatch(s,4,[1]);
            for(int n=1;n<=3;n++)batch.Record(1,n,Frame("no-core",n,"background"),Frame("no-core",n,"before"),Frame("no-core",n,"after"),new(48,48),new(0,0,0));
            var profile=batch.Profiles.Single();var summary=batch.Diagnostics.Single();
            Require(profile.SolidPixels==0&&!profile.SolidCore.Valid&&summary.State==BrushSignalState.NoSolidCore);
            Require(summary.Samples.Select(x=>x.Contrast.PeakDelta).SequenceEqual(new[]{139,142,145}));
            Require(summary.Samples.All(x=>x.Geometry is {SolidPixels:>0}));
            BrushFootprints.Save(s,batch.Profiles);BrushSignalDiagnostics.Save(s,batch.Diagnostics);
            Require(!AdaptiveBrush.CalibrationCurrent(s)&&!SpeedCalibration.BrushReady(s,1));
            BrushSignalDiagnostics.Save(s,[summary with{State=BrushSignalState.Rejected}]);
            Require(BrushSignalDiagnostics.Read(s).Single().State==BrushSignalState.NoSolidCore);
        });
        test("Recorded beta36 contrast 137 70 2 retains all failures without a new mask",()=>{
            var s=Config();var batch=new BrushCalibrationBatch(s,4,[1]);
            for(int n=1;n<=3;n++)batch.Record(1,n,Frame("weak",n,"background"),Frame("weak",n,"before"),Frame("weak",n,"after"),new(48,48),new(0,0,0));
            Require(batch.Profiles.Count==0&&batch.Diagnostics.Single().State==BrushSignalState.Rejected);
            Require(batch.Diagnostics[0].Samples.Select(x=>x.Contrast.PeakDelta).SequenceEqual(new[]{137,70,2}));
            Require(batch.Diagnostics[0].Samples[2].Contrast.ChangedPixels==0&&batch.Rejected.Single().Repeat==2);
        });
        test("Speed status explains missing core changed measurements and changed spatial proof",()=>{
            var s=Config();Require(SpeedCalibration.Status(s)==SpeedProfileState.NotTested);
            BrushSpan[] pixels=[new(-1,-1,2),new(0,-1,2),new(1,-1,2)];var stamp=new BrushStamp(pixels,pixels,new(20,20,20));
            BrushFootprints.Save(s,[BrushFootprints.Build(s,4,1,[stamp,stamp,stamp])]);
            var fp=SpeedCalibration.Footprint(s,1);var tiles=ProbeSpatialCalibration.Tiles(s.Calibration.Rect("canvas"),fp.Outer);
            var model=new SpatialProbeProfile(Guid.NewGuid().ToString("N"),ProbeSpatialCalibration.Context(s),DateTimeOffset.UtcNow,1,fp.Outer,
                new[]{false,true}.Select(v=>new SpatialAxis(v,fp.Inner,tiles.Skip(v?3:0).Take(3).Select(t=>new SpatialAnchor(v?t.Vertical:t.Horizontal,0,new(20,20,20),12)).ToList())).ToList());
            ProbeSpatialCalibration.Save(s,model);
            s.Set("speed_probe_profile",new SpeedProbeProfile(SpeedCalibration.Context(s),DateTimeOffset.UtcNow,[new(1,StrokeMethod.Paced,false,20,27,1,35,3,1,model.Id)]));
            Require(SpeedCalibration.Status(s)==SpeedProfileState.Current);
            var changed=s.Clone();changed.Data.Remove("probe_spatial_profiles");changed.Data.Remove("shape_spatial_profiles");
            Require(SpeedCalibration.Status(changed)==SpeedProfileState.SpatialChanged);
            changed=s.Clone();BrushFootprints.Save(changed,[BrushFootprints.Build(changed,4,3,[stamp,stamp,stamp])]);
            Require(SpeedCalibration.Status(changed)==SpeedProfileState.MeasurementsChanged);
            changed=s.Clone();var empty=stamp with{Solid=[]};BrushFootprints.Save(changed,[BrushFootprints.Build(changed,4,1,[empty,empty,empty])]);
            Require(SpeedCalibration.Status(changed)==SpeedProfileState.BrushUnavailable&&!SpeedCalibration.Current(changed));
        });
    }
    private sealed class DotInput:IStrokeInput
    {
        public double Seconds{get;private set;}
        public ScreenPoint Cursor{get;private set;}
        public bool Held;public int Downs,Ups;public bool FailMove,FailWait;
        public List<ScreenPoint> Moves=[];
        public void Move(IReadOnlyList<ScreenPoint> points){if(Held&&FailMove)throw new OperationCanceledException();Cursor=points.Single();Moves.Add(Cursor);}
        public void Button(bool up){Held=!up;if(up)Ups++;else Downs++;}
        public void Wait(double seconds){if(Held&&FailWait)throw new OperationCanceledException();Seconds+=seconds;}
    }
}
