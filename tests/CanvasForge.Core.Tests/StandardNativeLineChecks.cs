using CanvasForge.Core;

internal static class StandardNativeLineChecks
{
    private static void Need(bool value,string message){if(!value)throw new Exception(message);}
    public static void Run(Action<string,Action> test)
    {
        test("Native line prototype is explicit opt-in and does not enable Adaptive or change target/coverage",()=>{
            var s=Settings.Defaults();s.SetPalette([new(new(0,0,0),new(220,220),"main")]);
            var c=s.Calibration;c.SetRect("canvas",new(10,20,90,100));s.SetCalibration(c);
            var image=new PixelImage(8,8);for(int i=0;i<64;i++)image.Set(i,new(0,0,0));
            var before=Planner.Build(image,s);var timing=MotionTransportTiming.Context(s);
            Need(!StandardLineMotion.Applies(s,new(10,20,40,20)),"Prototype silently enabled");
            s.Set("standard_native_line",true);var effective=DrawingWorkflow.Effective(s);var after=Planner.Build(image,effective);
            Need(StandardLineMotion.Applies(effective,new(10,20,40,20)),"Opt-in lost during effective settings");
            Need(before.TargetIdentity==after.TargetIdentity&&before.Preview.Rgba.SequenceEqual(after.Preview.Rgba),"Prototype changed target");
            Need(before.Identity!=after.Identity&&timing!=MotionTransportTiming.Context(effective),"Old checkpoint/rates reused for new emitter");
            Need(!StandardLineMotion.Applies(ReleaseProfile.Experimental(s),new(10,20,40,20)),"Adaptive route changed");
            Need(!StandardLineMotion.Applies(effective,new(10,20,10,20)),"Point incorrectly uses line tool");
        });
        test("Native line prototype sends guarded endpoints with staged Shift mouse commit and exact ETA",()=>{
            var s=Settings.Defaults();s.Set("standard_native_line",true);
            foreach(var speed in SpeedProfile.All)
            foreach(var line in new[]{new ScreenLine(10,20,11,20),new(10,20,10,490),new(10,20,480,20)})
            foreach(bool reverse in new[]{false,true})
            {
                var input=new Input();StandardStrokeMotion.Draw(line,s,speed,reverse,input);
                var a=new ScreenPoint(reverse?line.X2:line.X1,reverse?line.Y2:line.Y1);
                var b=new ScreenPoint(reverse?line.X1:line.X2,reverse?line.Y1:line.Y2);
                Need(input.Points.SequenceEqual(new[]{a,b}),"Native line extrapolated or retained freehand packets");
                Need(input.Events.SequenceEqual(new[]{"move","wait","shift-down","wait","down","wait","move","wait","up","wait","shift-up","wait"}),"Line-tool transaction ordering changed");
                Need(!input.Held&&!input.ShiftHeld&&input.Ups==1,"Input retained after native line");
                Need(Math.Abs(input.Seconds-MotionTransportTiming.Standard(s,speed,line))<1e-12,"Preview does not use actual native-line waits");
                Need(input.UpAt-input.DownAt>=2*StrokeTiming.Frame(s)-1e-12,"Native endpoint could commit before a frame");
            }
        });
        test("Native line prototype rejects Experimental input and sub20ms timing before input",()=>{
            foreach(var engine in new[]{"Experimental 1 ms","Unknown","Stable"})
            {
                var s=Settings.Defaults();s.Set("standard_native_line",true);s.Set("input_engine",engine);s.Set("input_frame_delay_ms",16);
                var line=new ScreenLine(10,20,10,490);var input=new Input();
                Need(!StandardLineMotion.Applies(s,line)&&StandardStrokeMotion.Emitter(s)==StandardStrokeMotion.DenseRevision,"Unsafe input context enabled native line");
                bool rejected=false;try{StandardLineMotion.Draw(line,s,false,input);}catch(InvalidOperationException){rejected=true;}
                Need(rejected&&input.Events.Count==0,"Invalid context reached input");
            }
        });
        test("Native line prototype releases keys and mouse on every interruptible transaction phase",()=>{
            var s=Settings.Defaults();s.Set("standard_native_line",true);
            for(int fail=1;fail<=12;fail++)
            {
                var input=new Input{FailAt=fail};bool cancelled=false;
                try{StandardStrokeMotion.Draw(new(10,20,10,490),s,SpeedProfile.Get("Rapid"),false,input);}
                catch(OperationCanceledException){cancelled=true;}
                Need(cancelled&&!input.Held&&!input.ShiftHeld,$"Phase {fail} retained input");
            }
        });
        test("Native endpoint line validates its whole interior against controls and occlusions",()=>{
            var g=new InputCommandGuard(new(10,10,100,100),new(0,0,200,200),[new(45,45,55,55)]);
            Need(g.Allows(new ScreenPoint(20,50))&&g.Allows(new ScreenPoint(80,50)),"Endpoints fixture invalid");
            Need(!g.Allows(new ScreenLine(20,50,80,50))&&!g.Allows(new ScreenLine(80,50,20,50)),"Endpoints bypassed interior no-go");
            Need(g.Allows(new ScreenLine(20,30,80,30))&&!g.Allows(new ScreenLine(20,30,80,31)),"Cardinal route policy changed");
        });
    }
    private sealed class Input:ICalibratedStrokeInput
    {
        public double Seconds{get;private set;}
        public double DownAt,UpAt;
        public bool Held,ShiftHeld;
        public int Ups,FailAt,calls;
        public readonly List<string> Events=[];
        public readonly List<ScreenPoint> Points=[];
        private void Step(string label){Events.Add(label);if(++calls==FailAt)throw new OperationCanceledException();}
        public void Move(IReadOnlyList<ScreenPoint> points){Step("move");Points.AddRange(points);}
        public void Wait(double seconds){Step("wait");Seconds+=seconds;}
        public void Button(bool up){if(up){Held=false;Ups++;UpAt=Seconds;}else{Held=true;DownAt=Seconds;}Step(up?"up":"down");}
        public void Shift(bool up){ShiftHeld=!up;Step(up?"shift-up":"shift-down");}
    }
}
