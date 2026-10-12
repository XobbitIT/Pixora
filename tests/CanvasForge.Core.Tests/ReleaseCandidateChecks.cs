using System.IO.Compression;
using CanvasForge.Core;

internal static class ReleaseCandidateChecks
{
    private static void Require(bool value){if(!value)throw new Exception("RC policy regression");}
    public static void Run(Action<string,Action> test)
    {
        test("RC Standard is default and preserves experimental preferences",()=>{
            var s=Settings.Defaults();s.Set("precision_brush_size","2");s.Set("adaptive_brush",true);s.Set("coverage_audit",true);s.Set("input_engine","Experimental 1 ms");
            var effective=DrawingWorkflow.Effective(s);
            Require(ReleaseProfile.Standard(s)&&PaintTimingPlan.DefaultSize(effective)==3&&!effective.Bool("adaptive_brush")&&!effective.Bool("coverage_audit")&&effective.Text("input_engine")=="Stable");
            Require(s.Bool("adaptive_brush")&&s.Text("precision_brush_size")=="2"&&s.Text("input_engine")=="Experimental 1 ms");
            Require(AdaptiveBrush.CalibrationSettings(s,2).Number("brush_size_value")==2&&!ReleaseProfile.Standard(AdaptiveBrush.CalibrationSettings(s,2)));
        });
        test("RC Standard needs regions and colors but no brush footprint",()=>{
            var s=CalibrationReliabilityChecks.Captured();s.Set("release_profile","Standard");s.Data.Remove("brush_footprints");s.Set("visual_brush_check_pending",true);
            var cal=s.Calibration;cal.Set("hex_verified",1);s.SetCalibration(cal);
            Require(PaintingReadiness.BrushProblem(s) is null&&CalibrationReliability.PaintingProblem(s) is null);
            Require(PaintingReadiness.Read(s,true).CanPaint&&!PaintingReadiness.Read(s,false).CanPaint);
            s.Set("controls_validation_failed",true);Require(!PaintingReadiness.Read(s,true).CanPaint);
            s.Set("release_profile","Experimental");Require(PaintingReadiness.BrushProblem(s) is not null);
        });
        test("RC Standard rejects malformed regions even without calibration",()=>{
            var s=Settings.Defaults();Require(!PaintingReadiness.Read(s,true).CanPaint);s.Set("control_confirmation","Visual");
            Require(VisualControlVerification.ReadNumber(DrawingWorkflow.Effective(s),()=>throw new Exception("Clipboard must not be read")) is null);
        });
        test("RC Standard requires the applied palette swatch",()=>{
            var s=Settings.Defaults();var c=s.Calibration;c.SetRect("canvas",new(0,0,64,64));c.SetRect("palette",new(100,0,140,160));s.SetCalibration(c);s.SetPalette([new(new(0,0,0),new(105,5),"main")]);
            Require(PaintingReadiness.Read(s,true).Steps.Single(p=>p.Key=="colors").State!=PreparationState.Ready);c.SetRect("palette_swatch",new(150,0,155,5));s.SetCalibration(c);Require(PaintingReadiness.Read(s,true).Steps.Single(p=>p.Key=="colors").State==PreparationState.Ready);
        });
        test("RC Standard stage flow excludes optional physical trials",()=>{
            var seen=new List<SetupStage>();SetupSequence.Run((stage,_)=>{seen.Add(stage);return Task.CompletedTask;},(_,_,_)=>{},default,SetupSequence.StandardStages).GetAwaiter().GetResult();
            Require(seen.SequenceEqual(new[]{SetupStage.Capture,SetupStage.Colors,SetupStage.Controls}));
        });
        test("RC interruption reports pause rather than a failed calibration",()=>{
            var states=new List<SetupStageState>();
            try{SetupSequence.Run((_,_)=>throw new PreparationInterruptedException(),(_,state,_)=>states.Add(state),default,[SetupStage.Controls]).GetAwaiter().GetResult();Require(false);}catch(PreparationInterruptedException){}
            Require(states.SequenceEqual(new[]{SetupStageState.Running,SetupStageState.Paused}));
        });
        test("RC Standard identity ignores inactive experimental preferences",()=>{
            var s=Settings.Defaults();var image=new PixelImage(8,8);string before=PlanIdentity.Compute(image,s,[]);s.Set("precision_brush_size","2");s.Set("adaptive_brush",true);s.Set("adaptive_max_size",100);s.Set("input_experimental_delay_ms",8);s.Set("edge_merge_thickness",4);s.Set("shape_spatial_profiles",new{archived=true});Require(before==PlanIdentity.Compute(image,s,[]));
        });
        test("RC grouped probe propagates focus loss without running another Size",()=>{
            var seen=new List<double>();try{ProbeBatchSequence.Run(new double[]{3,10},(_,_,_)=>Task.FromResult(true),_=>true,(size,_,_)=>{seen.Add(size);throw new PreparationInterruptedException();},(_,_,_)=>{},default).GetAwaiter().GetResult();Require(false);}catch(PreparationInterruptedException){}Require(seen.SequenceEqual(new double[]{3}));
        });
        test("RC timing partitions pauses, color retries and recovery without overlap",()=>{
            double now=0;var clock=new RunTimeBreakdown(()=>now);now=2;clock.Switch("color");now=5;clock.Pause();now=15;clock.Resume();now=17;clock.Switch("motion");now=21;clock.Switch("service");now=22;
            var result=clock.Read();Require(result.Colors==5&&result.Motion==4&&result.Service==3&&result.Pause==10&&result.Wall==22&&result.Active==12);
            Require(result.Wall==result.Colors+result.Motion+result.Service+result.Pause);
        });
        test("RC timing retains an unfinished pause at Stop",()=>{
            double now=0;var clock=new RunTimeBreakdown(()=>now);now=3;clock.Pause();now=12;var result=clock.Read();Require(result.Service==3&&result.Pause==9&&result.Wall==12);
        });
        test("RC diagnostics bundles current logs and evidence only",()=>{
            string root=Path.Combine(Path.GetTempPath(),"pixora-export-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);Directory.CreateDirectory(Path.Combine(root,"brush-calibration","run"));
            File.WriteAllText(Path.Combine(root,"config-csharp.json"),"{\"language\":\"Українська\"}");File.WriteAllText(Path.Combine(root,"session-csharp.jsonl"),"test\n");File.WriteAllText(Path.Combine(root,"secret.txt"),"excluded");
            File.WriteAllText(Path.Combine(root,"brush-calibration","run","image.png"),"fixture");File.WriteAllText(Path.Combine(root,"brush-calibration","run","Pixora.exe"),"excluded");
            string zip=Path.Combine(root,"export.zip");DiagnosticBundle.Export(root,zip,"rc-test",attachments:new Dictionary<string,byte[]>{{"current/source.png",[1,2,3]}});using var archive=ZipFile.OpenRead(zip);
            Require(archive.Entries.Select(e=>e.FullName).ToHashSet().SetEquals(new[]{"config-csharp.json","session-csharp.jsonl","brush-calibration/run/image.png","diagnostic-export.json","current/source.png"}));
            using var reader=new StreamReader(archive.GetEntry("config-csharp.json")!.Open());Require(reader.ReadToEnd().Contains("Українська"));
        });
        test("RC diagnostic attachment traversal fails without replacing a prior ZIP",()=>{
            string root=Path.Combine(Path.GetTempPath(),"pixora-export-path-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string zip=Path.Combine(root,"export.zip");File.WriteAllText(zip,"prior");try{DiagnosticBundle.Export(root,zip,"test",attachments:new Dictionary<string,byte[]>{{"current/../escape.json",[1]}});Require(false);}catch(IOException){}Require(File.ReadAllText(zip)=="prior");
        });
        test("RC current diagnostic attachments override persisted files without duplicate ZIP entries",()=>{
            string root=Path.Combine(Path.GetTempPath(),"pixora-export-unique-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"last-run"));File.WriteAllText(Path.Combine(root,"last-run","target.png"),"old");string zip=Path.Combine(root,"export.zip");DiagnosticBundle.Export(root,zip,"test",attachments:new Dictionary<string,byte[]>{{"last-run/target.png",[1,2]}});using var archive=ZipFile.OpenRead(zip);Require(archive.Entries.Count(e=>e.FullName=="last-run/target.png")==1);using var image=archive.GetEntry("last-run/target.png")!.Open();Require(image.ReadByte()==1);
        });
        test("RC cancelled diagnostic export preserves the prior archive",()=>{
            string root=Path.Combine(Path.GetTempPath(),"pixora-export-cancel-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"session-csharp.jsonl"),"test");string zip=Path.Combine(root,"export.zip");File.WriteAllText(zip,"prior");
            using var cancel=new CancellationTokenSource();cancel.Cancel();try{DiagnosticBundle.Export(root,zip,"test",cancel.Token);Require(false);}catch(OperationCanceledException){}
            Require(File.ReadAllText(zip)=="prior"&&!Directory.EnumerateFiles(root,"*.tmp").Any());
        });
    }
}
