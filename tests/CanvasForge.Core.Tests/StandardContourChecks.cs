using CanvasForge.Core;

internal static class StandardContourChecks
{
    static void Need(bool ok,string why){if(!ok)throw new Exception(why);}
    static StandardGeometryContext Context()=>new(new(0,0,1000,1000),new(0,0),new(1000,1000),96,3,3,SpeedProfile.Get("Max Speed"),.02,.02,.02,"Rust Palette");
    static StandardBandContour Model(bool reverse=false)
    {
        var line=reverse?new ScreenLine(100,420,100,20):new(100,20,100,420);
        var rows=Enumerable.Range(100,241).Select(y=>new StandardEdgeSection(y,97.5+Math.Sin(y*.02),102.5+Math.Cos(y*.02))).ToArray();
        return StandardBandContour.Create(Context(),line,"native-source-a",rows);
    }
    public static void Run(Action<string,Action> test)
    {
        test("Two-boundary band compensates variable width without changing the target span",()=>{
            var m=Model();Need(m.TargetLow==93&&m.TargetHigh==107,"Target width inferred or widened");
            Need(m.PredictedMaximumBoundaryError<.51&&m.PredictedMaximumBoundaryError<m.OriginalMaximumBoundaryError,"Separate boundaries did not improve surrogate");
            Need(m.Control.Count==10&&m.Candidate.Count==10&&!m.ProductionEligible,"Wrong comparison count or production enabled");
            for(int i=0;i<10;i++)
            {
                var a=m.Control[i].Points;var b=m.Candidate[i].Points;
                Need(a.Count==401&&b.Count==401,"Commands missing");StandardBandContour.Validate(b,Context().Canvas);
                Need(a.Take(17).SequenceEqual(b.Take(17))&&a.TakeLast(17).SequenceEqual(b.TakeLast(17)),"Unmeasured cap correction hidden in the route");
            }
            Need(m.Candidate.Any(t=>t.Points.Select(p=>p.X).Distinct().Count()>1),"Candidate remains straight control");
        });
        test("Boundary minimax agrees with exhaustive independent enumeration",()=>{
            var random=new Random(20261021);
            for(int trial=0;trial<20;trial++)
            {
                var rows=Enumerable.Range(50,4).Select(y=>new StandardEdgeSection(y,97.5+random.NextDouble()-.5,102.5+random.NextDouble()-.5)).ToArray();
                var m=StandardBandContour.Create(Context(),new(100,20,100,120),"a",rows,7,2,1);
                double Best(bool left)
                {
                    double best=double.PositiveInfinity;
                    void Search(int i,int previous,double cost)
                    {
                        if(i==rows.Length){best=Math.Min(best,cost);return;}
                        for(int s=-1;s<=1;s++)if(i==0||Math.Abs(s-previous)<=1)
                            Search(i+1,s,Math.Max(cost,Math.Abs((left?rows[i].Low:rows[i].High)+s-(left?96.5:103.5))));
                    }
                    Search(0,0,0);return best;
                }
                Need(m.PredictedMaximumBoundaryError==Math.Max(Best(true),Best(false)),"Boundary solver not minimax optimal");
            }
        });
        test("Contour masks cannot shrink an oversized source or hide gaps and invalid coordinates",()=>{
            void Refuse(Action action){bool bad=false;try{action();}catch(InvalidDataException){bad=true;}Need(bad,"Unsafe contour accepted");}
            Refuse(()=>StandardBandContour.Create(Context(),new(100,20,100,420),"a",[new(100,90,110),new(101,90,110)]));
            Refuse(()=>StandardBandContour.Create(Context(),new(100,20,100,420),"a",[new(100,98,103),new(102,98,103)]));
            Refuse(()=>StandardBandContour.Validate([new(20,20),new(22,21)],Context().Canvas));
            Refuse(()=>StandardBandContour.Validate([new(20,20),new(20,20)],Context().Canvas));
            Refuse(()=>StandardBandContour.Validate([new(0,0),new(-1,0)],Context().Canvas));
        });
        test("Contour context identity direction and input ownership remain exact",()=>{
            var a=Model();var b=Model(true);Need(a.Id!=b.Id,"Reverse shares route identity");
            for(int i=0;i<a.Candidate.Count;i++)Need(a.Candidate[i].Points.Reverse().SequenceEqual(b.Candidate[i].Points),"Reverse changes spatial path");
            var c=StandardBandContour.Create(Context() with{Dpi=144},a.Command,"native-source-a",a.MeasuredInterior);
            Need(c.Id!=a.Id,"DPI omitted from identity");
        });
        test("Straight contour control has exactly the ordinary dense events and waits for every speed",()=>{
            var settings=Settings.Defaults();
            foreach(var speed in SpeedProfile.All)foreach(bool reverse in new[]{false,true})
            {
                var line=new ScreenLine(100,20,100,420);var points=Enumerable.Range(20,401).Select(y=>new ScreenPoint(100,y));if(reverse)points=points.Reverse();
                var a=new Input();var b=new Input();StandardStrokeMotion.Draw(line,settings,speed,reverse,a);
                StandardContourMotion.Draw(points.ToArray(),Context().Canvas,settings,speed,b);
                Need(a.Events.SequenceEqual(b.Events)&&a.Seconds==b.Seconds,"Contour control changes nominal exposure or packets");
            }
        });
        test("Contour emitter releases paint and tracking at every interrupted phase",()=>{
            var s=Settings.Defaults();var speed=SpeedProfile.Get("Max Speed");var path=Model().Candidate[0].Points;var complete=new Input();
            StandardContourMotion.Draw(path,Context().Canvas,s,speed,complete);
            for(int call=1;call<=complete.Calls;call++)
            {
                var input=new Input{FailAt=call};bool interrupted=false;
                try{StandardContourMotion.Draw(path,Context().Canvas,s,speed,input);}catch(OperationCanceledException){interrupted=true;}
                Need(interrupted&&!input.Held&&!input.Tracking,"Interrupted contour retained input");
            }
            var invalid=new Input();bool failed=false;
            try{StandardContourMotion.Draw([new(1,1),new(3,1)],Context().Canvas,s,speed,invalid);}catch(InvalidDataException){failed=true;}
            Need(failed&&invalid.Calls==0,"Invalid contour emitted input before validation");
        });
        test("Full native footprint includes real caps and distinguishes empty observations from missing data",()=>{
            var c=Context();var before=new PixelImage(1000,1000);for(int i=0;i<1_000_000;i++)before.Set(i,new(220,220,220));
            var after=before.Clone();for(int y=98;y<=124;y++)for(int x=198;x<=202;x++)after.Set(y*1000+x,new(0,0,0));
            var m=StandardFullFootprint.Measure("a",c,new(200,100,200,120),new(190,90,211,131),before,after,after,after);
            Need(m.FullReservationObserved&&m.Sections.Count==41&&m.Sections.Single(s=>s.Along==98).Edge is not null,"Real cap cropped at command endpoint");
            Need(m.Sections.Single(s=>s.Along==90).Status=="NO_DELTA12_OBSERVED"&&m.CommandSectionsWithoutUniqueEdge==0,"Empty row called missing capture");
            Need(m.Possible.Any(r=>r.Y<0)&&m.Possible.Any(r=>r.Y>20)&&!m.StableContrastIsCertifiedSolid,"Caps clipped or contrast certified as RGB");
        });
    }
    sealed class Input:ITrackedStrokeInput
    {
        public double Seconds{get;private set;}public bool Held,Tracking;public int Calls,FailAt;ScreenPoint cursor;
        public List<string> Events=[];
        void Step(string action){Events.Add(action);if(++Calls==FailAt)throw new OperationCanceledException();}
        public void Move(IReadOnlyList<ScreenPoint> p){Step("move:"+string.Join(";",p));cursor=p[^1];}
        public void Button(bool up){Held=!up;Step(up?"up":"down");}
        public void Wait(double seconds){Step("wait:"+seconds.ToString("R"));Seconds+=seconds;}
        public void Shift(bool up)=>throw new Exception("Shift not allowed");
        public void ConfirmPosition(ScreenPoint p){Need(p==cursor,"Wrong cursor");Step("confirm");}
        public void BeginCursorTracking(ScreenPoint p){Tracking=true;Step("begin");}
        public void EndCursorTracking(){Tracking=false;Step("end");}
    }
}
