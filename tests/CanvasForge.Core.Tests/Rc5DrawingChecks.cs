using CanvasForge.Core;
using System.Text.Json.Nodes;

internal static class Rc5DrawingChecks
{
    private static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    private static (Settings Settings,BrushFootprint Profile,ScreenLine Line,PixelImage Before,PixelImage First) Fixture(bool vertical=false)
    {
        var s=MeasuredColorChecks.Config();BrushSpan[] possible=[new(-2,-2,3),new(-1,-2,3),new(0,-2,3),new(1,-2,3),new(2,-2,3)];
        BrushSpan[] solid=[new(-1,-1,2),new(0,-1,2),new(1,-1,2)];var stamp=new BrushStamp(possible,solid,new(20,20,20));
        var p=BrushFootprints.Build(s,3,5,[stamp,stamp,stamp]);BrushFootprints.Save(s,[p]);
        var before=new PixelImage(32,32);for(int i=0;i<32*32;i++)before.Set(i,new(200,200,200));
        var first=before.Clone();var line=vertical?new ScreenLine(16,8,16,23):new ScreenLine(8,16,23,16);
        foreach(var row in BrushSweep.Project(possible,line))for(int x=row.Left;x<row.Right;x++)first.Set(row.Y*32+x,new(20,20,20));
        return(s,p,line,before,first);
    }
    public static void Run(Action<string,Action> test)
    {
        foreach(bool vertical in new[]{false,true})test("RC5 moving footprint matches actual pixels on "+(vertical?"V":"H"),()=>{
            var f=Fixture(vertical);var result=BrushMotionEvidence.Inspect(f.Profile,f.Line,f.Before,f.Before,f.First,f.First,new(0,0,0),true);
            Require(result.Passed&&result.ExpectedSolid==54&&result.Vertical==vertical,"Valid cardinal saturation evidence rejected");
        });
        test("RC5 motion rejects fringe outside the dot mask at delta 13 without changing tolerance 12",()=>{
            var f=Fixture();var first=f.First.Clone();first.Set(0,new(188,188,188));
            Require(BrushMotionEvidence.Inspect(f.Profile,f.Line,f.Before,f.Before,first,first,new(0,0,0),true).Passed,"Delta 12 became paint");
            first.Set(0,new(187,187,187));var bad=BrushMotionEvidence.Inspect(f.Profile,f.Line,f.Before,f.Before,first,first,new(0,0,0),true);
            Require(!bad.Passed&&bad.OutsidePossible==1&&bad.Reason=="moving_footprint_outside_dot_mask","Moving fringe escaped safety mask");
        });
        test("RC5 moving proof rejects gaps recoloring wrong source and unstable frames",()=>{
            var f=Fixture();var gap=f.First.Clone();gap.Set(16*32+16,new(200,200,200));
            Require(!BrushMotionEvidence.Inspect(f.Profile,f.Line,f.Before,f.Before,gap,gap,new(0,0,0),true).Passed,"Gap certified");
            var changed=f.First.Clone();changed.Set(16*32+16,new(33,33,33));
            Require(!BrushMotionEvidence.Inspect(f.Profile,f.Line,f.Before,f.Before,f.First,changed,new(0,0,0),true).Passed,"Second pass recoloring certified");
            Require(!BrushMotionEvidence.Inspect(f.Profile,f.Line,f.Before,f.Before,f.First,f.First,new(255,255,255),true).Passed,"Wrong color direction certified");
            Require(!BrushMotionEvidence.Inspect(f.Profile,f.Line,f.Before,f.Before,f.First,f.First,new(0,0,0),false).Passed,"Unverified source certified");
            var noise=f.Before.Clone();noise.Set(0,new(187,187,187));
            Require(!BrushMotionEvidence.Inspect(f.Profile,f.Line,noise,f.Before,f.First,f.First,new(0,0,0),true).Passed,"Frame noise beyond 12 certified");
        });
        test("RC5 motion still requires contrast 80 and rejects clipped masks",()=>{
            var f=Fixture();PixelImage Painted(int value){var im=f.Before.Clone();foreach(var row in BrushSweep.Project(f.Profile.Possible,f.Line))for(int x=row.Left;x<row.Right;x++)im.Set(row.Y*32+x,new((byte)value,(byte)value,(byte)value));return im;}
            var at80=Painted(120);var at79=Painted(121);
            Require(BrushMotionEvidence.Inspect(f.Profile,f.Line,f.Before,f.Before,at80,at80,new(0,0,0),true).Passed,"Contrast 80 rejected");
            Require(!BrushMotionEvidence.Inspect(f.Profile,f.Line,f.Before,f.Before,at79,at79,new(0,0,0),true).Passed,"Contrast 79 certified");
            bool clipped=false;try{BrushMotionEvidence.Inspect(f.Profile,new(0,0,23,0),f.Before,f.Before,f.First,f.First,new(0,0,0),true);}catch(System.IO.InvalidDataException){clipped=true;}Require(clipped,"Clipped motion certified");
        });
        test("RC5 motion certificates require both axes current profile transport and completed recheck",()=>{
            var f=Fixture();var h=BrushMotionEvidence.Inspect(f.Profile,f.Line,f.Before,f.Before,f.First,f.First,new(0,0,0),true);
            Require(!BrushMotionEvidence.Current(f.Settings,f.Profile),"Static dots certified a wide stroke");
            BrushMotionEvidence.Save(f.Settings,f.Profile,[h,h]);Require(!BrushMotionEvidence.Current(f.Settings,f.Profile),"Duplicate H axis certified V");
            var v=Fixture(true);var vc=BrushMotionEvidence.Inspect(v.Profile,v.Line,v.Before,v.Before,v.First,v.First,new(0,0,0),true);
            BrushMotionEvidence.Save(f.Settings,f.Profile,[h,vc]);Require(BrushMotionEvidence.Current(f.Settings,f.Profile),"Two axes rejected");
            var other=f.Settings.Clone();other.Set("input_frame_delay_ms",40);Require(!BrushMotionEvidence.Current(other,f.Profile),"Changed transport retained proof");
            BrushMotionEvidence.Save(f.Settings,f.Profile,[]);Require(!BrushMotionEvidence.Current(f.Settings,f.Profile),"Interrupted recheck kept the old proof");
            f.Settings.Data["brush_motion_proofs"]=JsonNode.Parse("[null,{\"ProfileId\":\"x\",\"Axes\":null}]");Require(!BrushMotionEvidence.Current(f.Settings,f.Profile),"Malformed proof accepted");
        });
        test("RC5 Adaptive excludes untested wide brushes and uses stable unconnected production strokes",()=>{
            var s=MeasuredColorChecks.Config();s.Data.Remove("brush_motion_proofs");s.Set("fast_transfer",true);s.Set("input_engine","Experimental 1 ms");s.Set("input_frame_delay_ms",16);
            var effective=DrawingWorkflow.Effective(s);var result=MeasuredColorPlan.TryBuild(MeasuredColorChecks.Plan(),s)!;
            Require(result.Groups.Values.SelectMany(g=>g).All(p=>p.Size==3),"Untested wide brush emitted");
            Require(!TransferSchedule.Fast(s)&&!new StrokeExecutionPlan(s).Fast&&effective.Text("input_engine")=="Stable"&&StrokeTiming.Frame(effective)==.02,"Adaptive used an unchecked fast route");
            Require(AdaptiveBrush.CalibrationCurrent(s),"Failed wide check blocked ordinary detail fallback");
        });
        test("RC5 background removal migration preserves opaque black and explicit alpha",()=>{
            var s=Settings.ExperimentalDefaults();s.Set("remove_bg",true);s.Set("fill_subject",false);SettingsStore.Migrate(s.Data);Require(!s.Data.ContainsKey("remove_bg"),"Removed flag retained");
            var im=new PixelImage(8,8);for(int i=0;i<64;i++)im.Set(i,i%2==0?new(0,0,0):new(255,255,255));im.Set(63,new(0,0,0),0);
            s.Set("remove_bg",true);s.Set("fit_mode","fit whole");s.Set("preblur",0);var prepared=ImageProcessing.Prepare(im,8,8,s,default);
            Require(prepared.Rgba.SequenceEqual(im.Rgba),"Legacy background flag erased black or explicit alpha");
        });
        test("RC5 one-button preparation completes six stages and keeps Standard after optional failures",()=>{
            var called=new List<SetupStage>();var states=new Dictionary<SetupStage,SetupStageState>();
            bool ok=SetupSequence.Prepare((stage,_)=>{called.Add(stage);return Task.CompletedTask;},(stage,state,_)=>states[stage]=state,default,true).GetAwaiter().GetResult();
            Require(ok&&called.SequenceEqual(SetupSequence.Stages)&&states.Values.All(v=>v==SetupStageState.Passed),"Full preparation omitted a stage");
            foreach(var failure in new[]{SetupStage.Brush,SetupStage.Spatial,SetupStage.Speed})
            {
                called.Clear();bool accelerated=SetupSequence.Prepare((stage,_)=>{called.Add(stage);if(stage==failure)throw new InvalidOperationException("optional failed");return Task.CompletedTask;},(_,_,_)=>{},default,true).GetAwaiter().GetResult();
                Require(!accelerated&&SetupSequence.StandardStages.All(called.Contains),"Optional check blocked Standard controls");
            }
        });
        test("RC5 unsupported calibration choices migrate without deleting historical measurements",()=>{
            var s=MeasuredColorChecks.Config();s.Set("probe_size",40);s.Set("precision_brush_size","100");s.Set("adaptive_max_size",60);s.Set("probe_sizes","3/15/40");
            string masks=s.Data["brush_footprints"]!.ToJsonString();DrawingWorkflow.Normalize(s);
            Require(s.Number("probe_size")==3&&s.Number("precision_brush_size")==3&&s.Number("adaptive_max_size")==20&&s.Text("probe_sizes")=="3/15"&&s.Data["brush_footprints"]!.ToJsonString()==masks,"Supported-size migration destroyed evidence");
        });
    }
}
