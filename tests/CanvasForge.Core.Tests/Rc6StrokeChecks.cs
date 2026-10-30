using CanvasForge.Core;

internal static class Rc6StrokeChecks
{
    private static void Need(bool ok,string why){if(!ok)throw new Exception(why);}
    private static (StrokeObservation Observation,PixelImage Before,PixelImage First,PixelImage Repeat,ScreenLine Line) Observe(BrushFootprint dot,bool vertical,int x=60,int y=60)
    {
        var before=new PixelImage(180,180);for(int i=0;i<180*180;i++)before.Set(i,new(200,200,200));var first=before.Clone();
        // Independently specified full moving paint: it deliberately differs
        // from dot-sweep geometry, with broad interior and endpoint caps.
        for(int across=-12;across<=12;across++)for(int along=-2;along<=34;along++)
        {byte color=(byte)(Math.Abs(across)<=10&&along>=0&&along<=32?20:80);int px=x+(vertical?across:along),py=y+(vertical?along:across);first.Set(py*180+px,new(color,color,color));}
        var repeat=first.Clone();var line=new ScreenLine(x,y,x+(vertical?0:32),y+(vertical?32:0));
        return (StrokeProfiles.Measure(dot,line,before,before,first,repeat,new(0,0,0),true),before,first,repeat,line);
    }
    public static void Run(Action<string,Action> test)
    {
        test("RC6 production policy cannot be switched back to legacy dot-sweep wide proofs",()=>{
            var legacy=MeasuredColorChecks.Config();legacy.Set("require_stroke_profiles",false);
            var actual=DrawingWorkflow.Effective(legacy);Need(actual.Bool("require_stroke_profiles"),"User flag disabled production proof");
            var result=MeasuredColorPlan.TryBuild(MeasuredColorChecks.Plan(),actual)!;
            Need(result.Groups.Values.SelectMany(g=>g).All(op=>op.Size<=3),"Legacy wide proof reached actual production plan");
            SettingsStore.Migrate(legacy.Data);Need(!legacy.Data.ContainsKey("brush_motion_proofs")&&legacy.Data.ContainsKey("legacy_brush_motion_proofs"),"Legacy proof not archived on reload/save");
        });
        test("RC6 full stroke fits moving paint independent of dot and rejects wrong direction length speed and heldout spill",()=>{
            var s=MeasuredColorChecks.Config();var dot=BrushFootprints.Find(s,20)!;
            var a=Observe(dot,false);var b=Observe(dot,false,65,70);var c=Observe(dot,false,70,80);
            var p=StrokeProfiles.Fit(s,dot,false,[a.Observation,b.Observation],c.Observation,1234);StrokeProfiles.Save(s,p);
            Need(StrokeProfiles.Valid(p)&&StrokeProfiles.Read(s).Single().Id==p.Id,"Moving evidence not active");
            Need(StrokeProfiles.Matches(s,p.Id,new(100,100,132,100),out _),"Correct command rejected");
            foreach(var line in new[]{new ScreenLine(100,100,131,100),new(100,100,100,132),new(132,100,100,100)})
                Need(!StrokeProfiles.Matches(s,p.Id,line,out _),"Wrong direction/length/reversal accepted");
            var slower=s.Clone();slower.Set("input_frame_delay_ms",30);Need(StrokeProfiles.Read(slower).Length==0,"Changed timing accepted");
            var spill=c.Observation with{Possible=BrushSweep.Merge(c.Observation.Possible.Append(new BrushSpan(14,5,6)))};
            bool refused=false;try{StrokeProfiles.Fit(s,dot,false,[a.Observation,b.Observation],spill,1234);}catch(InvalidDataException){refused=true;}Need(refused,"Heldout pixels expanded training envelope");
            refused=false;try{StrokeProfiles.Fit(s,dot,false,[a.Observation,b.Observation],Observe(dot,true).Observation,1234);}catch(InvalidDataException){refused=true;}Need(refused,"Wrong-axis heldout accepted");
        });
        test("RC6 source failure and Size10 repeat delta18 stay rejected at unchanged 80 12 thresholds",()=>{
            var s=MeasuredColorChecks.Config();var dot=BrushFootprints.Find(s,10)!;var a=Observe(dot,false);
            bool refused=false;try{StrokeProfiles.Measure(dot,a.Line,a.Before,a.Before,a.First,a.Repeat,new(0,0,0),false);}catch(InvalidDataException){refused=true;}Need(refused,"Unverified source accepted");
            a.Repeat.Set(60*180+60,new(38,38,38));refused=false;
            try{StrokeProfiles.Measure(dot,a.Line,a.Before,a.Before,a.First,a.Repeat,new(0,0,0),true);}catch(InvalidDataException e){refused=e.Message=="stroke_paint_changes_on_repeat";}Need(refused,"18/12 repeat became valid");
        });
        test("RC6 measured full-stroke plan stays inside colors adds coverage and emits bound fixed-length commands",()=>{
            var s=MeasuredColorChecks.Config();var old=BrushFootprints.Stored(s);var cal=s.Calibration;cal.SetRect("canvas",new(0,0,1600,320));s.SetCalibration(cal);
            BrushFootprints.Save(s,old.Select(p=>BrushFootprints.Build(s,p.ShapeSlot,p.Size,Enumerable.Repeat(new BrushStamp(p.Possible,p.Solid,new(20,20,20)),3).ToArray())).ToArray());
            var dot=BrushFootprints.Find(s,20)!;
            foreach(bool vertical in new[]{false,true})
            {
                var o=Observe(dot,vertical).Observation;StrokeProfiles.Save(s,StrokeProfiles.Fit(s,dot,vertical,[o,o],o,42));
            }
            var target=MeasuredColorChecks.Plan();var result=StrokeProfilePlan.Build(target,s)!;
            var rect=s.Calibration.Rect("canvas");var coverage=new bool[rect.Width*rect.Height];var profiles=StrokeProfiles.Read(s).ToDictionary(p=>p.Id);
            int full=0;
            foreach(var(color,ops) in result.Groups)foreach(var op in ops)
            {
                int x=op.Line.X1-rect.Left,y=op.Line.Y1-rect.Top;
                var possible=op.StrokeProfileId is {} id?profiles[id].Possible:BrushSweep.Project(BrushFootprints.Find(s,op.Size,op.ShapeSlot)!.Possible,new(0,0,op.Line.X2-op.Line.X1,op.Line.Y2-op.Line.Y1));
                var solid=op.StrokeProfileId is {} sid?profiles[sid].Solid:BrushSweep.Project(BrushFootprints.Find(s,op.Size,op.ShapeSlot)!.Solid,new(0,0,op.Line.X2-op.Line.X1,op.Line.Y2-op.Line.Y1));
                foreach(var row in possible)for(int px=row.Left;px<row.Right;px++)Need(x+px>=0&&y+row.Y>=0&&x+px<rect.Width&&y+row.Y<rect.Height&&target.Indices[(y+row.Y)*400+(x+px)/4]==color,"Full physical stroke crossed color or Canvas");
                Need(solid.Any(r=>Enumerable.Range(r.Left,r.Right-r.Left).Any(px=>!coverage[(y+r.Y)*rect.Width+x+px])),"Fully redundant command retained");
                foreach(var row in solid)Array.Fill(coverage,true,(y+row.Y)*rect.Width+x+row.Left,row.Right-row.Left);
                if(op.StrokeProfileId is not null)full++;
            }
            Need(full>0&&coverage.Count(x=>x)==result.CoveredPixels,$"Full={full}, coverage={coverage.Count(x=>x)}/{result.CoveredPixels}, choice={System.Text.Json.JsonSerializer.Serialize(result.Diagnostics?.Choice)}");
            var schedule=TransferSchedule.Build(target,s,result.Groups);Need(schedule.Values.SelectMany(x=>x).Count(b=>b.StrokeProfileId is not null)==full,"Schedule dropped bound profile identity");
        });
        test("RC6 diagnostic exposure emits exactly requested holds and releases on interruption",()=>{
            foreach(int ms in new[]{0,64,128})
            {
                var recorder=new RecordingStrokeInput();BrushExposureMotion.Draw(new(50,50),ms,recorder);
                Need(recorder.Downs==1&&recorder.Ups==1&&!recorder.Held&&Math.Abs(recorder.UpAt-recorder.DownAt-ms/1000d)<1e-9,"Exposure was not exact");
            }
            var failing=new RecordingStrokeInput{FailHeldWait=true};bool stopped=false;
            try{BrushExposureMotion.Draw(new(50,50),64,failing);}catch(OperationCanceledException){stopped=true;}Need(stopped&&!failing.Held&&failing.Ups==1,"Exposure interruption left input held");
        });
    }
}


