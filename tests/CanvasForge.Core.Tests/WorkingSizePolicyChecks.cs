using CanvasForge.Core;

internal static class WorkingSizePolicyChecks
{
    private static void Require(bool value){if(!value)throw new Exception("Working Size policy regression");}
    public static void Run(Action<string,Action> test)
    {
        test("Normal defaults and full setup start at Size 3",()=>{
            var s=Settings.Defaults();Require(PaintTimingPlan.DefaultSize(s)==3&&s.Text("brush_calibration_size")=="3"&&s.Number("probe_size")==3);
            Require(SetupBrushSelection.Sizes("3",true).SequenceEqual(new double[]{3}));
            Require(SetupBrushSelection.Sizes("3/10/20",true).SequenceEqual(new double[]{3,10,20}));
        });
        test("Explicit Size 1 remains available with its own measured evidence",()=>{
            var s=CalibrationReliabilityChecks.Captured();var p=CalibrationReliabilityChecks.Profile(s,1);BrushFootprints.Save(s,[p]);
            s.Set("precision_brush_size","1");s.Set("brush_size_value",1);s.Set("probe_size",1);s.Set("brush_calibration_size","1/3/10/20");
            var effective=DrawingWorkflow.Effective(s);
            Require(PaintTimingPlan.DefaultSize(effective)==1&&effective.Number("brush_size_value")==1&&effective.Number("probe_size")==1&&effective.Text("brush_calibration_size")=="1/3/10/20");
            Require(s.Text("precision_brush_size")=="1"&&BrushFootprints.Read(effective).Any(x=>x.Size==1));
            Require(SetupBrushSelection.Select([p],3,1) is null);
        });
        test("Profile and automatic normal painting respect Size 3 minimum",()=>{
            var s=Settings.Defaults();s.Set("precision_brush_size","Profile");s.Set("speed_profile","Rapid");
            Require(PaintTimingPlan.DefaultSize(DrawingWorkflow.Effective(s))==3);
            s.Set("coverage_mode","Fast");s.Set("cell_px",1);s.Set("auto_brush_size",true);
            var effective=DrawingWorkflow.Effective(s);Require(PaintTimingPlan.DefaultSize(effective)>=3&&s.Bool("auto_brush_size"));
            s.Set("auto_brush_size",false);s.Set("brush_size_value",1);Require(PaintTimingPlan.DefaultSize(DrawingWorkflow.Effective(s))==1&&CalibrationReliability.PaintingProblem(s) is not null);
        });
        test("Adaptive and isolated base calibration retain Size 1",()=>{
            var s=Settings.Defaults();s.Set("adaptive_brush",true);s.Set("speed_profile","Rapid");
            Require(PaintTimingPlan.DefaultSize(DrawingWorkflow.Effective(s))==1);
            var measured=AdaptiveBrush.CalibrationSettings(s,1);Require(!measured.Bool("adaptive_brush")&&PaintTimingPlan.DefaultSize(measured)==1);
            Require(!AdaptiveBrush.CalibrationCurrent(s));
        });
    }
}
