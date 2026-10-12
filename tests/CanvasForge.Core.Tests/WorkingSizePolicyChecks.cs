using CanvasForge.Core;

internal static class WorkingSizePolicyChecks
{
    private static void Require(bool value){if(!value)throw new Exception("Working Size policy regression");}
    public static void Run(Action<string,Action> test)
    {
        test("Normal defaults and full setup start at Size 3",()=>{
            var s=Settings.ExperimentalDefaults();Require(PaintTimingPlan.DefaultSize(s)==3&&s.Text("brush_calibration_size")=="3"&&s.Number("probe_size")==3);
            Require(SetupBrushSelection.Sizes("3",true).SequenceEqual(new double[]{3}));
            Require(SetupBrushSelection.Sizes("3/10/20",true).SequenceEqual(new double[]{3,10,20}));
        });
        test("Legacy Size 1 settings migrate without using its archived measurement",()=>{
            var s=CalibrationReliabilityChecks.Captured();var p=CalibrationReliabilityChecks.Profile(s,1);BrushFootprints.Save(s,[p]);
            s.Set("precision_brush_size","1");s.Set("brush_size_value",1);s.Set("probe_size",1);s.Set("brush_calibration_size","1/3/10/20");
            var effective=DrawingWorkflow.Effective(s);
            Require(PaintTimingPlan.DefaultSize(effective)==2&&effective.Number("brush_size_value")==2&&effective.Number("probe_size")==2&&effective.Text("brush_calibration_size")=="3/10/20");
            Require(s.Text("precision_brush_size")=="1"&&BrushFootprints.Read(effective).Any(x=>x.Size==1));
            Require(SetupBrushSelection.Select([p],3,1) is null&&DrawingWorkflow.PaintingProfiles(effective).Count==0&&!SpeedCalibration.BrushReady(effective,1));
        });
        test("Profile default stays 3 and automatic or manual painting stays at least 2",()=>{
            var s=Settings.ExperimentalDefaults();s.Set("precision_brush_size","Profile");s.Set("speed_profile","Rapid");
            Require(PaintTimingPlan.DefaultSize(DrawingWorkflow.Effective(s))==3);
            s.Set("coverage_mode","Fast");s.Set("cell_px",1);s.Set("auto_brush_size",true);
            var effective=DrawingWorkflow.Effective(s);Require(PaintTimingPlan.DefaultSize(effective)>=2&&s.Bool("auto_brush_size")&&effective.Int("cell_px")==2);
            s.Set("auto_brush_size",false);s.Set("brush_size_value",1);Require(PaintTimingPlan.DefaultSize(DrawingWorkflow.Effective(s))==2&&CalibrationReliability.PaintingProblem(s) is not null);
        });
        test("Adaptive without current measurements cannot fall back to Size 1",()=>{
            var s=Settings.ExperimentalDefaults();s.Set("adaptive_brush",true);s.Set("speed_profile","Rapid");
            Require(PaintTimingPlan.DefaultSize(DrawingWorkflow.Effective(s))==2);
            var measured=AdaptiveBrush.CalibrationSettings(s,2);Require(!measured.Bool("adaptive_brush")&&PaintTimingPlan.DefaultSize(measured)==2);
            Require(!AdaptiveBrush.CalibrationCurrent(s));
        });
        test("Every selectable calibration and probe Size is at least 2",()=>{
            Require(SetupBrushSelection.SizeOptions.All(x=>double.Parse(x,System.Globalization.CultureInfo.InvariantCulture)>=2));
            Require(SetupBrushSelection.Options.All(option=>SetupBrushSelection.Sizes(option,false).All(size=>size>=2)));
            try{SetupBrushSelection.Sizes("1/3",false);Require(false);}catch(ArgumentException){}
        });
        test("Intermediate Adaptive caps offered by UI pass settings validation",()=>{
            foreach(double cap in BrushFootprints.Sizes.Where(x=>x>=2)){var s=Settings.ExperimentalDefaults();s.Set("adaptive_max_size",cap);s.Validate();}
        });
        test("A synthetic narrow Size 2 cannot qualify painting or speed",()=>{
            var s=CalibrationReliabilityChecks.Captured();s.Set("precision_brush_size","2");BrushSpan[] possible=[new(0,0,2)];
            BrushFootprints.Save(s,[BrushFootprints.Build(s,3,2,Enumerable.Repeat(new BrushStamp(possible,[],new(20,20,20)),3).ToArray())]);
            Require(PaintingReadiness.BrushProblem(s) is not null&&!SpeedCalibration.BrushReady(s,2));
        });
        test("Only a verified Size 2 imprint qualifies the new minimum",()=>{
            var s=CalibrationReliabilityChecks.Captured();s.Set("precision_brush_size","2");BrushFootprints.Save(s,[CalibrationReliabilityChecks.Profile(s,2)]);
            Require(PaintTimingPlan.DefaultSize(s)==2&&PaintingReadiness.BrushProblem(s) is null&&SpeedCalibration.BrushReady(s,2));
        });
        test("Repair cannot fall back to archived Size 1 when a current footprint exceeds its budget",()=>{
            var s=CalibrationReliabilityChecks.Captured();
            var wide=CalibrationReliabilityChecks.Profile(s,20);
            wide=BrushFootprints.Build(s,3,20,Enumerable.Repeat(new BrushStamp(
                Enumerable.Range(-40,81).Select(y=>new BrushSpan(y,-40,41)).ToArray(),wide.Solid,new(20,20,20)),3).ToArray());
            BrushFootprints.Save(s,[CalibrationReliabilityChecks.Profile(s,1),wide]);
            Require(DrawingWorkflow.PaintingProfiles(s).Count==1&&DrawingWorkflow.RepairProfiles(s).Count==0);
            var two=CalibrationReliabilityChecks.Profile(s,2);BrushFootprints.Save(s,[CalibrationReliabilityChecks.Profile(s,1),wide,two]);
            Require(DrawingWorkflow.RepairProfiles(s).Count==1&&DrawingWorkflow.RepairProfiles(s)[0].Size==2);
        });
        test("Three strong displaced Size 2 stamps cannot authorize painting without their common core",()=>{
            var s=CalibrationReliabilityChecks.Captured();s.Set("precision_brush_size","2");
            BrushSpan[] possible=Enumerable.Range(0,8).Select(y=>new BrushSpan(y,-4,3)).ToArray();
            var stamps=new[]{new BrushStamp(possible,[new(1,-2,0)],new(20,20,20)),
                new BrushStamp(possible,[new(4,-2,0)],new(20,20,20)),new BrushStamp(possible,[new(6,-2,0)],new(20,20,20))};
            BrushFootprints.Save(s,[BrushFootprints.Build(s,3,2,stamps)]);
            Require(DrawingWorkflow.PaintingProfiles(s).Single().Solid.Length==0&&PaintingReadiness.BrushProblem(s) is not null
                &&!SpeedCalibration.BrushReady(s,2)&&DrawingWorkflow.RepairProfiles(s).Count==0);
        });
    }
}
