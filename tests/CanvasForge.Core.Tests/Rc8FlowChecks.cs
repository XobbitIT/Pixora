using CanvasForge.Core;
using System.IO.Compression;

internal static class Rc8FlowChecks
{
    private static void Need(bool value,string reason){if(!value)throw new Exception(reason);}
    public static void Run(Action<string,Action> test)
    {
        test("RC8 changed Canvas is recaptured and proved in the same setup transaction before Colors",()=>{
            bool current=true;int reads=0,colors=0,captures=0;
            var failed=new EditorEvidenceResult(EditorEvidenceState.Missing,"editor_structure_mismatch",["canvas-structure"]);
            var present=new EditorEvidenceResult(EditorEvidenceState.Present,"present",[]);
            var result=SetupSceneFlow.Ensure(()=>current,()=>Task.FromResult(++reads==1?failed:present),scope=>{
                Need(scope==SetupCaptureScope.Canvas,"Unrelated controls requested");captures++;return Task.CompletedTask;
            }).GetAwaiter().GetResult();
            if(result.Evidence.State==EditorEvidenceState.Present)colors++;
            Need(reads==2&&captures==1&&colors==1&&result.PreviousReason==failed.Reason,"Capture/proof order failed");
            Need(SetupSceneFlow.Scope(true,present)==SetupCaptureScope.None,"Valid editor forced recapture");
            Need(SetupSceneFlow.Scope(true,failed with{FailedGroups=["mode-ui","canvas-structure"]})==SetupCaptureScope.Regions,"Changed UI retained old controls");
        });
        test("RC8 cancelled ambiguous or incomplete capture never reaches later invasive setup stages",()=>{
            foreach(string scenario in new[]{"cancel","still-missing","metadata-missing"})
            {
                int reads=0,captures=0;bool current=false,continued=false;Exception? failure=null;
                try
                {
                    SetupSceneFlow.Ensure(()=>current,()=>{reads++;return Task.FromResult(new EditorEvidenceResult(EditorEvidenceState.Ambiguous,"unavailable",["canvas-structure"]));},scope=>{
                        Need(scope==SetupCaptureScope.Regions,"Fresh setup skipped capture");captures++;
                        if(scenario=="cancel")throw new OperationCanceledException();
                        current=scenario!="metadata-missing";return Task.CompletedTask;
                    }).GetAwaiter().GetResult();continued=true;
                }
                catch(Exception e) when(e is OperationCanceledException or InvalidDataException){failure=e;}
                Need(!continued&&failure is not null&&captures==1&&reads==(scenario=="still-missing"?1:0),"Failed capture allowed Colors or retried indefinitely");
            }
        });
        test("RC8 preview ETA shares execution work including transport costs and one size-only setup",()=>{
            foreach(string mode in new[]{"Rust Palette","HEX Direct"})
            {
                var s=Settings.Defaults();s.Set("release_profile","Standard");s.Set("color_mode",mode);s.Set("start_delay",5);
                var c=s.Calibration;c.SetRect("canvas",new(10,10,90,90));s.SetCalibration(c);
                s.SetPalette([new(new(0,0,0),new(100,100),"main")]);
                var image=new PixelImage(8,8);for(int i=0;i<64;i++)image.Set(i,new(0,0,0));
                var effective=DrawingWorkflow.Effective(s);var p=Planner.Build(image,effective);
                var groups=TransferSchedule.Build(p,effective);var order=TransferSchedule.Order(p,groups);
                double before=Coverage.EstimateSeconds(p,effective,preparedGroups:groups);
                effective.Set("motion_transport_rate",new MotionTransportRate(40,.0002,.03));
                var work=PaintTimingPlan.Build(effective,groups,order);
                double after=Coverage.EstimateSeconds(p,effective,preparedGroups:groups);
                Need(Math.Abs(after-(5+work.Sum(w=>w.PlannedSeconds)))<1e-9,"Preview/runtime ETA diverged");
                Need(after>before,"Preview ignored measured transport");
                Need(Math.Abs(work.Single(w=>w.Id==PaintTimingPlan.Setup).PlannedSeconds-StrokeTiming.SliderChangeEstimate(effective)-StrokeTiming.ClickEstimate(effective))<1e-9,"Retired sliders counted");
                Need(work.Any(w=>w.Id==PaintTimingPlan.NativeBefore)&&work.Any(w=>w.Id==PaintTimingPlan.NativeAfter),"Evidence work omitted");
                var resume=PaintTimingPlan.Build(effective,groups,order,0,groups[order[0]].Count/2);
                Need(resume.Count(w=>w.Motion)<work.Count(w=>w.Motion),"Resume repeats completed motion work");
            }
        });
        test("RC8 diagnostic export preserves native and command evidence while excluding unrelated archives",()=>{
            string root=Path.Combine(Path.GetTempPath(),"rc8-export-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            foreach(string name in new[]{"last-run/run/command-plan.json.gz","canvas-captures/seed.rgba.gz","editor-diagnostics/frame.png","last-run/run/unrelated.zip","standard-line-diagnostics/attempt/after-first.png","standard-line-diagnostics/attempt/attempt.json"})
            {string path=Path.Combine(root,name);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,"fixture");}
            string zip=Path.Combine(root,"diagnostics.zip");DiagnosticBundle.Export(root,zip,"test");
            using var archive=ZipFile.OpenRead(zip);var files=archive.Entries.Select(e=>e.FullName).ToArray();
            Need(files.Contains("last-run/run/command-plan.json.gz")&&files.Contains("canvas-captures/seed.rgba.gz")&&files.Contains("editor-diagnostics/frame.png"),"Evidence missing from export");
            Need(files.Contains("standard-line-diagnostics/attempt/after-first.png")&&files.Contains("standard-line-diagnostics/attempt/attempt.json"),"Straight-line diagnostic omitted from export");
            Need(!files.Any(f=>f.EndsWith(".zip")),"Unrelated nested archive exported");
        });
    }
}
