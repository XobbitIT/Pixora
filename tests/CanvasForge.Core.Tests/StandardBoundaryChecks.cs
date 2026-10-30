using CanvasForge.Core;

internal static class StandardBoundaryChecks
{
    private static void Need(bool value,string reason){if(!value)throw new Exception(reason);}
    private static void Refused(Action action){bool failed=false;try{action();}catch(Exception e) when(e is ArgumentException or InvalidDataException){failed=true;}Need(failed,"Invalid geometry accepted");}
    private static StandardGeometryContext Context(int w=24,int h=6)=>new(new(20,30,20+w,30+h),new(0,0),new(2560,1440),96,3,3,StandardEvidenceSeries.Speed,.020,.020,.020,"Rust Palette");
    private static StandardKernelObservation Observation(string id,StandardGeometryContext c,BrushSpan[]? possible=null,BrushSpan[]? solid=null,bool source=true,bool repeat=true,
        bool vertical=false,bool reverse=false,int length=2)=>new(id,c,vertical,reverse,length,possible??[new(0,0,3),new(1,0,3)],solid??[new(0,0,3),new(1,0,3)],source,repeat);
    private static StandardKernelCandidate Kernel(StandardGeometryContext c,string[]? required=null)=>StandardKernelCandidate.Freeze([Observation("train-a",c),Observation("train-b",c)],required??["held"]);
    private static HashSet<ScreenPoint> Points(IEnumerable<BrushSpan> rows)=>BrushFootprints.Points(rows.ToArray()).ToHashSet();
    public static void Run(Action<string,Action> test)
    {
        test("Boundary observation preserves physical offsets fringe and caps without declaring Solid",()=>{
            var c=Context(30,20);var before=new PixelImage(30,20);for(int i=0;i<600;i++)before.Set(i,new(200,200,200));var after=before.Clone();
            for(int y=6;y<=10;y++)for(int x=4;x<=17;x++)after.Set(y*30+x,new(0,0,0));after.Set(5*30+7,new(180,180,180));
            var observation=StandardPaintObservation.Measure("native",c,new(25,40,35,40),before,after,c.Canvas);
            Need(Points(observation.Possible).Contains(new(2,-5))&&Points(observation.Possible).Contains(new(12,0)),"Physical mask recentered or caps clipped");
            Need(observation.Possible.Sum(r=>r.Right-r.Left)==71&&observation.HighContrast.Sum(r=>r.Right-r.Left)==70,"Fringe was ignored or counted as strong");
            Need(!observation.ProductionEligible&&StandardKernelObservation.Observational(observation).Solid.Count==0,"High contrast promoted to guaranteed Solid");
            Need(observation.BeforeRgbaSha256.Length==64&&observation.AfterRgbaSha256!=observation.BeforeRgbaSha256,"Native provenance missing");
        });
        test("Boundary observation uses original reverse anchor and unchanged strict delta thresholds",()=>{
            var c=Context(30,20);var before=new PixelImage(30,20);var after=before.Clone();
            after.Set(10*30+10,new(12,12,12));after.Set(10*30+11,new(13,13,13));after.Set(10*30+12,new(80,80,80));after.Set(10*30+13,new(81,81,81));
            var o=StandardPaintObservation.Measure("reverse",c,new(40,40,30,40),before,after,c.Canvas);
            Need(o.Reverse&&!o.Vertical&&o.Length==10,"Command direction changed");
            Need(Points(o.Possible).SetEquals(new[]{new ScreenPoint(-9,0),new(-8,0),new(-7,0)})&&Points(o.HighContrast).SetEquals(new[]{new ScreenPoint(-7,0)}),"12/80 thresholds or raw anchor changed");
            Refused(()=>StandardPaintObservation.Measure("bad",c,new(25,35,30,36),before,after,c.Canvas));
            Refused(()=>StandardPaintObservation.Measure("bad",c,new(25,35,30,35),before,after,new(20,30,25,35)));
            Refused(()=>StandardPaintObservation.Measure("bad",c,new(25,35,30,35),new(5,5),after,c.Canvas));
        });
        test("Boundary context hashes geometry emitter timing direction-independent environment and invalidates every relevant change",()=>{
            var c=Context();var variants=new[]{c with{Canvas=new(21,30,45,36)},c with{Dpi=144},c with{ClientOrigin=new(1,0)},c with{ClientSize=new(1920,1080)},
                c with{ShapeSlot=4},c with{Size=2},c with{Speed=StandardEvidenceSeries.ProductionSpeed},c with{FrameSeconds=.03},c with{EndHoldSeconds=.03},c with{ReleaseSeconds=.03},c with{ColorMode="HEX Direct"},c with{RequestedHex="FFFFFF"}};
            Need(variants.All(x=>x.Id!=c.Id)&&variants.Select(x=>x.Id).Distinct().Count()==variants.Length,"Context omitted relevant fields");
            Refused(()=>(c with{MappingRevision="scale-.996"}).Validate());Refused(()=>(c with{Emitter=StandardStrokeMotion.Revision}).Validate());
            Refused(()=>(c with{FrameSeconds=double.NaN}).Validate());
        });
        test("Boundary training freezes full Possible union and independently supplied Solid intersection",()=>{
            var c=Context();BrushSpan[] p=[new(0,0,4),new(1,0,3)],a=[new(0,0,3),new(1,0,3)],b=[new(0,1,3),new(1,0,3)];
            var o=Observation("a",c,p,a);var k=StandardKernelCandidate.Freeze([o,Observation("b",c,solid:b)]);string id=k.Id;
            Need(Points(k.Possible).Count==7&&Points(k.Solid).Count==5,"Wrong union/intersection");p[0]=new(0,-5,20);a[0]=new(0,-5,20);
            Need(k.Id==id&&Points(k.Possible).Count==7&&Points(k.Solid).Count==5,"Caller mutated frozen masks");
            Need(!k.ProductionEligible,"Candidate silently authorized production");
        });
        test("Boundary training rejects mixed contexts routes duplicates and invalid masks",()=>{
            var c=Context();var a=Observation("a",c);
            foreach(var b in new[]{Observation("b",c with{Dpi=144}),Observation("b",c,vertical:true),Observation("b",c,reverse:true),Observation("b",c,length:3),Observation("a",c)})
                Refused(()=>StandardKernelCandidate.Freeze([a,b]));
            Refused(()=>StandardKernelCandidate.Freeze([]));Refused(()=>Observation("bad",c,possible:[new(0,0,1)],solid:[new(0,0,2)]));
            Refused(()=>Observation("bad",c,possible:[new(0,3,2)]));
        });
        test("Boundary validation requires every mandatory heldout and refuses reused or extra sample identities",()=>{
            var c=Context();var k=Kernel(c,["held-a","held-b"]);
            Need(k.Validate(["held-a","held-b"],[Observation("held-a",c)]).Status=="INCOMPLETE","Missing sample removed from denominator");
            foreach(var ids in new[]{new[]{"train-a"},new[]{"held-a","held-a"},Array.Empty<string>()})Need(k.Validate(ids,[]).Status=="INVALID","Invalid holdout IDs accepted");
            Need(k.Validate(["held-a"],[Observation("extra",c)]).Status=="INVALID","Extra sample accepted");
            Need(k.Validate(["held-a"],[Observation("held-a",c),Observation("held-a",c)]).Status=="INVALID","Duplicate heldout accepted");
            Need(k.Validate(["chosen-after-results"],[Observation("chosen-after-results",c)]).Reason=="heldout_protocol_changed_after_freeze","Heldout sample selection changed after freezing");
        });
        test("Boundary heldout cannot expand Possible shrink Solid or change profile identity",()=>{
            var c=Context();var k=Kernel(c);string id=k.Id;
            var spill=Observation("held",c,possible:[new(0,0,4),new(1,0,3)]);
            var r=k.Validate(["held"],[spill]);Need(!r.Passed&&r.OutsidePossiblePixels==1,"One outside pixel tolerated");
            var missing=Observation("held",c,solid:[new(0,0,2),new(1,0,3)]);r=k.Validate(["held"],[missing]);Need(!r.Passed&&r.MissingSolidPixels==1,"One missing Solid pixel tolerated");
            Need(k.Id==id&&Points(k.Possible).Count==6&&Points(k.Solid).Count==6,"Validation repaired the candidate");
        });
        test("Boundary validation refuses changed speed direction source and repeat contract",()=>{
            var c=Context();var k=Kernel(c);
            foreach(var held in new[]{Observation("held",c with{Speed=StandardEvidenceSeries.ProductionSpeed}),Observation("held",c,reverse:true),Observation("held",c,vertical:true),
                Observation("held",c,source:false),Observation("held",c,repeat:false)})Need(!k.Validate(["held"],[held]).Passed,"Unverified or changed route passed");
            var unverified=StandardKernelCandidate.Freeze([Observation("a",c,repeat:false)]);
            Need(unverified.Solid.Count==0&&!unverified.Validate(["held"],[Observation("held",c)]).Passed,"Single-pass evidence created certified core");
        });
        test("Boundary positive heldout validates the model without enabling execution",()=>{
            var c=Context();var k=Kernel(c);var r=k.Validate(["held"],[Observation("held",c)]);
            Need(r.Passed&&r.MissingSolidPixels==0&&r.OutsidePossiblePixels==0&&!r.ProductionEligible&&!k.ProductionEligible,"Model and production verdicts conflated");
        });
        test("Boundary draft reproduces two exact color regions using full kernels and an independent pixel oracle",()=>{
            var c=Context();var k=Kernel(c);var white=Kernel(c with{RequestedHex="FFFFFF"});var target=Enumerable.Range(0,24*6).Select(i=>i%24<12?0:0xffffff).ToArray();
            var result=StandardBoundaryPlan.Build(target,c,[k,white]);Need(result.Complete&&result.CoveredSolidPixels==144&&!result.DispatchAllowed,"Candidate not complete or became executable");
            var actual=Enumerable.Repeat(-1,144).ToArray();var occupied=new HashSet<ScreenPoint>();
            foreach(var command in result.Commands)
            {
                int x=command.Line.X1-c.Canvas.Left,y=command.Line.Y1-c.Canvas.Top;
                Need(command.Line.X2-command.Line.X1==2&&command.Line.Y2==command.Line.Y1,"Kernel length/direction changed");
                foreach(var point in Points(k.Possible))
                {var at=new ScreenPoint(x+point.X,y+point.Y);Need(occupied.Add(at),"Unproven Possible overlap allowed");Need(target[at.Y*24+at.X]==command.Color,"Physical fringe crossed color");actual[at.Y*24+at.X]=command.Color;}
            }
            Need(actual.SequenceEqual(target),"Kernel painting changed boundary thickness");
        });
        test("Boundary cannot reuse a black paint footprint for a different requested RGB",()=>{
            var c=Context();var target=Enumerable.Range(0,144).Select(i=>i%24<12?0:0xffffff).ToArray();
            var result=StandardBoundaryPlan.Build(target,c,[Kernel(c)]);
            Need(!result.Complete&&result.CoveredSolidPixels==72&&result.Commands.All(x=>x.Color==0)&&result.Uncovered.Count(x=>x)==72,"Black geometry silently reused for white");
        });
        test("Boundary full Possible blocks unsafe cap fringe transparency and unpainted residual",()=>{
            var c=Context(9,3);var k=StandardKernelCandidate.Freeze([Observation("a",c,possible:[new(0,-1,4)],solid:[new(0,0,3)])]);
            var target=Enumerable.Repeat(-1,27).ToArray();for(int x=3;x<6;x++)target[9+x]=0;
            var r=StandardBoundaryPlan.Build(target,c,[k]);Need(!r.Complete&&r.CoveredSolidPixels==0&&r.Uncovered.Count(v=>v)==3,"Fringe was clipped or credited");
            target[9+2]=1;target[9+6]=1;r=StandardBoundaryPlan.Build(target,c,[k]);Need(r.CoveredSolidPixels==0,"Caps crossed neighboring color");
        });
        test("Boundary possible overlap is rejected even within one color and Solid alone gets credit",()=>{
            var c=Context(12,3);var k=StandardKernelCandidate.Freeze([Observation("a",c,possible:[new(0,0,5)],solid:[new(0,1,4)])]);
            var r=StandardBoundaryPlan.Build(Enumerable.Repeat(0,36).ToArray(),c,[k]);
            Need(!r.Complete&&r.CoveredSolidPixels<36&&r.CoveredSolidPixels==r.Commands.Count*3,"Fringe or overlap counted as guaranteed coverage");
            var used=new HashSet<ScreenPoint>();foreach(var command in r.Commands)foreach(var p in Points(k.Possible))
                Need(used.Add(new(command.Line.X1-c.Canvas.Left+p.X,command.Line.Y1-c.Canvas.Top+p.Y)),"Unverified repeated application allowed");
        });
        test("Boundary missing Solid context mismatch budget and cancellation cannot report completion",()=>{
            var c=Context();var target=Enumerable.Repeat(0,144).ToArray();var empty=StandardKernelCandidate.Freeze([Observation("a",c,solid:[])]);
            Need(StandardBoundaryPlan.Build(target,c,[empty]).Reason=="no_established_solid","Empty model hid missing work");
            Need(StandardBoundaryPlan.Build(target,c,[empty,Kernel(c with{Dpi=144})]).Reason=="no_established_solid","Historic incompatible model hid missing current Solid");
            Need(StandardBoundaryPlan.Build(target,c,[Kernel(c with{Dpi=144})]).Reason=="context_mismatch","Stale model reused");
            var limited=StandardBoundaryPlan.Build(target,c,[Kernel(c)],1);Need(!limited.Complete&&limited.Reason=="placement_budget_exhausted","Budget exhaustion became complete");
            limited=StandardBoundaryPlan.Build(target,c,[Kernel(c)],maximumMaskPixelChecks:1);Need(!limited.Complete&&limited.CoveredSolidPixels==0&&limited.Reason=="mask_budget_exhausted","Partial mask check committed a command");
            using var cancel=new CancellationTokenSource();cancel.Cancel();bool stopped=false;try{StandardBoundaryPlan.Build(target,c,[Kernel(c)],token:cancel.Token);}catch(OperationCanceledException){stopped=true;}Need(stopped,"Cancelled planner continued");
            Refused(()=>StandardBoundaryPlan.Build([0],c,[]));
        });
        test("Boundary layer replay measures other-color overwrite while preserving same-color distinction",()=>{
            BrushSpan[] black=[new(1,2,8)],white=[new(1,0,4)],repeat=[new(1,0,2)];
            var r=StandardBoundaryLayers.Replay(10,3,[(0,black),(1,white),(1,repeat)]);
            Need(r.Layers[1].TouchedOtherColorPixels==2&&r.Layers[1].TouchedEarlierPixels==2&&r.Layers[2].TouchedEarlierPixels==2&&r.Layers[2].TouchedOtherColorPixels==0,"Wrong overwrite accounting");
            Need(r.LastPossibleOwner[12]==1&&r.LastPossibleOwner[14]==0&&!r.PhysicalQualityCertified,"Wrong layer ownership or certification");
            Refused(()=>StandardBoundaryLayers.Replay(10,3,[(0,new BrushSpan[]{new(1,-1,2)})]));
        });
    }
}
