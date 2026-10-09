using CanvasForge.Core;

internal static class PreparationAndPaletteChecks
{
    private static void Require(bool ok,string message="Preparation/color regression"){if(!ok)throw new Exception(message);}
    internal static void Run(Action<string,Action> test)
    {
        var c=new Calibration(new());c.SetRect("palette",new(100,200,500,840));
        var point=c.GridCenters("palette",4,16)[42];var entry=new PaletteEntry(new(51,133,255),point,"main");
        test("palette-target-matches-two-stable-reads",()=>
        {int reads=0,waits=0;var proof=PaletteSelection.Verify(c,entry,point,_=>{reads++;return entry.Color;},()=>waits++);Require(proof.Passed&&reads==2&&waits==1);});
        test("palette-target-stale-color-stops-before-input",()=>
        {bool applied=false;try{PaletteSelection.Verify(c,entry,point,_=>new(255,255,255),()=>{});applied=true;}catch(PaletteTargetException e){Require(!e.Evidence.Passed&&e.Evidence.First==new Rgb(255,255,255));}Require(!applied);});
        test("palette-target-second-frame-change-is-rejected",()=>
        {int reads=0;bool rejected=false;try{PaletteSelection.Verify(c,entry,point,_=>++reads==1?entry.Color:new(51,51,255),()=>{});}catch(PaletteTargetException){rejected=true;}Require(rejected);});
        test("palette-target-wrong-grid-center-is-rejected",()=>
        {int reads=0;try{PaletteSelection.Verify(c,entry,new(point.X+8,point.Y),_=>{reads++;return entry.Color;},()=>{});throw new Exception("Invalid point accepted");}catch(InvalidOperationException){Require(reads==0);}});
        test("palette-target-rebased-grid-remains-valid",()=>
        {var shifted=new Calibration(new());shifted.SetRect("palette",new(117,221,517,861));var p=new ScreenPoint(point.X+17,point.Y+21);Require(PaletteSelection.Verify(shifted,entry,p,_=>entry.Color,()=>{}).Passed);});
        test("palette-coordinate-transport-rebases-plan-once-and-skips-aligned-settings",()=>
        {
            var transform=new CoordinateRebase(0,0,96,17,21,96);
            var expected=new ScreenPoint(point.X+17,point.Y+21);
            Require(PaletteSelection.ClickPoint(entry,transform)==expected);
            var aligned=entry with{ClickPoint=expected};
            Require(PaletteSelection.ClickPoint(aligned,transform,alreadyAligned:true)==expected);
        });
        test("palette-target-quick-colors-use-own-grid",()=>
        {c.SetRect("quick",new(50,200,90,600));c.Set("quick_rows",10);var p=c.GridCenters("quick",1,10)[3];Require(PaletteSelection.SampleArea(c,p,"quick").Width<40);});
        test("palette-logo-cap-preserves-dominant-colors-with-skin-assist",()=>
        {
            var s=Settings.Defaults();var cal=s.Calibration;cal.SetRect("canvas",new(0,0,32,24));s.SetCalibration(cal);
            s.Set("cell_px",1);s.Set("max_colors","3");s.Set("preblur",0);s.Set("median_cleanup",false);s.Set("skin_assist",true);
            var colors=new[]{new Rgb(0,91,187),new Rgb(255,213,0),new Rgb(255,255,255)};
            s.SetPalette(colors.Append(new(220,160,110)).Select((rgb,i)=>new PaletteEntry(rgb,new(100+i,50),"main")));
            var im=new PixelImage(32,24);for(int i=0;i<32*24;i++)im.Set(i,colors[i%32/11]);
            var plan=Planner.Build(im,s);Require(plan.ColorCount==3&&plan.Counts.Keys.All(i=>i<3),"Unused skin shade displaced a logo color");
        });
        test("readiness-rejects-legacy-diameter-only-brush",()=>
        {var s=Settings.Defaults();s.Set("brush_calibration_points",new double[][]{[3,9,3]});s.Set("brush_calibration_context",AdaptiveBrush.Context(s));Require(PaintingReadiness.BrushProblem(s) is not null);});
        test("readiness-has-one-canonical-set-of-required-steps",()=>
        {var s=Settings.Defaults();var state=PaintingReadiness.Read(s,false);Require(!state.CanPaint&&state.FirstIncomplete?.Key=="image"&&state.Steps.Count==5&&state.Steps.All(p=>p.State!=PreparationState.Ready));});
        test("readiness-can-switch-to-uncaptured-hex-without-throwing",()=>
        {var s=Settings.Defaults();s.Set("color_mode","HEX Direct");var state=PaintingReadiness.Read(s,true);Require(!state.CanPaint&&state.Steps.Single(p=>p.Key=="colors").State!=PreparationState.Ready&&state.Steps.Single(p=>p.Key=="controls").State!=PreparationState.Ready);});
        test("preparation-history-and-error-indicators-do-not-alter-plan-identity",()=>
        {var s=Settings.Defaults();var image=new PixelImage(4,4);var id=PlanIdentity.Compute(image,s,s.Palette());s.Set("preparation_history",new{context="test",stages=new[]{"Brush"}});s.Set("palette_target_failed",true);s.Set("controls_validation_failed",true);Require(PlanIdentity.Compute(image,s,s.Palette())==id);});
        test("unified-preparation-orders-all-stages",()=>
        {var seen=new List<SetupStage>();var ok=SetupSequence.Prepare((s,t)=>{seen.Add(s);return Task.CompletedTask;},(s,state,d)=>{},default).GetAwaiter().GetResult();Require(ok&&seen.SequenceEqual(SetupSequence.Stages));});
        test("unified-preparation-spatial-failure-retains-basic-proof",()=>
        {var seen=new List<SetupStage>();var passed=new List<SetupStage>();var ok=SetupSequence.Prepare((s,t)=>{seen.Add(s);if(s==SetupStage.Spatial)throw new InvalidOperationException("unstable");return Task.CompletedTask;},(s,state,d)=>{if(state==SetupStageState.Passed)passed.Add(s);},default).GetAwaiter().GetResult();Require(!ok&&!seen.Contains(SetupStage.Speed)&&passed.SequenceEqual(SetupSequence.DrawingStages));});
        test("unified-preparation-speed-failure-does-not-certify-acceleration",()=>
        {var failed=new List<SetupStage>();var ok=SetupSequence.Prepare((s,t)=>s==SetupStage.Speed?Task.FromException(new InvalidOperationException("gaps")):Task.CompletedTask,(s,state,d)=>{if(state==SetupStageState.Failed)failed.Add(s);},default).GetAwaiter().GetResult();Require(!ok&&failed.SequenceEqual(new[]{SetupStage.Speed}));});
        test("unified-preparation-required-failure-stops-input",()=>
        {var seen=new List<SetupStage>();try{SetupSequence.Prepare((s,t)=>{seen.Add(s);if(s==SetupStage.Colors)throw new InvalidOperationException("wrong color");return Task.CompletedTask;},(s,state,d)=>{},default).GetAwaiter().GetResult();throw new Exception("Failure ignored");}catch(SetupStageException e){Require(e.Stage==SetupStage.Colors&&seen.SequenceEqual(new[]{SetupStage.Capture,SetupStage.Colors}));}});
        test("unified-preparation-cancellation-never-enables-speed",()=>
        {using var stop=new CancellationTokenSource();bool passed=false;try{SetupSequence.Prepare((s,t)=>{if(s==SetupStage.Spatial)stop.Cancel();return Task.CompletedTask;},(s,state,d)=>{if(s==SetupStage.Speed&&state==SetupStageState.Passed)passed=true;},stop.Token).GetAwaiter().GetResult();throw new Exception("Cancellation ignored");}catch(OperationCanceledException){Require(!passed);}});
    }
}
