using CanvasForge.Core;

internal static class LiveLifecycleRegressionChecks
{
    private static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    public static void Run(Action<string,Action> test)
    {
        test("Numeric capture parks outside all adjacent controls at multiple DPI",()=>{
            var c=new Calibration(new());c.SetRect("size_track",new(2200,350,2400,370));
            c.SetRect("interval_track",new(2200,420,2400,440));c.SetRect("opacity_track",new(2200,490,2400,510));
            var window=new ScreenRect(0,0,2560,1440);
            foreach(int dpi in new[]{96,144,192})
            {
                var cursor=new ScreenPoint(2488,491);
                var parked=ControlCapture.ParkingPoint(c,window,cursor,dpi);
                Require(parked is not null,"No clear parking position");
                foreach(string kind in new[]{"size","interval","opacity"})
                {
                    var r=ControlCapture.Bounds(c.Rect(kind+"_track"),window);var p=parked!.Value;
                    int margin=CaptureCursor.Clearance(0,dpi);
                    Require(p.X<=r.Left-margin||p.X>=r.Right+margin||p.Y<=r.Top-margin||p.Y>=r.Bottom+margin,
                        "Parking for Opacity contaminated another numeric field");
                }
                Require(ControlCapture.ParkingPoint(c,window,parked!.Value,dpi)==parked,"Safe parking point moved again");
            }
        });
        test("Small palette stamp verifies colour at readable Size then restores target",()=>{
            foreach(double requested in new[]{1d,2,3,10,20})
            {
                double current=requested;var changes=new List<double>();
                bool passed=CalibrationPaintSource.Verify(requested,true,size=>{current=size;changes.Add(size);},
                    ()=>{Require(current>=3,"Colour read from tiny antialiased preview");return true;});
                Require(passed&&current==requested,"Colour proof changed the measured stamp Size");
                Require(changes.SequenceEqual(requested<3?new[]{3d,requested}:[]),"Unneeded numeric edits");
            }
        });
        test("Locator capture parks outside tool shapes and relocated-control search",()=>{
            var c=new Calibration(new());c.SetRect("size_track",new(1750,400,2000,425));
            c.SetRect("interval_track",new(1750,450,2000,475));c.SetRect("opacity_track",new(1750,500,2000,525));
            c.SetRect("brush_shapes",new(1600,225,2030,275));c.SetPoint("brush_tool",new(1750,130));
            var window=new ScreenRect(0,0,2048,1152);var region=BrushControlsLocator.CaptureRegion(window,c);
            foreach(var cursor in new[]{new ScreenPoint(1750,130),new ScreenPoint(1780,250),new ScreenPoint(1980,700)})
            {
                var parked=CaptureCursor.ParkingPoint(region,window,cursor,CaptureCursor.Clearance(0));
                Require(parked is not null&&parked!=cursor,"Locator kept cursor inside a scanned row");
                Require(parked!.Value.X<=region.Left-96||parked.Value.X>=region.Right+96
                    ||parked.Value.Y<=region.Top-96||parked.Value.Y>=region.Bottom+96,"Locator parking lacked clearance");
            }
        });
        test("Rejected small preview retains readable retry state and never becomes proof",()=>{
            double size=2;bool passed=CalibrationPaintSource.Verify(2,true,x=>size=x,()=>false);
            Require(!passed&&size==3,"Failed proof restored unreadable Size");
            bool failed=false;try{CalibrationPaintSource.Verify(2,true,x=>size=x,()=>throw new InvalidOperationException("source mismatch"));}
            catch(InvalidOperationException){failed=true;}Require(failed&&size==3,"Source failure was hidden");
        });
        test("HEX source proof does not modify the requested Size",()=>{
            int changes=0;Require(CalibrationPaintSource.Verify(1,false,_=>changes++,()=>true)&&changes==0,"HEX proof unnecessarily changed Size");
        });
        test("Calibration colour selection restores small Size only after successful source proof",()=>{
            double size=2;var changes=new List<double>();
            var colour=CalibrationPaintSource.Read(2,true,x=>{size=x;changes.Add(x);},()=>{
                Require(size==3,"Selection attempted with tiny preview");return new Rgb(0,0,0);});
            Require(colour==new Rgb(0,0,0)&&size==2&&changes.SequenceEqual(new[]{3d,2}),"Selection failed to restore measured Size");
            bool refused=false;try{CalibrationPaintSource.Read<Rgb>(2,true,x=>size=x,()=>throw new InvalidOperationException());}
            catch(InvalidOperationException){refused=true;}Require(refused&&size==3,"Failed selection locked subsequent retry to Size 2");
        });
        test("Ordinary H V reverse and point strokes emit endpoints through input events",()=>{
            var s=Settings.Defaults();s.Set("line_mode",false);var speed=SpeedProfile.Get("Rapid");
            foreach(var line in new[]{new ScreenLine(10,20,31,20),new ScreenLine(10,20,10,41),new ScreenLine(10,20,10,20)})
            foreach(bool reverse in new[]{false,true})
            {
                var input=new Input();StandardStrokeMotion.Draw(line,s,speed,reverse,input);
                var first=new ScreenPoint(reverse?line.X2:line.X1,reverse?line.Y2:line.Y1);
                var last=new ScreenPoint(reverse?line.X1:line.X2,reverse?line.Y1:line.Y2);
                Require(input.Events[0]=="move"&&input.Painted.Contains(first)&&input.Painted.Contains(last),"Endpoint did not reach event transport");
                Require(!input.Held&&!input.ShiftHeld&&input.Ups==1,"Stroke left input held");
                Require(input.Seconds-input.DownAt>=.04,"Ordinary minimum hold was shortened");
                // Independent asymmetric stamp: paint is two pixels below the
                // command, never implicitly recentered onto it.
                var painted=input.Painted.Select(p=>new ScreenPoint(p.X,p.Y+2)).ToHashSet();
                Require(painted.Contains(new(first.X,first.Y+2))&&painted.Contains(new(last.X,last.Y+2)),"Command-relative footprint lost at endpoint");
            }
        });
        test("Ordinary and Shift interruptions always release keys and mouse",()=>{
            foreach(bool shift in new[]{false,true})
            {
                var s=Settings.Defaults();s.Set("line_mode",shift);s.Set("coverage_mode","Fast");
                var input=new Input{FailHeldMove=true};bool cancelled=false;
                try{StandardStrokeMotion.Draw(new(10,20,80,20),s,SpeedProfile.Get("Rapid"),false,input);}
                catch(OperationCanceledException){cancelled=true;}
                Require(cancelled&&!input.Held&&!input.ShiftHeld&&input.Ups==1,"Interruption retained input");
            }
        });
    }
    private sealed class Input:ICalibratedStrokeInput
    {
        public double Seconds{get;private set;}
        public double DownAt{get;private set;}
        public bool Held,ShiftHeld,FailHeldMove;
        public int Ups;
        private ScreenPoint cursor;
        public readonly List<string> Events=[];
        public readonly HashSet<ScreenPoint> Painted=[];
        public void Move(IReadOnlyList<ScreenPoint> points)
        {
            if(Held&&FailHeldMove)throw new OperationCanceledException();
            Events.Add("move");foreach(var p in points){cursor=p;if(Held)Painted.Add(p);}
        }
        public void Button(bool up){Events.Add(up?"up":"down");Held=!up;if(up)Ups++;else{DownAt=Seconds;Painted.Add(cursor);}}
        public void Shift(bool up){ShiftHeld=!up;}
        public void Wait(double seconds){Seconds+=seconds;}
    }
}
