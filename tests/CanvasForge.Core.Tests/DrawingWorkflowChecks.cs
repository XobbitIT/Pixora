using CanvasForge.Core;

internal static class DrawingWorkflowChecks
{
    private static void Require(bool value) { if (!value) throw new Exception("Drawing workflow regression"); }
    private static Settings Captured(bool hex = false)
    {
        var s = Settings.Defaults(); s.Set("color_mode", hex ? "HEX Direct" : "Rust Palette");
        var c = s.Calibration; c.SetRect("canvas", new(10,10,26,26)); c.SetSession(new(0,0),96,new(100,100));
        c.SetRect("palette", new(30,10,70,74)); if(hex)c.SetPoint("hex_field",new(80,80)); s.SetCalibration(c);
        s.SetPalette([new(new(0,0,0),new(35,15),"main")]); return s;
    }
    public static void Run(Action<string,Action> test)
    {
        test("Simple workflow is the default for fresh and legacy UI settings",()=>Require(DrawingWorkflow.Simple(Settings.Defaults())));
        test("Simple workflow isolates stale advanced flags without destroying preferences",()=>{
            var s=Captured(); s.Set("adaptive_brush",true);s.Set("coverage_audit",true);s.Set("calibrated_strokes",true);s.Set("input_engine","Experimental 1 ms");
            string original=s.Data.ToJsonString();var effective=DrawingWorkflow.Effective(s);
            Require(s.Data.ToJsonString()==original&&!effective.Bool("adaptive_brush")&&!effective.Bool("coverage_audit")&&!effective.Bool("calibrated_strokes"));
            Require(effective.Bool("manual_brush_controls")&&effective.Text("input_engine")=="Stable"&&effective.Number("input_frame_delay_ms")==25);effective.Validate();
        });
        test("Simple palette painting needs no brush masks sliders or probe",()=>Require(DrawingWorkflow.SimpleSetupProblem(Captured()) is null));
        test("Simple HEX needs a field but not a separate multi-color control test",()=>{
            var s=Captured(true);Require(!s.HexControlsReady&&!s.Calibration.HexReady&&DrawingWorkflow.SimpleSetupProblem(s) is null);
            Require(DrawingWorkflow.Effective(s).PaintCalibration().HexPoint.HasValue);
            var c=s.Calibration;c.SetPoint("hex_field",default);s.SetCalibration(c);Require(DrawingWorkflow.SimpleSetupProblem(s) is not null);
        });
        test("Simple painting rejects palette commands outside selected color regions",()=>{
            var s=Captured();s.SetPalette([new(new(0,0,0),new(90,95),"main")]);Require(DrawingWorkflow.SimpleSetupProblem(s) is not null);
        });
        test("Simple painting still requires a captured window session",()=>{
            var s=Captured();var c=s.Calibration;c.Data.Remove("session_client_x");s.SetCalibration(c);Require(DrawingWorkflow.SimpleSetupProblem(s) is not null);
        });
        test("Advanced workflow keeps measured and audited settings strict",()=>{
            var s=Captured(true);s.Set("drawing_mode","Advanced");s.Set("coverage_audit",true);s.Set("adaptive_brush",true);s.Set("manual_brush_controls",true);
            var e=DrawingWorkflow.Effective(s);Require(e.Bool("coverage_audit")&&e.Bool("adaptive_brush")&&!e.Bool("manual_brush_controls"));
            bool rejected=false;try{e.PaintCalibration();}catch(InvalidOperationException){rejected=true;}Require(rejected);
        });
        test("Simple HEX planner and timing agree without captured numeric controls",()=>{
            var s=DrawingWorkflow.Effective(Captured(true));var image=new PixelImage(4,4);for(int i=0;i<16;i++)image.Set(i,i%2==0?new(255,0,0):new(0,0,0));
            var p=Planner.Build(image,s);Require(p.Identity==PlanIdentity.Compute(image,s,p.Palette));
            var groups=TransferSchedule.Build(p,s);Require(groups.Count>0&&groups.Count==p.Counts.Count&&groups.Values.SelectMany(x=>x).All(x=>x.Size==0));
            var timing=PaintTimingPlan.Build(s,groups,TransferSchedule.Order(p,groups));Require(timing[0].PlannedSeconds==0&&PaintTimingPlan.DefaultSize(s)==1);
        });
    }
}
