using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void CheckStrokeCursorDisplacement()
    {
        foreach(var speed in SpeedProfile.All)
        foreach(bool vertical in new[]{false,true})foreach(bool reverse in new[]{false,true})
        {
            var s=Settings.Defaults();var line=vertical?new ScreenLine(1028,150,1028,1192):new(465,600,1508,600);
            var good=new TrackedInput();StandardStrokeMotion.Draw(line,s,speed,reverse,good);
            Assert(!good.Held&&!good.Guard.Active&&good.Downs==1,"Clean transaction leaked tracking");
            // Each boundary and wait gets an independent negative run. The
            // recorded +79/-348 displacement must not be dragged back by us.
            foreach(int slot in Enumerable.Range(1,good.Checks))
            {
                var input=new TrackedInput{DisplaceAt=slot};bool denied=false;
                try{StandardStrokeMotion.Draw(line,s,speed,reverse,input);}
                catch(StrokeCursorMismatchException error)
                {denied=true;Assert(error.Actual.X-error.Expected.X==79&&error.Actual.Y-error.Expected.Y==-348,"Lost recorded displacement");}
                Assert(denied&&!input.Held&&!input.Guard.Active,"Displacement retained paint/tracking");
                Assert(input.MoveCalls==input.MovesAtFailure,"Issued another packet after displacement");
            }
        }
        Console.WriteLine("PASS Standard-cursor-displacement-at-every-packet-and-wait-HV-reverse-all-speeds");
    }
    private static void CheckStrokeCursorReadFailure()
    {
        var s=Settings.Defaults();var speed=SpeedProfile.Get("Max Speed");var line=new ScreenLine(0,0,110,0);
        var good=new TrackedInput();StandardStrokeMotion.Draw(line,s,speed,false,good);
        for(int i=1;i<=good.Checks;i++)
        {
            var input=new TrackedInput{ReadFailsAt=i};bool failed=false;
            try{StandardStrokeMotion.Draw(line,s,speed,false,input);}
            catch(CursorReadException error){failed=error.Win32Error==5;}
            Assert(failed&&!input.Held&&!input.Guard.Active&&input.Releases>0,"API failure converted to a coordinate or retained input");
            Assert(input.MoveCalls==input.MovesAtFailure,"Emitted after cursor API failure");
        }
        // Legacy evidence is frozen: no hidden guard measurements or costs.
        var legacy=new TrackedInput{ReadFailsAt=1};StandardStrokeMotion.DrawLegacy(line,s,speed,false,legacy);
        Assert(legacy.Checks==0&&!legacy.Guard.Active,"Frozen protocol acquired new tracking");
        int reads=0,released=0;var guard=new StrokeCursorGuard(()=>{reads++;return new(0,0);},()=>released++);
        guard.Check("idle");Assert(reads==0,"Idle guard reads the global cursor");
        guard.Begin(new(0,0));guard.Check("origin");guard.End();guard.Check("idle");
        Assert(reads==1&&released==0,"Valid origin treated as API failure");
        // A transport insertion can be observed before its last cursor event.
        // Exact forward packet progress is valid; an old point after progress,
        // off-path input or an incomplete packet at the next emission is not.
        ScreenPoint current=new(10,20);var asyncGuard=new StrokeCursorGuard(()=>current,()=>released++);
        asyncGuard.Begin(current);asyncGuard.Emitted([new(10,21),new(10,22),new(10,23)]);
        asyncGuard.Check("queued");current=new(10,21);asyncGuard.Check("progress");
        current=new(10,22);asyncGuard.Check("progress");current=new(10,23);asyncGuard.Check("before_move",true);
        asyncGuard.Emitted([new(10,24)]);current=new(10,24);asyncGuard.Check("ack");asyncGuard.End();
        Assert(released==0,"Valid asynchronous progress was mistaken for displacement");
        foreach(var invalid in new[]{new ScreenPoint(10,20),new(11,21),new(10,22)})
        {
            current=new(10,20);asyncGuard.Begin(current);asyncGuard.Emitted([new(10,21),new(10,22),new(10,23)]);
            current=new(10,21);asyncGuard.Check("progress");current=invalid;bool rejected=false;
            try{asyncGuard.Check("before_move",requireEndpoint:invalid==new ScreenPoint(10,22));}
            catch(StrokeCursorMismatchException){rejected=true;}
            Assert(rejected&&!asyncGuard.Active,"Reverse/off-path/unacknowledged packet accepted");
        }
        Console.WriteLine("PASS Standard-cursor-API-failure-release-legacy-isolation-origin-and-idle");
    }
    private sealed class TrackedInput:ITrackedStrokeInput
    {
        internal readonly StrokeCursorGuard Guard;
        internal bool Held;internal int Downs,Releases,Checks,MoveCalls,MovesAtFailure,DisplaceAt,ReadFailsAt;
        ScreenPoint cursor;bool failRead;
        public double Seconds{get;private set;}
        internal TrackedInput()=>Guard=new(()=>failRead?throw new CursorReadException(5):cursor,()=>{Held=false;Releases++;});
        void Check(string phase)
        {
            if(!Guard.Active)return;
            Checks++;
            if(Checks==DisplaceAt){cursor=new(cursor.X+79,cursor.Y-348);MovesAtFailure=MoveCalls;}
            if(Checks==ReadFailsAt){failRead=true;MovesAtFailure=MoveCalls;}
            Guard.Check(phase,requireEndpoint:phase=="before_move");
        }
        public void Move(IReadOnlyList<ScreenPoint> points)
        {
            Check("safety_check");Check("after_editor_check");Check("before_move");
            MoveCalls++;cursor=points[^1];Guard.Emitted(points);
        }
        public void Wait(double seconds){Check("wait-begin");Seconds+=seconds;Check("wait-end");}
        public void Button(bool up){if(up){Held=false;return;}Check("before-down");Held=true;Downs++;}
        public void Shift(bool up)=>throw new Exception("Unexpected Shift input");
        public void ConfirmPosition(ScreenPoint expected)=>Assert(cursor==expected,"Endpoint mismatch in clean fixture");
        public void BeginCursorTracking(ScreenPoint expected)=>Guard.Begin(expected);
        public void EndCursorTracking()=>Guard.End();
    }
}
