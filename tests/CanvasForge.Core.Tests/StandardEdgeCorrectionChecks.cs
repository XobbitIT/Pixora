using CanvasForge.Core;

internal static class StandardEdgeCorrectionChecks
{
    static void Need(bool value,string why){if(!value)throw new Exception(why);}
    static StandardGeometryContext Context()=>new(new(0,0,1000,1000),new(0,0),new(1000,1000),96,3,3,SpeedProfile.Get("Max Speed"),.02,.02,.02,"Rust Palette");
    static StandardEdgeSection[] Sections(params double[] centers)=>centers.Select((c,i)=>new StandardEdgeSection(20+i,c-2.5,c+2.5)).ToArray();
    static StandardEdgeCorrection Solve(StandardEdgeSection[] sections,int shift=4)=>StandardEdgeCorrection.Solve(Context(),new(50,20,50,20+sections.Length-1),"source-a",sections,47.5,52.5,shift);
    public static void Run(Action<string,Action> test)
    {
        test("Two-edge inverse corrects a synthetic positional kink with a connected integer path",()=>{
            var observed=Sections(50,50,51,52,52,51,50);var result=Solve(observed);
            Need(result.OriginalMaximumEdgeError==2&&result.SurrogateMaximumEdgeError==0,"Surrogate did not remove the synthetic shift");
            Need(result.CandidatePoints.Count==7&&result.ShiftOffsets.SequenceEqual(new[]{0,0,-1,-2,-2,-1,0}),"Unexpected inverse path");
            Need(!result.DispatchAllowed&&!result.ProductionEligible&&result.Status=="CANDIDATE_ONLY","Synthetic fit enabled production");
        });
        test("Two-edge correction cannot hide width changes behind a straight center",()=>{
            var sections=new[]{new StandardEdgeSection(20,47,53),new(21,48,52),new(22,49,51)};var result=Solve(sections);
            Need(result.WidthOnlyErrorLowerBound==1.5&&result.AnyConstantWidthErrorLowerBound==1,"Width lower bound wrong");
            Need(result.SurrogateMaximumEdgeError>=result.WidthOnlyErrorLowerBound,"Translated edges changed width");
        });
        test("Inverse path minimizes the maximum error against independent exhaustive enumeration",()=>{
            var random=new Random(20261020);
            for(int t=0;t<24;t++)
            {
                var rows=Enumerable.Range(20,6).Select(y=>new StandardEdgeSection(y,47+random.NextDouble()*3,51+random.NextDouble()*3)).ToArray();
                var actual=Solve(rows,2);double best=double.PositiveInfinity;
                void Enumerate(int i,int previous,double cost)
                {
                    if(i==rows.Length){best=Math.Min(best,cost);return;}
                    for(int shift=-2;shift<=2;shift++)if(i==0||Math.Abs(shift-previous)<=1)
                        Enumerate(i+1,shift,Math.Max(cost,Math.Max(Math.Abs(rows[i].Low+shift-47.5),Math.Abs(rows[i].High+shift-52.5))));
                }
                Enumerate(0,0,0);Need(actual.SurrogateMaximumEdgeError==best,"DP differs from brute-force optimum");
                Need(actual.ShiftOffsets.Zip(actual.ShiftOffsets.Skip(1)).All(p=>Math.Abs(p.First-p.Second)<=1),"Correction skips a transverse pixel");
            }
        });
        test("Missing end caps never become an executable candidate",()=>{
            var result=StandardEdgeCorrection.Solve(Context(),new(50,10,50,40),"source-a",Sections(50,51,51),47.5,52.5,4);
            Need(result.Status=="INCOMPLETE"&&result.MissingMandatorySections==28&&result.CandidatePoints.Count==0,"Central measurements extrapolated to caps");
            Need(result.ShiftOffsets.Count==3,"Missing caps erased diagnostic observations");
        });
        test("Correction is deterministic immutable contextual and preserves traversal direction",()=>{
            var rows=Sections(50,51,52);var forward=Solve(rows);
            var reverse=StandardEdgeCorrection.Solve(Context(),new(50,22,50,20),"source-a",rows.Reverse().ToArray(),47.5,52.5,4);
            Need(reverse.CandidatePoints.SequenceEqual(forward.CandidatePoints.Reverse()),"Reverse changed spatial correction");
            Need(forward.Id==Solve(rows).Id&&forward.Id!=reverse.Id,"Identity or determinism broken");
            rows[0]=new(20,1,3);Need(forward.Sections[0].Low==47.5,"Caller mutated frozen observations");
        });
        test("Missing duplicate malformed and oversized edge samples fail without interpolation",()=>{
            void Refused(Action action){bool failed=false;try{action();}catch(InvalidDataException){failed=true;}Need(failed,"Invalid correction accepted");}
            Refused(()=>Solve([new(20,double.NaN,51),new(21,49,51)]));
            Refused(()=>Solve([new(20,49,51),new(20,49,51)]));
            Refused(()=>Solve([new(20,49,51),new(22,49,51),new(23,49,51)]));
            Refused(()=>Solve(Sections(50,50),33));
            Refused(()=>StandardEdgeCorrection.Solve(Context(),new(50,20,52,22),"a",Sections(50,50),47,53,4));
        });
        test("Every corrected command stays inside the exact Canvas without clamping",()=>{
            var result=StandardEdgeCorrection.Solve(Context(),new(0,20,0,22),"a",[new(20,3,5),new(21,3,5),new(22,3,5)],-.5,1.5,4);
            Need(result.CandidatePoints.All(p=>p.X>=0)&&result.SurrogateMaximumEdgeError==3.5,"Correction left Canvas or clamped its model");
        });
        test("Corrected measurement requires a new source and matching context with all mandatory rows",()=>{
            var result=Solve(Sections(50,51,52));bool refused=false;
            try{result.CompareMeasured(Context(),"source-a",result.CandidatePoints,Sections(50,50,50));}catch(InvalidDataException){refused=true;}
            Need(refused,"Training pixels reused as corrected validation");
            refused=false;try{result.CompareMeasured(Context() with{Dpi=144},"source-b",result.CandidatePoints,Sections(50,50,50));}catch(InvalidDataException){refused=true;}
            Need(refused,"Changed DPI accepted");
            refused=false;try{result.CompareMeasured(Context(),"source-b",[new(50,20),new(50,21),new(50,22)],Sections(50,50,50));}catch(InvalidDataException){refused=true;}
            Need(refused,"Uncorrected original path accepted as corrected observation");
            var partial=result.CompareMeasured(Context(),"source-b",result.CandidatePoints,Sections(50,50));
            Need(partial.Status=="INCOMPLETE"&&partial.MissingMandatorySections==1,"Missing mandatory corrected section omitted");
            var complete=result.CompareMeasured(Context(),"source-b",result.CandidatePoints,Sections(50,50,50));
            Need(complete.Status=="MEASURED_NOT_QUALIFIED"&&complete.MeasuredMaximumEdgeError==0&&!complete.DispatchAllowed,"Edge observable alone certified paint");
        });
        test("Native edge observable measures both axes in physical coordinates and reports missing rows",()=>{
            var canvas=new ScreenRect(100,200,150,250);var before=new PixelImage(50,50);for(int i=0;i<2500;i++)before.Set(i,new(220,220,220));
            var after=before.Clone();for(int y=5;y<=40;y++)for(int x=23;x<=27;x++)after.Set(y*50+x,new(0,0,0));
            var measured=StandardEdgeMeasurement.Measure(before,after,canvas,new(125,205,125,240),220);
            Need(measured.Section==new StandardEdgeSection(220,122.5,127.5),"Vertical screen edge wrong");
            Need(StandardEdgeMeasurement.Measure(before,before,canvas,new(125,205,125,240),220).MissingReason=="missing_strong_component","Blank sample silently used");
            var horizontal=before.Clone();for(int x=5;x<=40;x++)for(int y=23;y<=27;y++)horizontal.Set(y*50+x,new(0,0,0));
            Need(StandardEdgeMeasurement.Measure(before,horizontal,canvas,new(105,225,140,225),120).Section==new StandardEdgeSection(120,222.5,227.5),"Horizontal screen edge wrong");
        });
    }
}
