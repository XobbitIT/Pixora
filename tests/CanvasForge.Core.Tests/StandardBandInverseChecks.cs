using CanvasForge.Core;

internal static class StandardBandInverseChecks
{
    private static void Need(bool condition,string message){if(!condition)throw new Exception(message);}
    private static void Refuse(Action action){bool refused=false;try{action();}catch(InvalidDataException){refused=true;}Need(refused,"Invalid inverse calibration accepted");}
    private static StandardBandResponse Model(int shift,bool reverse=false,bool horizontal=false,bool missing=false,int dpi=96)
    {
        var blank=new PixelImage(500,500);for(int i=0;i<250000;i++)blank.Set(i,new(220,220,220));
        var context=new StandardBandResponseContext(new(new(0,0,500,500),new(0,0),new(2560,1440),dpi,3,3,
            StandardEvidenceSeries.ProductionSpeed,.02,.02,.02,"Rust Palette"),new('a',64),new('b',64),StandardContourMotion.Revision,StandardEvidenceSeries.Speed,"same frozen capture policy");
        var target=horizontal?new ScreenLine(20,100,80,100):new(100,20,100,80);if(reverse)target=new(target.X2,target.Y2,target.X1,target.Y1);
        var m=new StandardBandResponse(context,target,new(5,5,150,150),blank);var previous=blank;
        for(int k=0;k<10;k++)
        {
            var next=blank.Clone();
            for(int along=20;along<=80;along++)
            {
                if(missing&&along==20)continue;
                int drift=along<45?0:along<60?1:2;
                for(int cross=90+shift+drift;cross<=94+k+shift+drift;cross++)next.Set(horizontal?cross*500+along:along*500+cross,new(0,0,0));
            }
            var path=Enumerable.Range(20,61).Select(along=>horizontal?new ScreenPoint(along,96+k+shift):new(96+k+shift,along)).ToArray();if(reverse)Array.Reverse(path);
            m.Add($"{shift}-{k}",path,previous,next);previous=next;
        }
        return m;
    }
    public static void Run(Action<string,Action> test)
    {
        test("Joint band inverse produces a nonconstant correction for BOTH edges without endpoint ramps",()=>{
            var a=Model(0);var b=Model(4);var x=StandardBandInverse.Build(a,b,14)!;
            Need(x.PrefixLength>=9&&x.Rows.Count==61&&x.Paths.Count==x.PrefixLength,"Incomplete common-warp route");
            Need(x.Prediction.MaximumTwoEdgeErrorPx<=.50000001&&x.Prediction.MaximumAdjacentEdgeStepPx<=1e-9,"Synthetic known response not inverted");
            Need(x.Rows.Select(r=>r.CommandShift).Distinct().Count()==3,"Constant placement substituted for correction");
            Need(x.Rows[0].CommandShift!=0&&x.Rows[^1].CommandShift!=0,"Endpoint correction silently ramped back");
            Need(!x.DispatchAllowed&&!x.ProductionEligible&&!x.PhysicalQualityCertified&&x.EstablishedSolidPixels==0,"Interpolation became physical qualification");
            Need(x.Prediction.MaximumTwoEdgeErrorPx<a.States[^1].Score(a.Target,14).MaximumTwoEdgeErrorPx,"Correction not compared with original placement");
        });
        test("Joint band inverse preserves direction spacing full length and unit connected paths",()=>{
            foreach(bool horizontal in new[]{false,true})foreach(bool reverse in new[]{false,true})
            {
                var x=StandardBandInverse.Build(Model(0,reverse,horizontal),Model(4,reverse,horizontal),14)!;
                foreach(var p in x.Paths)
                {
                    Need(p.Count==61,"Command skipped");StandardBandContour.Validate(p,new(0,0,500,500));
                    Need((horizontal?p[0].X:p[0].Y)==(reverse?80:20)&&(horizontal?p[^1].X:p[^1].Y)==(reverse?20:80),"Reverse or endpoints lost");
                }
                for(int k=1;k<x.Paths.Count;k++)for(int i=0;i<61;i++)
                    Need((horizontal?x.Paths[k][i].Y-x.Paths[k-1][i].Y:x.Paths[k][i].X-x.Paths[k-1][i].X)==1,"Tracks crossed or unmeasured gap invented");
            }
        });
        test("Joint band inverse refuses missing rows context transfer extrapolation and invalid width",()=>{
            Need(StandardBandInverse.Build(Model(0,missing:true),Model(4),14) is null,"Missing first paint row interpolated away");
            var a=Model(0);Refuse(()=>StandardBandInverse.Build(a,Model(4,dpi:144),14));
            Need(StandardBandInverse.Build(a,Model(0),14) is null,"Identical placements invented an inverse calibration");Refuse(()=>StandardBandInverse.Build(a,Model(9),14));
            Refuse(()=>StandardBandInverse.Build(a,Model(4),double.NaN));Refuse(()=>StandardBandInverse.Build(a,Model(4),0));
        });
        test("Joint band inverse freezes deterministic candidate identity and caller cannot mutate paths",()=>{
            var a=Model(0);var b=Model(4);var x=StandardBandInverse.Build(a,b,14)!;var y=StandardBandInverse.Build(a,b,14)!;
            Need(x.Id==y.Id&&x.ContextId==a.Context.Id&&x.Rows.SequenceEqual(y.Rows),"Correction not reproducible");
            Need(StandardBandInverse.Build(a,b,13)!.Id!=x.Id,"Target width absent from identity");
            Need(x.Paths is not ScreenPoint[][]&&x.Paths[0] is not ScreenPoint[]&&x.Rows is not StandardInverseRow[],"Mutable arrays exposed");
        });
    }
}
