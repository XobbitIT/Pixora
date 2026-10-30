using CanvasForge.Core;

internal static class StandardLineDiagnosticChecks
{
    private static void Need(bool value,string message){if(!value)throw new Exception(message);}
    public static void Run(Action<string,Action> test)
    {
        test("Line diagnostics preserve the selected rectangle and reject unavailable or altered routes",()=>{
            var canvas=new ScreenRect(534,217,1451,1136);
            foreach(bool vertical in new[]{false,true})foreach(var route in Enum.GetValues<StandardLineDiagnosticRoute>())foreach(int ms in new[]{20,40,80})
            {
                var p=StandardLineDiagnosticPlan.Create(canvas,vertical,route,ms);
                Need(p.Canvas==canvas&&p.Lines.Length==2&&p.Lines.All(l=>TransferSchedule.Length(l)==470),"Diagnostic snapped or shortened the selection");
                var guard=new InputCommandGuard(canvas,new(0,0,2560,1440),[]);p.Validate(guard);
                var blocked=new InputCommandGuard(canvas,new(0,0,2560,1440),[new(p.Lines[0].X1-1,p.Lines[0].Y1-1,p.Lines[0].X1+2,p.Lines[0].Y1+2)]);
                bool denied=false;try{p.Validate(blocked);}catch(InvalidOperationException){denied=true;}Need(denied,"Control inside diagnostic route ignored");
                p.Lines[0]=new(0,0,1,0);denied=false;try{p.Validate(guard);}catch(InvalidOperationException){denied=true;}Need(denied,"Mutated plan accepted");
            }
            foreach(var rectangle in new[]{new ScreenRect(0,0,500,500),new(0,0,10,1000),default})
            {
                bool denied=false;try{StandardLineDiagnosticPlan.Create(rectangle,true,StandardLineDiagnosticRoute.Freehand,20);}catch(InvalidOperationException){denied=true;}
                Need(denied,"Small Canvas silently resized the probe");
            }
        });
        test("Diagnostic Shift-after-down is isolated from production and releases on every event failure",()=>{
            var p=StandardLineDiagnosticPlan.Create(new(0,0,917,919),true,StandardLineDiagnosticRoute.ShiftAfterDown,40);
            var settings=Settings.Defaults();var original=settings.Data.ToJsonString();var input=new Input();p.Draw(0,settings,input);
            Need(input.Events.SequenceEqual(new[]{"move","wait","down","wait","shift-down","wait","move","wait","up","wait","shift-up","wait"}),"Alternate key order differs");
            Need(Math.Abs(input.Seconds-.24)<1e-12&&!input.Held&&!input.ShiftHeld,"Alternate timing or release wrong");
            Need(settings.Data.ToJsonString()==original&&!StandardLineMotion.Enabled(settings),"Diagnostic enabled the production route");
            for(int fail=1;fail<=12;fail++)
            {
                input=new(){FailAt=fail};bool stopped=false;try{p.Draw(1,settings,input);}catch(OperationCanceledException){stopped=true;}
                Need(stopped&&!input.Held&&!input.ShiftHeld,"Alternate order retained input at event "+fail);
            }
            foreach(int ms in new[]{0,16,27,100,int.MaxValue})
            {
                input=new();bool denied=false;try{StandardLineMotion.Diagnostic(p.Lines[0],ms,false,true,input);}catch(ArgumentOutOfRangeException){denied=true;}
                Need(denied&&input.Events.Count==0,"Unregistered timing sent input");
            }
        });
        test("Native line confirms both commanded endpoints and fails before paint or commit on mismatch",()=>{
            foreach(bool after in new[]{false,true})foreach(int fail in new[]{1,2,3})
            {
                var input=new VerifiedInput(fail);bool stopped=false;
                try{StandardLineMotion.Diagnostic(new(10,20,10,490),20,true,after,input);}catch(InvalidOperationException){stopped=true;}
                Need(stopped&&input.Expected.SequenceEqual(new[]{new ScreenPoint(10,490),new(10,490),new(10,20)}.Take(fail)),"Endpoint check order changed");
                Need(!input.Inner.Held&&!input.Inner.ShiftHeld&&(fail==3||!input.Inner.Events.Contains("down")),"Failed endpoint retained input or allowed paint-down");
            }
        });
        test("Line diagnostic analysis reports a connector and changes at RGB13 without claiming certification",()=>{
            var p=StandardLineDiagnosticPlan.Create(new(0,0,600,620),true,StandardLineDiagnosticRoute.ShiftBeforeDown,20);
            PixelImage Blank(){var im=new PixelImage(p.Area.Width,p.Area.Height);for(int i=0;i<im.Width*im.Height;i++)im.Set(i,new(220,220,220));return im;}
            var background=Blank();var before=Blank();var first=Blank();var second=Blank();
            int midX=(p.Lines[0].X1+p.Lines[1].X1)/2-p.Area.Left,midY=p.Lines[0].Y1+100-p.Area.Top,index=midY*before.Width+midX;
            second.Set(index,new(208,220,220));var exact=StandardLineDiagnosticAnalysis.Inspect(p,background,before,first,second);
            Need(exact.OutsideReservations==0,"RGB12 was relaxed to >=12");
            second.Set(index,new(207,220,220));var changed=StandardLineDiagnosticAnalysis.Inspect(p,background,before,first,second);
            Need(changed.OutsideReservations==1&&changed.SecondChangesOutsideItsReservation==1&&!changed.PhysicalResultVerified,"Connector hidden or observation called PASS");
            background.Set(index,new(200,220,220));Need(StandardLineDiagnosticAnalysis.Inspect(p,background,before,first,second).NoisePeak==20,"Noise excluded from evidence");
            bool rejected=false;try{StandardLineDiagnosticAnalysis.Inspect(p,new(1,1),before,first,second);}catch(ArgumentException){rejected=true;}Need(rejected,"Wrong capture geometry accepted");
        });
        test("Line diagnostic observes varying width and waviness without turning diagnostic thresholds into acceptance",()=>{
            var p=StandardLineDiagnosticPlan.Create(new(0,0,600,620),true,StandardLineDiagnosticRoute.Freehand,20);
            PixelImage Blank(){var im=new PixelImage(p.Area.Width,p.Area.Height);for(int i=0;i<im.Width*im.Height;i++)im.Set(i,new(220,220,220));return im;}
            var before=Blank();var first=Blank();var second=Blank();
            for(int a=0;a<=470;a++)foreach(int cross in Enumerable.Range(-2,5))
            {
                int y=p.Lines[0].Y1+a-p.Area.Top;
                first.Set(y*first.Width+p.Lines[0].X1+cross-p.Area.Left,new(0,0,0));
                int shift=a==200?2:0;
                second.Set(y*second.Width+p.Lines[1].X1+cross+shift-p.Area.Left,new(0,0,0));
            }
            var observed=StandardLineDiagnosticAnalysis.Inspect(p,before,before,first,second);
            Need(observed.Lines[0].MeasuredSections==471&&observed.Lines[0].MinimumWidth==5&&observed.Lines[0].MaximumWidth==5
                &&observed.Lines[0].MaxOwnBestLineResidual==0,"Straight synthetic line is not measured as straight");
            Need(observed.Lines[1].CenterRange==2&&observed.Lines[1].MaxOwnBestLineResidual>1.9&&!observed.PhysicalResultVerified,"Waviness hidden or synthetic data certified");
        });
    }
    private sealed class VerifiedInput(int fail):IPositionVerifiedStrokeInput
    {
        internal readonly Input Inner=new();internal readonly List<ScreenPoint> Expected=[];
        public double Seconds=>Inner.Seconds;
        public void Move(IReadOnlyList<ScreenPoint> points)=>Inner.Move(points);
        public void Wait(double seconds)=>Inner.Wait(seconds);
        public void Button(bool up)=>Inner.Button(up);
        public void Shift(bool up)=>Inner.Shift(up);
        public void ConfirmPosition(ScreenPoint expected){Expected.Add(expected);if(Expected.Count==fail)throw new InvalidOperationException("Endpoint mismatch");}
    }
    private sealed class Input:ICalibratedStrokeInput
    {
        public double Seconds{get;private set;}internal bool Held,ShiftHeld;internal int FailAt,calls;
        internal readonly List<string> Events=[];
        private void Event(string name){Events.Add(name);if(++calls==FailAt)throw new OperationCanceledException();}
        public void Move(IReadOnlyList<ScreenPoint> points)=>Event("move");
        public void Wait(double seconds){Event("wait");Seconds+=seconds;}
        public void Button(bool up){Held=!up;Event(up?"up":"down");}
        public void Shift(bool up){ShiftHeld=!up;Event(up?"shift-up":"shift-down");}
    }
}
