using CanvasForge.Core;

internal static class StandardMotionChecks
{
    static void Need(bool v,string why){if(!v)throw new Exception(why);}
    public static void Run(Action<string,Action> test)
    {
        test("Standard emits every path pixel exactly once for every preset, length, axis and direction",()=>{
            var s=Settings.Defaults();
            foreach(var speed in SpeedProfile.All)
            foreach(int length in new[]{0,1,2,3,4,15,16,17,47,48,49,127,400,1043})
            foreach(bool vertical in new[]{false,true})foreach(bool reverse in new[]{false,true})
            {
                var line=vertical?new ScreenLine(-20,37,-20,37+length):new(-20,37,-20+length,37);
                var input=new Input();StandardStrokeMotion.Draw(line,s,speed,reverse,input);
                var expected=Enumerable.Range(0,length+1).Select(k=>{
                    int offset=reverse?length-k:k;return new ScreenPoint(-20+(vertical?0:offset),37+(vertical?offset:0));}).ToArray();
                Need(input.Painted.SequenceEqual(expected),$"Missing, repeated or off-axis pixel: {speed.Name}/{length}/{vertical}/{reverse}");
                var legacy=new Input();StandardStrokeMotion.DrawLegacy(line,s,speed,reverse,legacy);
                Need(input.PacketEnds.SequenceEqual(legacy.PacketEnds)&&input.Waits.SequenceEqual(legacy.Waits),"Spatial density changed packet endpoints or requested timing");
                Need(input.Downs==1&&input.Ups==1&&!input.Held,"Unexpected down/up cycle");
                Need(input.TrackingBegins==1&&input.TrackingEnds==1&&!input.Tracking,"Tracking did not span just one Standard transaction");
                Need(Math.Abs(input.Seconds-MotionTransportTiming.Standard(s,speed,line))<1e-10,"ETA and emitter disagree");
                Need(Math.Abs(input.Seconds-StrokeTiming.Estimate(s,speed,length,false,false))<1e-10,"Analytical timing disagrees");
                Need(input.Confirmations.Count==2&&input.Confirmations.All(p=>!p.Held),"Endpoint capture performed with mouse held");
            }
        });
        test("Dense Standard closes actual Turbo and Max input gaps without changing Experimental",()=>{
            foreach(var speed in SpeedProfile.All)
            {
                var line=new ScreenLine(10,20,410,20);var s=Settings.Defaults();
                var dense=new Input();var legacy=new Input();var experimental=new Input();
                StandardStrokeMotion.Draw(line,s,speed,false,dense);
                StandardStrokeMotion.DrawLegacy(line,s,speed,false,legacy);
                StandardStrokeMotion.Draw(line,ReleaseProfile.Experimental(s),speed,false,experimental);
                Need(dense.Painted.Distinct().Count()==401,"Dense contract failed");
                Need(legacy.Painted.Count==(int)Math.Ceiling(400d/speed.Pitch)+1,"Legacy protocol changed");
                Need(legacy.Events.SequenceEqual(experimental.Events),"Experimental fallback changed");
                if(speed.Pitch>1)Need(dense.Painted.Count>legacy.Painted.Count,"Regression fixture no longer exposes old gaps");
            }
        });
        test("Standard denies paint on failed start position and releases on every interrupted phase",()=>{
            var s=Settings.Defaults();var line=new ScreenLine(20,30,120,30);var speed=SpeedProfile.Get("Max Speed");
            var start=new Input{FailConfirm=1};bool failed=false;
            try{StandardStrokeMotion.Draw(line,s,speed,false,start);}catch(OperationCanceledException){failed=true;}
            Need(failed&&start.Downs==0&&!start.Held,"Failed start allowed paint");
            var final=new Input{FailConfirm=2};failed=false;
            try{StandardStrokeMotion.Draw(line,s,speed,false,final);}catch(OperationCanceledException){failed=true;}
            Need(failed&&final.Downs==1&&!final.Held&&final.Ups==1,"Failed end retained paint");
            var complete=new Input();StandardStrokeMotion.Draw(line,s,speed,false,complete);
            for(int call=1;call<=complete.Calls;call++)
            {
                var input=new Input{FailAt=call};failed=false;
                try{StandardStrokeMotion.Draw(line,s,speed,false,input);}catch(OperationCanceledException){failed=true;}
                Need(failed&&!input.Held&&!input.Tracking,$"Interrupted phase {call} retained paint or tracking");
            }
        });
        test("Dense Standard emergency release survives pre-down refusal and a down applied before transport failure",()=>{
            var s=Settings.Defaults();var speed=SpeedProfile.Get("Max Speed");var line=new ScreenLine(20,30,20,130);
            var complete=new Input();StandardStrokeMotion.Draw(line,s,speed,false,complete);
            int downCall=complete.Events.FindIndex(e=>e=="down")+1;
            Need(downCall>0,"Fixture did not reach paint-down");
            var refused=new Input{FailConfirm=1};
            var partialDown=new Input{FailAt=downCall};
            var interruptedHold=new Input{FailAt=downCall+1};
            foreach(var input in new[]{refused,partialDown,interruptedHold})
            {
                bool failed=false;
                try{StandardStrokeMotion.Draw(line,s,speed,false,input);}catch(OperationCanceledException){failed=true;}
                Need(failed&&!input.Held&&!input.Tracking,"Failure retained paint or cursor tracking");
                Need(input.Ups==1&&input.Events.TakeLast(2).SequenceEqual(new[]{"up","tracking-end"}),"Emergency release was skipped or tracking ended first");
                Need(input.PacketEnds.Count==1,"A movement packet was emitted after the failed boundary");
            }
            Need(refused.Downs==0&&refused.Painted.Count==0,"Failed start confirmation sent paint-down");
            // Input.Button applies Held=true before throwing. A successful
            // return from Button(false) therefore cannot be the release gate.
            Need(partialDown.Downs==1&&partialDown.Painted.Count==1,"Partial-down fault no longer represents applied input");
            Need(interruptedHold.Downs==1&&interruptedHold.Painted.Count==1,"Failure during initial hold continued painting");
        });
        test("Dense Standard has a separate timing context from legacy Experimental",()=>{
            var s=Settings.Defaults();var old=ReleaseProfile.Experimental(s);
            Need(StandardStrokeMotion.Emitter(s)==StandardStrokeMotion.DenseRevision,"New route not selected");
            Need(StandardStrokeMotion.Emitter(old)==StandardStrokeMotion.Revision,"Legacy revision changed");
            Need(MotionTransportTiming.Context(s)!=MotionTransportTiming.Context(old),"Old rates accepted for changed input");
            s.Set("standard_native_line",true);
            Need(StandardStrokeMotion.Emitter(s)==StandardLineMotion.Revision,"Prototype dispatch changed");
        });
    }
    sealed class Input:ITrackedStrokeInput
    {
        public double Seconds{get;private set;}
        public bool Held;public int Downs,Ups,Calls,FailAt,FailConfirm;
        public bool Tracking;public int TrackingBegins,TrackingEnds;
        ScreenPoint cursor;
        public List<ScreenPoint> Painted=[],PacketEnds=[];
        public List<double> Waits=[];
        public List<(ScreenPoint Point,bool Held)> Confirmations=[];
        public List<string> Events=[];
        void Step(string e){Events.Add(e);if(++Calls==FailAt)throw new OperationCanceledException();}
        public void Move(IReadOnlyList<ScreenPoint> points)
        {Step("move:"+string.Join(";",points));PacketEnds.Add(points[^1]);foreach(var p in points){cursor=p;if(Held)Painted.Add(p);}}
        public void Button(bool up)
        {Held=!up;if(up)Ups++;else{Downs++;Painted.Add(cursor);}Step(up?"up":"down");}
        public void Wait(double seconds){Step("wait:"+seconds.ToString("R"));Seconds+=seconds;Waits.Add(seconds);}
        public void Shift(bool up)=>Step(up?"shift-up":"shift-down");
        public void ConfirmPosition(ScreenPoint point)
        {Confirmations.Add((point,Held));Step("confirm");if(cursor!=point||Confirmations.Count==FailConfirm)throw new OperationCanceledException();}
        public void BeginCursorTracking(ScreenPoint point)
        {Need(!Held&&!Tracking&&cursor==point,"Invalid tracking begin");Tracking=true;TrackingBegins++;Step("tracking-begin");}
        public void EndCursorTracking(){Tracking=false;TrackingEnds++;Step("tracking-end");}
    }
}
