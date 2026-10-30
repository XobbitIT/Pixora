using CanvasForge.Core;

internal static class StandardBandResponseChecks
{
    private static void Need(bool value,string reason){if(!value)throw new Exception(reason);}
    private static void Refuse(Action action){bool bad=false;try{action();}catch(InvalidDataException){bad=true;}Need(bad,"Invalid measured state accepted");}
    private static StandardBandResponseContext Context()=>new(new(new(0,0,500,500),new(0,0),new(2560,1440),96,3,3,
        StandardEvidenceSeries.ProductionSpeed,.02,.02,.02,"Rust Palette"),new('a',64),new('b',64),StandardContourMotion.Revision,
        StandardEvidenceSeries.Speed,"native before/after every released track");
    private static PixelImage Blank(){var p=new PixelImage(500,500);for(int i=0;i<250000;i++)p.Set(i,new(220,220,220));return p;}
    private static ScreenPoint[] Track(int x,int low=20,int high=80)=>Enumerable.Range(low,high-low+1).Select(y=>new ScreenPoint(x,y)).ToArray();
    private static PixelImage Fill(PixelImage image,int left,int right,int low=20,int high=80)
    {var p=image.Clone();for(int y=low;y<=high;y++)for(int x=left;x<=right;x++)p.Set(y*500+x,new(0,0,0));return p;}
    private static StandardBandResponse Model(PixelImage blank,ScreenLine? target=null)=>new(Context(),target??new(100,20,100,80),new(60,5,140,450),blank);
    public static void Run(Action<string,Action> test)
    {
        test("Band response retains complete absolute prefix and RGB overlap changes instead of delta union",()=>{
            var blank=Blank();var model=Model(blank);var first=Fill(blank,95,99);var a=model.Add("one",Track(96),blank,first);
            var second=first.Clone();second.Set(30*500+95,new(220,220,220));second.Set(31*500+95,new(58,49,47));
            var b=model.Add("two",Track(97),first,second);
            Need(a.IsFirstObservedPass&&!b.IsFirstObservedPass&&b.Commands.Count==2,"First and overlap states conflated");
            Need(b.PossiblePixels==a.PossiblePixels-1&&b.Transition.RemovedPossiblePixels==1,"Cumulative state built as union");
            Need(b.Transition.LightenedPreviouslyPaintedPixels==2&&b.Transition.LostDiagnosticStrongPixels==1,"RGB lightening or strong loss omitted");
            Need(b.EstablishedSolidPixels==0&&!b.ProductionEligible&&!b.PhysicalQualityCertified,"Observation promoted to certified paint");
        });
        test("Band response measures endpoint-region steps that a central-only score misses",()=>{
            var blank=Blank();var model=Model(blank,new(100,20,100,420));var first=Fill(blank,95,99,20,420);
            model.Add("one",Track(96,20,420),blank,first);var second=Fill(first,100,100,20,349);second=Fill(second,100,102,350,420);
            var state=model.Add("two",Track(97,20,420),first,second);var score=state.Score(model.Target,10);
            Need(state.Sections.Count==445&&state.Sections.Count(s=>s.Mandatory)==401,"Full reservation/body rows omitted");
            Need(score.MissingBodySections==0&&score.MaximumAdjacentEdgeStepPx==2,"End-region kink hidden");
            var central=state.Sections.Where(s=>s.Along>=100&&s.Along<=340).Select(s=>s.Edges!).ToArray();
            Need(central.All(s=>s.High==central[0].High),"Fixture central region is not flat");
        });
        test("Band response requires exact command history emitter cadence color and geometry context",()=>{
            var blank=Blank();var model=Model(blank);var first=Fill(blank,95,99);var a=model.Add("one",Track(96),blank,first);
            var second=Fill(first,100,100);var b=model.Add("two",Track(97),first,second);
            Need(model.FindExact(Context(),a.BaselineRgbaSha256,a.Commands)?.Id==a.Id&&model.FindExact(Context(),b.BaselineRgbaSha256,b.Commands)?.Id==b.Id,"Known prefix unavailable");
            Need(model.FindExact(Context(),new string('0',64),a.Commands) is null,"Changed initial surface reused an observed response");
            Need(model.FindExact(Context(),a.BaselineRgbaSha256,[b.Commands[1]]) is null&&model.FindExact(Context(),a.BaselineRgbaSha256,b.Commands.Reverse().ToArray()) is null,"History ignored");
            foreach(var context in new[]{Context() with{CapturePolicy="ordinary START"},Context() with{Geometry=Context().Geometry with{Dpi=144}},
                Context() with{Geometry=Context().Geometry with{RequestedHex="FFFFFF"}},Context() with{ActualSpeed=StandardEvidenceSeries.ProductionSpeed},
                Context() with{AppSha256=new('c',64)}})Need(model.FindExact(context,a.BaselineRgbaSha256,a.Commands) is null,"Context silently transferred");
            Refuse(()=>model.FindExact(Context() with{Geometry=Context().Geometry with{MappingRevision="scale-.996"}},a.BaselineRgbaSha256,a.Commands));
        });
        test("Band response rejects missing duplicate curved gapped noisy and foreign transitions atomically",()=>{
            var blank=Blank();var model=Model(blank);var first=Fill(blank,95,99);model.Add("one",Track(96),blank,first);string id=model.Id;
            Refuse(()=>model.Add("one",Track(97),first,first));
            Refuse(()=>model.Add("next",Track(98),first,first));
            Refuse(()=>model.Add("next",Track(97).Skip(1).ToArray(),first,first));
            var curved=Track(97);curved[10]=new(98,30);Refuse(()=>model.Add("next",curved,first,first));
            var noisy=first.Clone();noisy.Set(0,new(207,220,220));Refuse(()=>model.Add("next",Track(97),noisy,first));
            var foreign=first.Clone();foreign.Set(0,new(0,0,0));Refuse(()=>model.Add("next",Track(97),first,foreign));
            Need(model.States.Count==1&&model.Id==id,"Failed transition partially published");
            model.Add("next",Track(97),first,first);Need(model.States.Count==2,"Failed transition consumed source ID");
        });
        test("Band response freezes caller frames paths masks and immutable state IDs",()=>{
            var blank=Blank();var model=Model(blank);var first=Fill(blank,95,99);var path=Track(96);var next=first.Clone();
            var a=model.Add("one",path,blank,first);string id=a.Id;path[0]=new(0,0);blank.Set(30*500+95,new(0,0,0));first.Set(0,new(0,0,0));
            model.Add("two",Track(97),next,next);Need(a.Id==id&&a.Commands[0]==new ScreenLine(96,20,96,80),"Caller mutated frozen state");
            Need(a.PossiblePixels==305,"Caller mutated baseline/mask");
        });
        test("Band response refuses cumulative outside drift even when every individual transition is within RGB12",()=>{
            var blank=Blank();var model=Model(blank);var first=Fill(blank,95,99);first.Set(0,new(208,220,220));
            model.Add("one",Track(96),blank,first);var next=first.Clone();next.Set(0,new(196,220,220));
            Refuse(()=>model.Add("two",Track(97),first,next));Need(model.States.Count==1,"Accumulated foreign scene change hidden");
        });
        test("Band response keeps missing body rows and disconnected paint in the denominator",()=>{
            var blank=Blank();var model=Model(blank);var after=Fill(blank,95,99,25,80);var state=model.Add("one",Track(96),blank,after);
            Need(state.Score(model.Target,14).MissingBodySections==5&&state.Sections.Count(s=>s.Mandatory)==61,"Missing paint rows silently removed");
            Need(model.SelectObserved(14)!.Status=="OBSERVED_PREFIX_WITH_PAINT_GAPS","Paint gap became complete profile");
            var disconnected=Model(blank);var split=Fill(Fill(blank,95,99),109,110);var b=disconnected.Add("split",Track(96),blank,split);
            Need(b.Score(disconnected.Target,14).MissingBodySections==61&&b.Sections.Where(s=>s.Mandatory).All(s=>s.Reason=="ambiguous_strong_components"),"Disconnected row accepted");
        });
        test("Band response selects measured prefixes by both full edges and never globally removes the last pass",()=>{
            var blank=Blank();var good=Model(blank);var first=Fill(blank,95,99);good.Add("one",Track(96),blank,first);
            var second=Fill(first,100,104,18,82);var state=good.Add("two",Track(97),first,second);var selected=good.SelectObserved(10)!;
            Need(selected.PrefixLength==2&&selected.Score.MaximumTwoEdgeErrorPx==.5,"Useful last pass removed");
            Need(selected.Score.StartCapPossiblePixels==10&&selected.Score.EndCapPossiblePixels==10,"Physical caps discarded");
            Need(!selected.DispatchAllowed&&!selected.ProductionEligible&&selected.Commands.SequenceEqual(state.Commands),"Draft became executable or invented a route");
            var bad=Model(blank);first=Fill(blank,95,104);bad.Add("one",Track(96),blank,first);bad.Add("two",Track(97),first,Fill(first,105,106));
            Need(bad.SelectObserved(10)!.PrefixLength==1,"Worsening last pass forced");Refuse(()=>bad.SelectObserved(double.NaN));
        });
        test("Band response preserves strict RGB12 and diagnostic strong80 boundaries",()=>{
            var blank=Blank();var model=Model(blank);var after=blank.Clone();
            after.Set(30*500+95,new(208,208,208));after.Set(30*500+96,new(207,207,207));after.Set(30*500+97,new(140,140,140));after.Set(30*500+98,new(139,139,139));
            var a=model.Add("one",Track(96),blank,after);
            Need(a.PossiblePixels==3&&a.Transition.ChangedPixels==3,"RGB12 threshold changed");
            var b=model.Add("two",Track(97),after,blank);Need(b.Transition.RemovedPossiblePixels==3&&b.Transition.LostDiagnosticStrongPixels==1,"Strict >80 diagnostic changed");
        });
        test("Band response does not reward a narrow smooth prefix when maximum target error plateaus",()=>{
            var blank=Blank();var model=Model(blank);var first=Fill(blank,97,103);
            var a=model.Add("narrow",Track(96),blank,first);
            var second=Fill(Fill(first,104,106),107,107,60,80);
            var b=model.Add("wider",Track(97),first,second);
            var old=a.Score(model.Target,14);var next=b.Score(model.Target,14);
            Need(old.MaximumTwoEdgeErrorPx==next.MaximumTwoEdgeErrorPx&&old.MaximumAdjacentEdgeStepPx<next.MaximumAdjacentEdgeStepPx,"Fixture does not reproduce the plateau/smoothness trap");
            Need(next.MeanSquaredTwoEdgeErrorPx2<old.MeanSquaredTwoEdgeErrorPx2,"Both target edges omitted from aggregate fit");
            var chosen=model.SelectObserved(14)!;
            Need(chosen.StateId==b.Id&&chosen.PrefixLength==2,"Smooth under-width prefix won a saturated maximum-error tie");
            Need(!chosen.DispatchAllowed&&!chosen.PhysicalQualityCertified&&!chosen.ProductionEligible,"Ranking improvement qualified a physical band");
        });
    }
}
