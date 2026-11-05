using CanvasForge.Core;

// F3: experimental Standard stroke joining (standard_join_experimental, OFF by default).
internal static class StandardJoinChecks
{
    private static void Require(bool value,string problem){if(!value)throw new Exception(problem);}
    private static Settings Config(bool? join)
    {
        var s=FastColorFixtures.Config(true,"Auto");s.Set("release_profile","Standard");
        if(join is { } value)s.Set(TransferSchedule.StandardJoinKey,value);
        return s;
    }
    private static Dictionary<int,List<PaintBatch>> Groups(PaintPlan plan,Settings s)=>TransferSchedule.Build(plan,DrawingWorkflow.Effective(s.Clone()));
    private static IEnumerable<(int X,int Y)> Pixels(ScreenLine l)
    {
        int n=TransferSchedule.Length(l),dx=Math.Sign(l.X2-l.X1),dy=Math.Sign(l.Y2-l.Y1);
        for(int k=0;k<=n;k++)yield return (l.X1+k*dx,l.Y1+k*dy);
    }
    private static HashSet<(int,int)> Cover(IEnumerable<PaintBatch> batches)=>batches.SelectMany(b=>b.Segments).SelectMany(Pixels).ToHashSet();

    private sealed class Recorder(int failAtMove=-1):ICalibratedStrokeInput
    {
        public readonly List<ScreenPoint> Points=[];public readonly List<bool> Buttons=[];int moves;
        public double Seconds{get;private set;}
        public void Move(IReadOnlyList<ScreenPoint> points){if(++moves==failAtMove)throw new OperationCanceledException("ESC");Points.AddRange(points);}
        public void Button(bool up)=>Buttons.Add(up);
        public void Shift(bool up){}
        public void Wait(double seconds)=>Seconds+=seconds;
    }

    internal static PixelImage Fill()=>SpeedReportChecks.Fill();

    // Printed by StandardJoinOffFixtures.Print on commit 4b5fa8f (before F3):
    // plan identity, schedule SHA, ETA and the SHA of the full input-event trace.
    private static readonly Dictionary<string,(string Identity,string Schedule,string Eta,string Input)> PreF3=new()
    {
        ["fill"]=("0BABE2AF2D0A94F97D2BD097296F74E89A3EE1EF692A55A99B6B58B15A560C6A","D0BB4960BD9A7D1574B4186F205D8623156B780278504D35F4D6D24D1F801FED","50.74900000000024","45D963E710DFB4F4BD462B90152B3ECAC9576798376EED0A7845C46751729B6B"),
        ["anime"]=("13EE05DA3A40110C38F456E774E717C62F2C6BE9354871515F4045099C7E4D5E","D70521BFDD9F3AE5651E0B54424FC3545FC6D913669EE7C0C281A2CE1141F90A","2357.7562000003672","FB85A02144551D0345CFD364A59FA2DD53133E65FA35ABC4113C5B2B03D06E24"),
    };

    // Tracked recorder that can fail at a named step of the joined transaction.
    private sealed class Tracked(string failAt,int occurrence=1):ITrackedStrokeInput
    {
        public readonly List<string> Events=[];readonly Dictionary<string,int> seen=[];
        public double Seconds{get;private set;}
        void Step(string name){Events.Add(name);seen[name]=seen.GetValueOrDefault(name)+1;if(name==failAt&&seen[name]==occurrence)throw new OperationCanceledException(name);}
        public void Move(IReadOnlyList<ScreenPoint> points)=>Step("move");
        public void Button(bool up)=>Step(up?"up":"down");
        public void Shift(bool up){}
        public void Wait(double seconds){Seconds+=seconds;Step("wait");}
        public void ConfirmPosition(ScreenPoint expected)=>Step("confirm");
        public void BeginCursorTracking(ScreenPoint expected)=>Step("begin");
        public void EndCursorTracking()=>Step("end");
    }

    public static void Run(Action<string,Action> test)
    {
        test("F3 OFF matches commit 4b5fa8f: identity, schedule, ETA and every input event",()=>{
            foreach(var (name,image) in StandardJoinOffFixtures.Images())
            foreach(bool? flag in new bool?[]{null,false})
            {
                var s=StandardJoinOffFixtures.Config();if(flag is { } f)s.Set(TransferSchedule.StandardJoinKey,f);
                var m=StandardJoinOffFixtures.Measure(image,s);
                Require(m==PreF3[name],$"{name} ({(flag is null?"absent":"false")}) differs from 4b5fa8f: {m}");
            }
        });
        test("F3 joined transaction releases the button and ends tracking on a failure at every step",()=>{
            ScreenLine[] path=[new(10,10,40,10),new(40,10,40,11),new(40,11,10,11)];
            var s=DrawingWorkflow.Effective(Config(true));var speed=SpeedProfile.Get("Max Speed");
            var clean=new Tracked("none");StandardStrokeMotion.DrawJoined(path,s,speed,clean);
            Require(clean.Events.Count(e=>e=="down")==1&&clean.Events.Count(e=>e=="up")==1&&clean.Events.Count(e=>e=="begin")==1&&clean.Events.Count(e=>e=="end")==1,"Clean press is not a single tracked transaction");
            int waitsBeforeEnd=clean.Events.TakeWhile(e=>e!="up").Count(e=>e=="wait");
            var steps=new (string Step,int Occurrence)[]{("move",1),("wait",1),("confirm",1),("begin",1),("down",1),("wait",2),("move",2),("move",4),("wait",waitsBeforeEnd),("up",1),("confirm",2),("wait",waitsBeforeEnd+1)};
            foreach(var (step,occurrence) in steps)
            {
                var input=new Tracked(step,occurrence);bool threw=false;
                try{StandardStrokeMotion.DrawJoined(path,s,speed,input);}catch(OperationCanceledException){threw=true;}
                Require(threw,$"Failure at {step}#{occurrence} was swallowed");
                bool pressed=input.Events.Contains("down");
                if(pressed)Require(input.Events.LastIndexOf("up")>input.Events.LastIndexOf("down")||step=="up","Button stayed down after a failure at "+step);
                if(input.Events.Contains("begin"))Require(input.Events.Contains("end"),"Cursor tracking was not ended after a failure at "+step);
                Require(input.Events.Count(e=>e=="down")<=1,"A failure caused a second press");
            }
        });
        test("F3 envelope boundary: another color at 6 px blocks the join, at 7 px it does not; presses stop at 16 strokes",()=>{
            // One Canvas pixel per cell; color A everywhere except one B pixel left of the right edge.
            int Joins(int distance)
            {
                var image=new PixelImage(60,60);for(int k=0;k<3600;k++)image.Set(k,new(40,120,200));
                if(distance>=0)image.Set(30*60+59-distance,new(250,250,250));
                var on=FastColorFixtures.Config(true,"Auto",60,1);on.Set("release_profile","Standard");on.Set(TransferSchedule.StandardJoinKey,true);
                // No blur, cleanup or edge sharpening: the foreign pixel stays one pixel.
                on.Set("preblur",0);on.Set("median_cleanup",false);on.Set("smooth_passes",0);on.Set("min_region",1);on.Set("edge_preserve",false);
                var plan=Planner.Build(image,on);var groups=Groups(plan,on);var rect=on.Calibration.Rect("canvas");
                int a=plan.Palette.Select((p,i)=>(p,i)).Single(x=>x.p.Color==new Rgb(40,120,200)).i;
                // Vertical 1 px connectors at the right edge across rows 29/30/31 (row 30 holds B).
                return groups[a].Where(b=>b.Segments.Count>1).SelectMany(b=>b.Segments)
                    .Count(l=>l.X1==l.X2&&l.Y1!=l.Y2&&l.X1==rect.Right-1&&Math.Min(l.Y1,l.Y2)>=rect.Top+22&&Math.Max(l.Y1,l.Y2)<=rect.Top+38);
            }
            int at6=Joins(6),at7=Joins(7),none=Joins(-1);
            Require(at7==none&&at7>0,$"A foreign pixel 7 px away still blocked joins ({at7} vs {none} without it)");
            Require(at6<at7,$"A foreign pixel at 6 px did not block joins ({at6} vs {at7} at 7 px)");
            var fill=Planner.Build(Fill(),Config(true));var presses=Groups(fill,Config(true)).Values.SelectMany(x=>x).ToArray();
            Require(presses.All(b=>b.SourceStrokes<=TransferSchedule.MaximumStrokes)&&presses.Any(b=>b.SourceStrokes==TransferSchedule.MaximumStrokes),"Press size limit is not reached exactly");
        });
        test("F3 OFF is byte-for-byte the former plan, schedule and ETA (absent key or explicit false)",()=>{
            foreach(var image in new[]{Fill(),FastColorFixtures.Anime(200,200)})
            {
                var absent=Config(null);var off=Config(false);
                var a=Planner.Build(image,absent);var b=Planner.Build(image,off);
                Require(a.Identity==b.Identity,"An explicit OFF flag changed the plan identity");
                var ga=Groups(a,absent);var gb=Groups(b,off);
                Require(System.Text.Json.JsonSerializer.Serialize(ga)==System.Text.Json.JsonSerializer.Serialize(gb),"OFF changed the schedule");
                Require(ga.Values.SelectMany(x=>x).All(x=>x.Segments.Count==1),"OFF produced joined batches");
                Require(Coverage.EstimateSeconds(a,absent)==Coverage.EstimateSeconds(b,off),"OFF changed the ETA");
                Require(Planner.Build(image,Config(true)).Identity!=a.Identity,"ON shares the OFF identity, so a checkpoint could resume across modes");
                var native=Config(true);native.Set("standard_native_line",true);
                Require(!TransferSchedule.StandardJoin(DrawingWorkflow.Effective(native)),"Join is active together with the native-line emitter");
            }
        });
        test("F3 ON covers exactly the same center pixels per color, all inside Canvas; connectors stay in their own color",()=>{
            foreach(var image in new[]{Fill(),FastColorFixtures.Anime(200,200),FastColorFixtures.Photo(160,160)})
            {
                var off=Config(false);var on=Config(true);var plan=Planner.Build(image,off);
                var go=Groups(plan,off);var gn=Groups(plan,on);
                var rect=on.Calibration.Rect("canvas");
                Require(go.Keys.OrderBy(x=>x).SequenceEqual(gn.Keys.OrderBy(x=>x)),"Colors changed");
                foreach(int color in go.Keys)
                {
                    var before=Cover(go[color]);var after=Cover(gn[color]);
                    Require(before.SetEquals(after),$"Color {color}: joined coverage differs ({after.Except(before).Count()} extra, {before.Except(after).Count()} missing)");
                    Require(after.All(p=>p.Item1>=rect.Left&&p.Item1<rect.Right&&p.Item2>=rect.Top&&p.Item2<rect.Bottom),"A joined point is outside Canvas");
                    var originals=go[color].SelectMany(b=>b.Segments).ToHashSet();
                    var xb=Coverage.Partition(rect.Left,rect.Right,plan.Width);var yb=Coverage.Partition(rect.Top,rect.Bottom,plan.Height);
                    int CellOf(int[] b,int px){int k=Array.BinarySearch(b,px);return k>=0?k:~k-1;}
                    foreach(var batch in gn[color])
                    {
                        // Connector points: joined path pixels that are not on an original segment of the batch.
                        var connectors=batch.Segments.Count<=1?[]:batch.Segments.Where(l=>!originals.Contains(l)&&!originals.Contains(TransferSchedule.Reverse(l))).ToArray();
                        Require(connectors.Sum(TransferSchedule.Length)<=TransferSchedule.MaximumConnector,"Connector budget per press exceeded");
                        foreach(var l in connectors)foreach(var (x,y) in Pixels(l))
                        {
                            int r=TransferSchedule.StandardJoinRadius;
                            Require(before.Contains((x,y)),"Connector centre is not on this color's own route");
                            for(int cy=CellOf(yb,Math.Max(rect.Top,y-r));cy<=CellOf(yb,Math.Min(rect.Bottom-1,y+r));cy++)
                            for(int cx=CellOf(xb,Math.Max(rect.Left,x-r));cx<=CellOf(xb,Math.Min(rect.Right-1,x+r));cx++)
                                Require(plan.Indices[cy*plan.Width+cx]==color,"Connector envelope touches another color");
                        }
                        Require(batch.Segments.Count<=1||batch.SourceStrokes<=TransferSchedule.MaximumStrokes,"Too many strokes in one press");
                        for(int i=1;i<batch.Segments.Count;i++)Require(batch.Segments[i-1].X2==batch.Segments[i].X1&&batch.Segments[i-1].Y2==batch.Segments[i].Y1,"Joined batch is not contiguous");
                    }
                }
                Require(go.Values.Sum(x=>x.Sum(b=>b.SourceStrokes))==gn.Values.Sum(x=>x.Sum(b=>b.SourceStrokes)),"Source strokes were lost or duplicated");
            }
        });
        test("F3 a 1-cell checkerboard gets no joins; a solid fill joins about 16 rows per press",()=>{
            // One image pixel = one target cell = one Canvas pixel: every same-color
            // neighbour is diagonal, so any connector would cross the other color.
            var checker=new PixelImage(130,130);for(int y=0;y<130;y++)for(int x=0;x<130;x++)checker.Set(y*130+x,((x+y)&1)==0?new(0,0,0):new(255,255,255));
            Settings Pixel(bool join){var s=FastColorFixtures.Config(true,"Auto",130,1);s.Set("release_profile","Standard");s.Set(TransferSchedule.StandardJoinKey,join);return s;}
            var off=Pixel(false);var on=Pixel(true);var plan=Planner.Build(checker,off);
            Require(plan.ColorCount==2,"Checker fixture is not two colors");
            var go=Groups(plan,off);var gn=Groups(plan,on);
            Require(gn.Values.Sum(x=>x.Count)==go.Values.Sum(x=>x.Count)&&gn.Values.SelectMany(x=>x).All(b=>b.Segments.Count==1),"A checkerboard was joined across other colors");
            off=Config(false);on=Config(true);
            var fill=Planner.Build(Fill(),off);var fo=Groups(fill,off).Values.Sum(x=>x.Count);var fn=Groups(fill,on).Values.Sum(x=>x.Count);
            Require(fn<=(fo+TransferSchedule.MaximumStrokes-1)/TransferSchedule.MaximumStrokes+1,$"Fill joins {fo} rows into {fn} presses");
            Require(Coverage.EstimateSeconds(fill,on)<Coverage.EstimateSeconds(fill,off),"Joining did not shorten the ETA");
        });
        test("F3 joined emission: every path pixel once, one press, button released on cancel mid-batch",()=>{
            ScreenLine[] path=[new(10,10,30,10),new(30,10,30,11),new(30,11,10,11),new(10,11,10,12),new(10,12,25,12)];
            var s=DrawingWorkflow.Effective(Config(true));var speed=SpeedProfile.Get("Max Speed");
            var rec=new Recorder();StandardStrokeMotion.DrawJoined(path,s,speed,rec);
            var expected=new List<ScreenPoint>{new(10,10)};
            foreach(var l in path)expected.AddRange(Pixels(l).Skip(1).Select(p=>new ScreenPoint(p.X,p.Y)));
            Require(rec.Points.SequenceEqual(expected),"Joined emission skipped or repeated a pixel");
            Require(rec.Buttons.SequenceEqual([false,true]),"A joined batch did not use exactly one press");
            var cancelled=new Recorder(3);bool threw=false;
            try{StandardStrokeMotion.DrawJoined(path,s,speed,cancelled);}catch(OperationCanceledException){threw=true;}
            Require(threw&&cancelled.Buttons.Count==2&&cancelled.Buttons[^1],"Cancel mid-batch did not release the button");
            bool rejected=false;try{StandardStrokeMotion.DrawJoined([new(0,0,5,0),new(7,0,9,0)],s,speed,new Recorder());}catch(ArgumentException){rejected=true;}
            Require(rejected,"A gap inside a joined batch was drawn");
            Require(Math.Abs(MotionTransportTiming.StandardJoined(s,speed,path)-rec.Seconds)<1e-12,"Joined ETA differs from the emitter");
        });
        test("F3 resume is at a batch boundary: no joined segment is skipped or repeated",()=>{
            var on=Config(true);var plan=Planner.Build(Fill(),on);var s=DrawingWorkflow.Effective(on.Clone());
            var groups=TransferSchedule.Build(plan,s);var order=TransferSchedule.Order(plan,groups);
            int group=0;var batches=groups[order[group]];Require(batches.Count>2,"Fixture too small");
            for(int line=0;line<=batches.Count;line++)
            {
                var timing=PaintTimingPlan.Build(s,groups,order,group,line);
                var resumed=timing.Where(w=>w.Motion).Select(w=>int.Parse(w.Id.Split(':')[2])).ToArray();
                Require(resumed.SequenceEqual(Enumerable.Range(line,batches.Count-line)),$"Resume at {line} skips or repeats a batch");
            }
            var r=SpeedReport.Build(Fill(),on,variants:false);
            Require(Math.Abs(r.Breakdown.Sum(p=>p.Seconds)-r.Seconds)<1e-6&&r.Breakdown.All(p=>p.Name!="motion_other"),"F1 cannot classify joined presses");
            Require(Math.Abs(r.Seconds-Coverage.EstimateSeconds(plan,on))<1e-6,"F1 ETA differs with join ON");
        });
    }
}
