using CanvasForge.Core;

internal static class CalibrationReliabilityChecks
{
    private static void Require(bool value){if(!value)throw new Exception("Calibration reliability regression");}
    private sealed class Input:IControlReadbackInput
    {
        public double Time,Latency,CopiedAt;public int Copies;public string Response="3.00",Marker=ControlNumber.Marker;
        public void SelectField(){}public void SelectAll(){}public void WriteMarker(string marker)=>Marker=marker;
        public void Copy(){Copies++;CopiedAt=Time;}public void Wait(double seconds)=>Time+=seconds;
        public string? Read()=>Time-CopiedAt+1e-9>=Latency?Response:Marker;
        public void Commit(){}
    }
    internal static Settings Captured()
    {
        var s=Settings.Defaults();s.Set("color_mode","HEX Direct");s.Set("precision_brush_size","3");var c=s.Calibration;
        c.SetRect("canvas",new(0,0,640,640));c.SetSession(new(0,0),96,new(1280,720));c.SetPoint("hex_field",new(1100,600));
        c.SetRect("brush_shapes",new(900,100,1250,150));
        foreach(string kind in new[]{"size","interval","opacity"})
        {c.SetRect(kind+"_track",new(900,200,1150,230));c.SetRect(kind+"_value_field",new(1150,200,1230,230));}
        s.SetCalibration(c);s.Data["hex_controls"]=c.Data.DeepClone();return s;
    }
    private static PixelImage Frame(byte value)
    {var image=new PixelImage(96,96);for(int i=0;i<96*96;i++)image.Set(i,new(value,value,value));return image;}
    internal static BrushFootprint Profile(Settings s,double size=3)
    {
        var before=Frame(180);var after=before.Clone();var command=new ScreenPoint(48,48);
        for(int y=47;y<=49;y++)for(int x=47;x<=49;x++)after.Set(y*96+x,new(10,10,10));
        var batch=new BrushCalibrationBatch(s,3,[size]);
        for(int i=1;i<=3;i++)
        {
            var confirmed=BrushLocalColor.Confirm(before,after,command,size,new(0,0,0),0,after.Clone);
            batch.RecordLocal(size,i,before,before,after,confirmed,command,new(0,0,0));
        }
        return batch.Profiles.Single();
    }
    public static void Run(Action<string,Action> test)
    {
        test("Calibration budgets preserve beta45 defaults",()=>{
            var s=Settings.Defaults();s.Validate();Require(CalibrationReliability.Readback(s)==new ReadbackOptions());
            Require(CalibrationReliability.StableAttempts(s)==5&&CalibrationReliability.StableInterval(s)==.08&&!CalibrationReliability.Visual(s));
        });
        test("Slow laptop preset disables fast input and raises only calibration budgets",()=>{
            var s=Settings.Defaults();s.Set("fast_transfer",true);s.Set("input_engine","Experimental 1 ms");
            string color=s.Text("color_mode"),profile=s.Text("profile");CalibrationReliability.ApplyPreset(s,true);s.Validate();
            Require(!s.Bool("fast_transfer")&&s.Text("input_engine")=="Stable"&&CalibrationReliability.Visual(s));
            Require(CalibrationReliability.Readback(s)==new ReadbackOptions(54,3,.2)&&CalibrationReliability.StableAttempts(s)==10);
            Require(s.Text("color_mode")==color&&s.Text("profile")==profile&&s.Number("paint_opacity_value")==1);
        });
        test("Slow numeric readback receives a two-second late copy without relaxing match",()=>{
            var normal=new Input{Latency=2};Require(!ControlReadback.Read("size",3,normal,.15).Verified);
            var slow=new Input{Latency=2};var s=Settings.Defaults();CalibrationReliability.ApplyPreset(s,true);
            var result=ControlReadback.Read("size",3,slow,.15,options:CalibrationReliability.Readback(s));
            Require(result.Verified&&result.Attempts==1&&slow.Copies==1&&slow.Time>=2&&slow.Time<2.7);
            slow=new Input{Response="3.01"};Require(!ControlReadback.Read("size",3,slow,.15,options:CalibrationReliability.Readback(s)).Verified);
        });
        test("HEX shares the configurable late-copy budget and exact color match",()=>{
            var input=new Input{Latency=2,Response="#FF3333"};
            Require(new HexReadback(new(255,51,51)).Read(input,.15,options:new(54,3,.2)).Verified&&input.Copies==1);
            input=new Input{Response="#FF3334"};Require(!new HexReadback(new(255,51,51)).Read(input,.15,options:new(54,3,.2)).Verified);
        });
        test("Missing readback remains bounded at the configured attempt count",()=>{
            var input=new Input{Latency=100};var result=ControlReadback.Read("size",3,input,.15,options:new(54,2,.2));
            Require(!result.Verified&&result.Reads==108&&input.Copies==2&&input.Time<6);
        });
        test("Malformed calibration budgets fail validation",()=>{
            foreach(var (key,value) in new[]{("readback_polls",23d),("readback_polls",24.5),("readback_attempts",0d),
                ("readback_retry_pause_ms",1001d),("capture_stable_attempts",4d),("capture_stable_interval_ms",39d)})
            {var s=Settings.Defaults();s.Set(key,value);bool rejected=false;try{s.Validate();}catch(InvalidDataException){rejected=true;}Require(rejected);}
        });
        test("Visual controls retain precise slider geometry and reject drift or missing bands",()=>{
            foreach(string kind in new[]{"size","interval","opacity"})
            {
                double value=kind=="size"?3:kind=="interval"?.01:1;
                var read=new SliderObservation(new(20,25,270,55),ControlCurve.Fraction(kind,value)){ValueField=new(270,25,350,55)};
                Require(VisualControlVerification.Read(kind,value,()=>read,_=>{},.08).Verified);
                Require(!VisualControlVerification.Read(kind,value,()=>read with{Fraction=read.Fraction+.01},_=>{},.08).Verified);
                int n=0;Require(!VisualControlVerification.Read(kind,value,()=>n++==0?read:null,_=>{},.08).Verified);
            }
        });
        test("Visual Size3 Size10 Size20 can be measured with zero numeric clipboard copies",()=>{
            var s=Captured();CalibrationReliability.ApplyPreset(s,true);var clipboard=new Input{Latency=100};
            foreach(double size in new double[]{3,10,20})
            {
                Require(VisualControlVerification.ReadNumber(s,()=>ControlReadback.Read("size",size,clipboard,.15).Number) is null);
                var bar=new SliderObservation(new(20,25,270,55),ControlCurve.Fraction("size",size)){ValueField=new(270,25,350,55)};
                Require(VisualControlVerification.Read("size",size,()=>bar,_=>{},.16).Verified);
                var p=Profile(s,size);Require(p.SolidCore.Valid&&p.Repeats==3);BrushFootprints.Save(s,[p]);
            }
            Require(clipboard.Copies==0&&BrushFootprints.Read(s).Count==3);
            s.Set("control_confirmation","Clipboard");
            Require(VisualControlVerification.ReadNumber(s,()=>ControlReadback.Read("size",3,clipboard,.15).Number) is null&&clipboard.Copies==3);
        });
        test("Visual painting remains blocked without a strong current imprint",()=>{
            var s=Captured();CalibrationReliability.ApplyPreset(s,true);Require(CalibrationReliability.PaintingProblem(s) is not null);
            BrushFootprints.Save(s,[Profile(s)]);Require(CalibrationReliability.PaintingProblem(s) is null);
            s.Set("precision_brush_size","10");Require(CalibrationReliability.PaintingProblem(s) is not null);
        });
        test("Verified Size3 enables measured mixed painting without inventing Size1 proof",()=>{
            var s=Captured();BrushFootprints.Save(s,[Profile(s)]);Require(AdaptiveBrush.CalibrationCurrent(s)&&!SpeedCalibration.BrushReady(s,1));
            s.Set("adaptive_brush",true);AdaptiveBrush.Prepare(s);AdaptiveBrush.Validate(s);Require(PaintTimingPlan.DefaultSize(s)==3);
        });
        test("A failed fresh visual check cannot reuse an older valid brush imprint",()=>{
            var s=Captured();CalibrationReliability.ApplyPreset(s,true);var p=Profile(s);BrushFootprints.Save(s,[p]);
            Require(CalibrationReliability.PaintingProblem(s) is null);CalibrationReliability.BeginBrushCheck(s);
            CalibrationReliability.ConfirmBrushCheck(s,[]);Require(CalibrationReliability.PaintingProblem(s) is not null);
            var reopened=s.Clone();CalibrationReliability.ConfirmBrushCheck(reopened,[Profile(reopened,10)]);Require(CalibrationReliability.PaintingProblem(reopened) is not null);
            CalibrationReliability.ConfirmBrushCheck(reopened,[p]);Require(CalibrationReliability.PaintingProblem(reopened) is null);
        });
        test("Extra stable capture attempts find the same two-frame criterion",()=>{
            PixelImage Read(bool slow)
            {
                int n=0;var s=Settings.Defaults();if(slow)CalibrationReliability.ApplyPreset(s,true);
                return StableCapture.Read(()=>Frame((byte)(++n<8?n%2==0?0:255:180)),_=>{},CalibrationReliability.StableAttempts(s),CalibrationReliability.StableInterval(s));
            }
            bool failed=false;try{Read(false);}catch(InvalidOperationException){failed=true;}Require(failed);Require(Read(true).Color(0).R==180);
        });
        test("Stable capture never accepts a scene that keeps changing",()=>{
            int n=0;bool failed=false;try{StableCapture.Read(()=>Frame((byte)(n++%2==0?0:255)),_=>{},10,.16);}catch(InvalidOperationException){failed=true;}Require(failed&&n==11);
        });
        test("Drawing setup skips Spatial and Speed while retaining fail-closed sequence",()=>{
            var calls=new List<SetupStage>();SetupSequence.Run((s,_)=>{calls.Add(s);return Task.CompletedTask;},(_,_,_)=>{},default,SetupSequence.DrawingStages).GetAwaiter().GetResult();
            Require(calls.SequenceEqual(new[]{SetupStage.Capture,SetupStage.Colors,SetupStage.Controls,SetupStage.Brush}));
            var s=Captured();Require(SetupSequence.CaptureReady(s)&&!SetupSequence.CaptureKeys(s).Contains("brush_tool")&&!SetupSequence.CaptureKeys(s).Contains("tool_row"));
            Require(SetupSequence.DrawingSize(Settings.Defaults())==3);
        });
        test("Readback diagnostics retain only markers or valid control values",()=>{
            Require(ReadbackDiagnostics.SafeText(ControlNumber.Marker,"interval")==ControlNumber.Marker);
            Require(ReadbackDiagnostics.SafeText("0.01","interval")=="0.01");
            Require(!ReadbackDiagnostics.SafeText("personal clipboard note","size")!.Contains("personal"));
            Require(ReadbackDiagnostics.SafeText("#ff3333")=="FF3333");
        });
    }
}
