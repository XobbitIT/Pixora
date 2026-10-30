using CanvasForge.Core;

internal static class StandardDiscreteBandChecks
{
    private static void Need(bool x,string message){if(!x)throw new Exception(message);}
    private static void Refuse(Action action){bool refused=false;try{action();}catch(InvalidDataException){refused=true;}Need(refused,"Unmeasured frontier context accepted");}
    private static StandardBandResponse Model(bool descending,bool horizontal=false,bool reverse=false,int missing=-1,int dpi=96)
    {
        var canvas=new ScreenRect(0,0,180,180);var blank=new PixelImage(180,180);for(int i=0;i<180*180;i++)blank.Set(i,new(220,220,220));
        var context=new StandardBandResponseContext(new(canvas,new(0,0),new(2560,1440),dpi,3,3,StandardEvidenceSeries.ProductionSpeed,.02,.02,.02,"Rust Palette"),
            new('a',64),new('b',64),StandardContourMotion.Revision,StandardEvidenceSeries.ProductionSpeed,"fixed per-track captures");
        var target=horizontal?new ScreenLine(20,100,80,100):new(100,20,100,80);if(reverse)target=new(target.X2,target.Y2,target.X1,target.Y1);
        var model=new StandardBandResponse(context,target,new(5,5,150,150),blank);var before=blank;
        var commands=Enumerable.Range(94,13);if(descending)commands=commands.Reverse();int first=commands.First(),index=0;
        foreach(int q in commands)
        {
            int low=2*(Math.Min(first,q)/2)-2,high=2*(Math.Max(first,q)/2)+2;var after=blank.Clone();
            for(int along=20;along<=80;along++)if(!(q==missing&&along==20))for(int cross=low;cross<=high;cross++)
                after.Set(horizontal?cross*180+along:along*180+cross,new(0,0,0));
            var path=Enumerable.Range(20,61).Select(along=>horizontal?new ScreenPoint(along,q):new(q,along)).ToArray();if(reverse)Array.Reverse(path);
            model.Add($"{descending}/{index++}",path,before,after);before=after;
        }
        return model;
    }
    public static void Run(Action<string,Action> test)
    {
        test("Discrete correction uses independently observed low and high frontiers without 1px interpolation",()=>{
            var model=new StandardDiscreteBand(Model(false),Model(true));var a=model.PredictStraight(94,104,14)!;var b=model.PredictStraight(95,104,14)!;var c=model.PredictStraight(96,104,14)!;
            Need(a.Rows.Select(r=>(r.Along,r.Low,r.High)).SequenceEqual(b.Rows.Select(r=>(r.Along,r.Low,r.High))),"Odd command invented an intermediate physical edge");
            Need(c.Rows[0].Low-a.Rows[0].Low==2&&c.Rows[0].High==a.Rows[0].High,"Two-frontier discrete response conflated");
            Need(c.Rows.All(r=>!string.IsNullOrEmpty(r.LowStateId)&&!string.IsNullOrEmpty(r.HighStateId)),"Native frontier provenance omitted");
            var chosen=model.BestStraight(14)!;Need(chosen.Prediction.MaximumTwoEdgeErrorPx==.5,"Known discrete synthetic target not solved");
            Need(!chosen.DispatchAllowed&&!chosen.PhysicalQualityCertified&&!chosen.ProductionEligible&&chosen.EstablishedSolidPixels==0,"Discrete hypothesis became a certified footprint");
        });
        test("Discrete correction preserves complete constant routes endpoints and H V direction",()=>{
            foreach(bool h in new[]{false,true})foreach(bool reverse in new[]{false,true})
            {
                var model=new StandardDiscreteBand(Model(false,h,reverse),Model(true,h,reverse));var chosen=model.BestStraight(14)!;
                foreach(var path in chosen.Paths)
                {
                    Need(path.Count==61&&path.Select(p=>h?p.Y:p.X).Distinct().Count()==1,"Unmeasured warp introduced");
                    Need((h?path[0].X:path[0].Y)==(reverse?80:20)&&(h?path[^1].X:path[^1].Y)==(reverse?20:80),"Endpoints or direction changed");
                    StandardBandContour.Validate(path,model.Context.Geometry.Canvas);
                }
            }
        });
        test("Discrete correction refuses missing mandatory low rows unknown positions context changes and reversed roles",()=>{
            var a=Model(false);var m=new StandardDiscreteBand(a,Model(true,missing:96));
            Need(m.PredictStraight(96,104,14) is null,"Missing low frontier was inferred from the high frontier");
            Need(m.PredictStraight(93,104,14) is null&&m.PredictStraight(94,107,14) is null,"Unknown command was interpolated or extrapolated");
            Refuse(()=>new StandardDiscreteBand(a,Model(true,dpi:144)));Refuse(()=>new StandardDiscreteBand(Model(true),Model(false)));
            Refuse(()=>m.PredictStraight(104,96,14));Refuse(()=>m.PredictStraight(96,104,double.NaN));Refuse(()=>m.BestStraight(0));
        });
        test("Discrete correction freezes native frontier tables candidate paths and width-sensitive identity",()=>{
            var model=new StandardDiscreteBand(Model(false),Model(true));var a=model.BestStraight(14)!;var b=model.BestStraight(14)!;
            Need(a.Id==b.Id&&a.Rows.SequenceEqual(b.Rows),"Discrete correction is nondeterministic");
            Need(model.BestStraight(12)!.Id!=a.Id,"Target width omitted from correction identity");
            Need(model.Commands is not int[]&&model.LowFrontiers is not StandardDiscreteFrontier[]&&a.Rows is not StandardDiscreteRow[]&&a.Paths[0] is not ScreenPoint[],"Mutable caller tables exposed");
        });
    }
}
