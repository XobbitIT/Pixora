using CanvasForge.Core;

internal static class StandardEvidenceSeriesChecks
{
    private static void Need(bool value,string reason){if(!value)throw new Exception(reason);}
    public static void Run(Action<string,Action> test)
    {
        test("Evidence series freezes 36 singles two inter-colour cases and four production bridges",()=>{
            var p=StandardEvidenceSeries.Create(new(465,150,1509,1193));p.Validate();
            var single=p.Cases.Where(c=>c.Kind=="single").ToArray();Need(single.Length==36&&p.Cases.Length==42,"Series count changed");
            foreach(bool v in new[]{false,true})foreach(bool r in new[]{false,true})foreach(int length in new[]{16,64,400})
                Need(single.Count(c=>c.Vertical==v&&c.Reverse==r&&c.Length==length)==3,"Missing direction/length/repetition");
            Need(single.GroupBy(c=>c.Block).All(g=>g.Count()==12)&&single.Select(c=>c.Reservation).Distinct().Count()==36,"Blocks or independent positions lost");
            Need(p.Cases.Sum(c=>c.Stages.Length)==44&&p.Cases.Sum(c=>c.Stages.Sum(s=>s.Lines.Length))==216,"Colour/stroke count changed");
            foreach(var c in p.Cases.Where(c=>c.Kind=="colour-overdraw"))
                Need(c.Stages[0].Colour==new Rgb(0,0,0)&&c.Stages[0].Lines.Length==72&&c.Stages[1].Colour==Rgb.White&&c.Stages[1].Lines.Length==16,"Missing black/white boundary sequence");
        });
        test("Evidence production bridges freeze current Max Speed packets and report requested waits only",()=>{
            var p=StandardEvidenceSeries.Create(new(0,0,900,920));var s=Settings.Defaults();s.Set("input_frame_delay_ms",20);s.Set("cycle_delay_ms",0);
            var bridges=p.Cases.Where(c=>c.Kind=="production-packet").ToArray();
            Need(bridges.Length==4&&bridges.Count(c=>c.Vertical)==2&&bridges.All(c=>c.Length==400&&c.Speed==SpeedProfile.Get("Max Speed")),"Production bridge preset changed");
            foreach(var c in bridges)
            {
                var input=new Input();StandardStrokeMotion.Draw(c.Stages[0].Lines[0],s,c.Speed,c.Reverse,input);
                Need(input.MaxPacket==48&&input.Moves==10&&input.Downs==1&&input.Ups==1,"Production shape no longer matches ordinary dense Max Speed");
            }
            Need(Math.Abs(p.RequestedMotionSeconds(s)-40.7496)<1e-8,"Requested wait lower bound incorrectly includes a per-point frame or omits strokes");
            var changed=StandardEvidenceSeries.Create(p.Canvas);changed.Cases[38]=changed.Cases[38] with{Speed=StandardEvidenceSeries.Speed};
            bool failed=false;try{changed.Validate();}catch(InvalidDataException){failed=true;}Need(failed,"Bridge profile was not frozen");
        });
        test("Evidence reservations are disjoint bounded and rebase exactly at positive and negative origins",()=>{
            foreach(var origin in new[]{new ScreenPoint(0,0),new(-1200,80),new(465,150)})
            {
                var canvas=new ScreenRect(origin.X,origin.Y,origin.X+900,origin.Y+920);var p=StandardEvidenceSeries.Create(canvas);
                foreach(var c in p.Cases)
                {
                    var b=c.Reservation;Need(b.Left>=canvas.Left&&b.Top>=canvas.Top&&b.Right<=canvas.Right&&b.Bottom<=canvas.Bottom,"Reservation outside Canvas");
                    foreach(var line in c.Stages.SelectMany(s=>s.Lines))Need(new InputCommandGuard(b,canvas,[]).Allows(line),"Command outside reserved area");
                    foreach(var other in p.Cases.Where(o=>o.Index<c.Index))Need(!(b.Left<other.Reservation.Right&&b.Right>other.Reservation.Left&&b.Top<other.Reservation.Bottom&&b.Bottom>other.Reservation.Top),"Reservations overlap");
                }
            }
            bool failed=false;try{StandardEvidenceSeries.Create(new(0,0,899,920));}catch(InvalidDataException){failed=true;}Need(failed,"Too small Canvas accepted");
        });
        test("Evidence series rejects altered length colour or order and preserves deterministic seed",()=>{
            var p=StandardEvidenceSeries.Create(new(0,0,1000,1000));Need(p.Identity()==StandardEvidenceSeries.Create(p.Canvas,p.Seed).Identity(),"Same seed changed series");
            Need(p.Identity()!=StandardEvidenceSeries.Create(p.Canvas,p.Seed+1).Identity(),"Seed did not vary order");
            foreach(string mutation in new[]{"length","colour","order"})
            {
                var changed=StandardEvidenceSeries.Create(p.Canvas);
                if(mutation=="length")changed.Cases[0]=changed.Cases[0] with{Length=17};
                else if(mutation=="colour")changed.Cases[0].Stages[0]=changed.Cases[0].Stages[0] with{Colour=Rgb.White};
                else (changed.Cases[0],changed.Cases[1])=(changed.Cases[1],changed.Cases[0]);
                bool failed=false;try{changed.Validate();}catch(InvalidDataException){failed=true;}Need(failed,"Altered frozen protocol accepted");
            }
        });
        test("Evidence region uses physical origin RGB12 boundary and refuses outside contamination",()=>{
            var canvas=new ScreenRect(100,200,120,220);var a=new PixelImage(20,20);var b=a.Clone();var region=new ScreenRect(105,205,110,210);
            b.Set(6*20+6,new(12,0,0));Need(EvidenceRegionCheck.Same(a,b,canvas,region),"Tolerance12 rejected");
            b.Set(6*20+6,new(13,0,0));Need(!EvidenceRegionCheck.Same(a,b,canvas,region)&&EvidenceRegionCheck.ChangesContained(a,b,canvas,region),"Inside change was hidden or treated as outside");
            b.Set(10*20+10,new(13,0,0));Need(!EvidenceRegionCheck.ChangesContained(a,b,canvas,region),"Exclusive right/bottom boundary bypassed");
        });
        test("Evidence uses dense current emitter with one actual point per move and unconditional release",()=>{
            var p=StandardEvidenceSeries.Create(new(0,0,1000,1000));var s=Settings.Defaults();s.Set("standard_native_line",false);s.Set("input_frame_delay_ms",20);
            Need(StandardStrokeMotion.Emitter(s)==StandardStrokeMotion.DenseRevision,"Legacy emitter selected");
            foreach(var c in p.Cases.Where(c=>c.Kind=="single"))
            {
                var input=new Input();var line=c.Stages[0].Lines[0];StandardStrokeMotion.Draw(line,s,StandardEvidenceSeries.Speed,c.Reverse,input);
                Need(input.Moves==c.Length+1&&input.MaxPacket==1&&input.Downs==1&&input.Ups==1&&input.Begins==1&&input.Ends==1&&input.Confirms==2&&!input.Held,"Not per-point tracked dense input");
                Need(input.First==(c.Reverse?new ScreenPoint(line.X2,line.Y2):new(line.X1,line.Y1)),"Reverse start changed");
            }
            var failed=new Input{FailHeldMove=true};bool stopped=false;
            try{StandardStrokeMotion.Draw(new(20,20,420,20),s,StandardEvidenceSeries.Speed,false,failed);}catch(OperationCanceledException){stopped=true;}
            Need(stopped&&failed.Ups==1&&!failed.Held&&failed.Ends==1,"Interrupted evidence held mouse down");
        });
    }
    private sealed class Input:ITrackedStrokeInput
    {
        public double Seconds{get;private set;}public int Moves,MaxPacket,Downs,Ups,Begins,Ends,Confirms;public bool Held,FailHeldMove;public ScreenPoint First,Cursor;
        public void Move(IReadOnlyList<ScreenPoint> p){if(Held&&FailHeldMove)throw new OperationCanceledException();if(Moves==0)First=p[0];Moves++;MaxPacket=Math.Max(MaxPacket,p.Count);Cursor=p[^1];}
        public void Button(bool up){Held=!up;if(up)Ups++;else Downs++;}
        public void Wait(double seconds)=>Seconds+=seconds;
        public void Shift(bool up)=>throw new Exception("Unexpected Shift");
        public void ConfirmPosition(ScreenPoint expected){Need(Cursor==expected,"Endpoint mismatch");Confirms++;}
        public void BeginCursorTracking(ScreenPoint expected)=>Begins++;
        public void EndCursorTracking()=>Ends++;
    }
}
