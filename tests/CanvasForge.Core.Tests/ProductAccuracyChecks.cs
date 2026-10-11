using CanvasForge.Core;

internal static class ProductAccuracyChecks
{
    private static void Require(bool ok,string message="Product accuracy regression"){if(!ok)throw new Exception(message);}
    public static void Run(Action<string,Action> test)
    {
        test("Edge merge rejects malformed fractional and out-of-range thickness",()=>{
            foreach(var value in new object[]{"invalid","NaN",0,5,2.5})
            {
                var s=Settings.Defaults();s.Set("edge_merge_thickness",value);bool refused=false;
                try{s.Validate();}catch(System.IO.InvalidDataException){refused=true;}Require(refused);
            }
        });
        test("Edge merge is on by default and preserves transparent and thick regions",()=>{
            Require(Settings.Defaults().Bool("edge_merge")&&Settings.Defaults().Int("edge_merge_thickness")==2);
            int[] labels=Enumerable.Repeat(0,30*30).ToArray();for(int y=10;y<20;y++)for(int x=10;x<20;x++)labels[y*30+x]=1;
            labels[2*30+2]=2;labels[0]=-1;var before=(int[])labels.Clone();var r=EdgeRegionMerge.Apply(labels,30,30,2,4);
            Require(r.ChangedPixels==1&&r.MergedRegions==1&&labels[0]==-1);
            Require(Enumerable.Range(0,labels.Length).Where(i=>before[i]==1).All(i=>labels[i]==1));
        });
        test("Edge merge uses frozen decisions and has deterministic bounded passes",()=>{
            int[] original=Enumerable.Range(0,120).Select(i=>i%12<6?0:1).ToArray();original[25]=2;original[26]=3;
            var a=(int[])original.Clone();var b=(int[])original.Clone();var x=EdgeRegionMerge.Apply(a,12,10,2,5);var y=EdgeRegionMerge.Apply(b,12,10,2,5);
            Require(a.SequenceEqual(b)&&x==y&&x.Passes<=3);
        });
        test("Edge thickness distinguishes one and two pixel strips",()=>{
            int[] labels=Enumerable.Repeat(0,20*20).ToArray();for(int y=5;y<15;y++)for(int x=9;x<11;x++)labels[y*20+x]=1;
            var one=(int[])labels.Clone();var two=(int[])labels.Clone();
            Require(EdgeRegionMerge.Apply(one,20,20,1,1).ChangedPixels==0);
            Require(EdgeRegionMerge.Apply(two,20,20,2,1).ChangedPixels==20);
        });
        test("Edge merging can cancel without claiming completed preprocessing",()=>{
            bool canceled=false;try{EdgeRegionMerge.Apply(new int[400],20,20,2,1,new CancellationToken(true));}catch(OperationCanceledException){canceled=true;}
            Require(canceled);
        });
        test("Edge merge removes sub-core AA islands with fewer strokes and fewer unplanned pixels",()=>{
            var s=MeasuredColorChecks.Config(false);s.Set("cell_px",1);s.Set("skin_assist",false);s.Set("edge_preserve",false);s.Set("preblur",0);s.Set("smooth_grid",0);s.Set("min_region_pixels",0);
            s.SetPalette([new(new(20,20,20),new(600,400),"main"),new(new(240,240,240),new(640,400),"main"),new(new(120,120,120),new(680,400),"main")]);
            var image=new PixelImage(400,320);for(int y=0;y<320;y++)for(int x=0;x<400;x++)image.Set(y*400+x,x<200?new(20,20,20):new(240,240,240));
            for(int y=16;y<304;y+=12)for(int x=16;x<384;x+=12)image.Set(y*400+x,new(120,120,120));
            s.Set("edge_merge",false);var old=Planner.Build(image,s);var a=MeasuredColorPlan.TryBuild(old,s)!;
            s.Set("edge_merge",true);var current=Planner.Build(image,s);var b=MeasuredColorPlan.TryBuild(current,s)!;
            Require(current.EdgeMerge is {ChangedPixels:>0}&&b.UnplannedPixels<=a.UnplannedPixels*.7,$"Unplanned {a.UnplannedPixels} -> {b.UnplannedPixels}");
            Require(b.Groups.Values.Sum(g=>g.Count)<a.Groups.Values.Sum(g=>g.Count),"AA merge did not reduce stroke count");
            Require(current.BeforeEdgeMerge!.SequenceEqual(old.Indices));
        });
        test("Edge off strips new identity fields and thickness changes invalidate the new target",()=>{
            var s=Settings.Defaults();s.Set("edge_merge",false);var image=new PixelImage(20,20);var old=PlanIdentity.Compute(image,s,[]);
            s.Set("edge_merge_thickness",4);Require(PlanIdentity.Compute(image,s,[])==old);
            s.Set("edge_merge",true);var on=PlanIdentity.Compute(image,s,[]);Require(on!=old);s.Set("edge_merge_thickness",2);Require(PlanIdentity.Compute(image,s,[])!=on);
        });
        test("Target preprocessing does not invalidate physical brush measurements",()=>{
            var s=MeasuredColorChecks.Config();string context=BrushFootprints.Context(s,3);s.Set("edge_merge",false);s.Set("edge_merge_thickness",4);
            Require(context==BrushFootprints.Context(s,3)&&BrushFootprints.Read(s).Count>0);
        });
        test("Final audit confirms pixels independently of command progress",()=>{
            var before=new PixelImage(32,32);var after=before.Clone();for(int i=0;i<1024;i++){before.Set(i,new(220,220,220));after.Set(i,new(10,10,10));}
            var labels=new int[1024];var r=FinalCoverageAudit.Read(before,after,labels);
            Require(r.Passed&&r.Confirmed==1024&&r.Missing==0&&r.Unknown==0&&r.ConfirmedPercent==100);
        });
        test("Final audit keeps identical white on white unknown without lowering contrast",()=>{
            var white=new PixelImage(32,32);for(int i=0;i<1024;i++)white.Set(i,new(255,255,255));
            var r=FinalCoverageAudit.Read(white,white,new int[1024]);Require(!r.Passed&&r.Unknown==1024&&r.UnknownPercent==100&&r.UnknownMask.All(x=>x));
        });
        test("Final audit distinguishes confirmed missing and unknown masks",()=>{
            var before=new PixelImage(32,32);var after=before.Clone();for(int i=0;i<1024;i++){before.Set(i,new(220,220,220));after.Set(i,new(10,10,10));}
            for(int i=0;i<20;i++)after.Set(i,before.Color(i));for(int i=20;i<30;i++)after.Set(i,new(100,100,100));
            var r=FinalCoverageAudit.Read(before,after,new int[1024]);Require(r.Confirmed==994&&r.Missing==20&&r.Unknown==10&&r.MissingMask.Count(x=>x)==20&&r.UnknownMask.Count(x=>x)==10);
        });
        test("Final audit local references reduce lighting ambiguity with unchanged auditor",()=>{
            var before=new PixelImage(256,128);var after=before.Clone();for(int y=0;y<128;y++)for(int x=0;x<256;x++){before.Set(y*256+x,new(220,220,220));after.Set(y*256+x,x<128?new(10,10,10):new(60,60,60));}
            Require(CoverageAudit.Read(before,after,Enumerable.Repeat(true,32768).ToArray()).Unknown>0);
            Require(FinalCoverageAudit.Read(before,after,new int[32768]).Passed);
        });
        test("Final audit rejects unrelated scene changes and absent baseline",()=>{
            var before=new PixelImage(32,32);var after=before.Clone();int[] labels=Enumerable.Range(0,1024).Select(i=>i<512?0:-1).ToArray();
            for(int i=512;i<600;i++)after.Set(i,new(200,200,200));var r=FinalCoverageAudit.Read(before,after,labels);
            Require(!r.Passed&&r.Unknown==512&&r.UnavailableReason is not null&&r.Missing==0);
            Require(FinalCoverageAudit.Unavailable(labels,"No initial frame").Unknown==512);
        });
        test("Final audit uncertainty overlay does not change the source image",()=>{
            var source=new PixelImage(8,8);bool[] mask=new bool[64];mask[0]=true;var overlay=FinalCoverageAudit.UnknownOverlay(source,mask);
            Require(overlay.Color(0)==new Rgb(255,174,55)&&source.Color(0)!=overlay.Color(0)&&overlay.Color(1)==source.Color(1));
        });
        test("Palette applied proof requires two matching readings within strict RGB 12",()=>{
            var expected=new Rgb(100,100,100);int count=0;var r=PaletteSelection.Applied(expected,new(0,0,8,8),_=>{count++;return new(112,100,100);},()=>{});
            Require(r.Passed&&count==2);Require(!PaletteSelection.Applied(expected,new(0,0,8,8),_=>new(113,100,100),()=>{}).Passed);
        });
        test("Palette applied proof never substitutes source cell for absent active swatch",()=>{
            int reads=0;var r=PaletteSelection.Applied(new(100,100,100),default,_=>{reads++;return new(100,100,100);},()=>{});
            Require(!r.Passed&&reads==0&&r.First is null&&r.Second is null);
        });
        test("Palette applied proof rejects a transient stale color",()=>{
            int reads=0;var r=PaletteSelection.Applied(new(100,100,100),new(0,0,8,8),_=>++reads==1?new(100,100,100):new(180,0,0),()=>{});Require(!r.Passed);
        });
        test("Mode comparison preserves settings and recommends only coverage dominance",()=>{
            var s=MeasuredColorChecks.Config();var plan=MeasuredColorChecks.Plan();string before=s.Data.ToJsonString();var r=PaintingModeComparison.Build(plan,s)!;
            Require(s.Data.ToJsonString()==before&&r.Ordinary.TargetPixels==r.Adaptive.TargetPixels);
            if(r.RecommendedAdaptive is {} mixed){var chosen=mixed?r.Adaptive:r.Ordinary;var other=mixed?r.Ordinary:r.Adaptive;
                Require(chosen.Seconds<=other.Seconds&&!chosen.UnplannedMask.Where((m,i)=>m&&!other.UnplannedMask[i]).Any());}
        });
        test("Changing recommended painting mode does not change the preprocessing threshold",()=>{
            var s=MeasuredColorChecks.Config(false);var dot=new BrushStamp([new(0,0,1)],[new(0,0,1)],new(20,20,20));
            BrushFootprints.Save(s,[BrushFootprints.Build(s,4,3,[dot,dot,dot])]);
            int ordinary=EdgeRegionMerge.MinimumArea(s,400,320);s.Set("adaptive_brush",true);s.Set("adaptive_auto_shape",true);
            Require(ordinary==1&&EdgeRegionMerge.MinimumArea(s,400,320)==ordinary);
        });
        test("ETA full cycle has a fifty-sample window and rejects duplicate observations",()=>{
            var t=new RemainingTime(Enumerable.Range(0,120).Select(i=>new TimedWork($"m{i}","motion",1,true)));
            Require(!t.RecordFullCycle("m0",1,2));for(int i=0;i<70;i++){t.Complete($"m{i}",1);Require(t.RecordFullCycle($"m{i}",1,i<20?2:1.2));Require(!t.RecordFullCycle($"m{i}",1,2));}
            var e=t.Estimate(0);Require(e.FullCycleSamples==50&&Math.Abs(e.FullCycleRatio-1.2)<1e-9&&e.LowerSeconds==e.Seconds*.9&&e.UpperSeconds==e.Seconds*1.15);
        });
        test("ETA warmup reports a conservative upper range and zero at completion",()=>{
            var t=new RemainingTime([new("m","motion",10,true)]);var e=t.Estimate(0);Require(e.UpperSeconds==15&&e.LowerSeconds==9);
            t.Complete("m",12);e=t.Estimate(0);Require(e.Seconds==0&&e.UpperSeconds==0);
        });
        test("ETA successful cycles calibrate unseen route costs without double scaling measured routes",()=>{
            var t=new RemainingTime(Enumerable.Range(0,20).Select(i=>new TimedWork($"m{i}","known",1,true)).Concat([new("unseen","new",2,true),new("known","known",2,true)]));
            for(int i=0;i<20;i++){t.Complete($"m{i}",1.2);t.RecordFullCycle($"m{i}",1,1.2);}Require(Math.Abs(t.Estimate(0).Seconds-4.8)<1e-9);
        });
        test("ETA full-cycle fallback counts checkpoint overhead only once",()=>{
            var t=new RemainingTime(Enumerable.Range(0,20).Select(i=>new TimedWork($"m{i}","known",1,true)).Concat([new("unseen","new",2,true),new("known","known",2,true)]));
            for(int i=0;i<20;i++){t.Complete($"m{i}",1.2);t.RecordOperationOverhead($"m{i}",.1);t.RecordFullCycle($"m{i}",1,1.3);}
            Require(Math.Abs(t.Estimate(0).Seconds-5.1)<1e-9);
        });
        test("ETA simulation with recorded aggregate rates stays within eight percent after warmup",()=>{
            // Aggregate rates from supplied beta.55 BVB logs. This is a
            // simulation, not a replay of unavailable individual stroke times.
            const int count=1000;const double ratio=1.1190143094,overhead=.000191732;
            double[] planned=Enumerable.Range(0,count).Select(i=>.04+(i%7)*.009).ToArray();
            double[] actual=planned.Select((p,i)=>p*ratio*(1+.02*Math.Sin(i))+overhead).ToArray();
            var work=new List<TimedWork>{new("setup","setup",6.8144986,false),new("finish","finish",2.1426249,false)};
            for(int i=0;i<count;i++){if(i%50==0)work.Add(new($"c{i}","color",.472721025,false));work.Add(new($"m{i}","motion",planned[i],true));}
            var t=new RemainingTime(work);t.Complete("setup",6.8144986);
            double remaining=actual.Sum()+20*.472721025+2.1426249;
            for(int i=0;i<count;i++)
            {
                if(i%50==0){t.Complete($"c{i}",.472721025);remaining-=.472721025;}
                t.Complete($"m{i}",actual[i]-overhead);t.RecordOperationOverhead($"m{i}",overhead);t.RecordFullCycle($"m{i}",planned[i],actual[i]);remaining-=actual[i];
                if(i>=19&&remaining>5){var e=t.Estimate(0);Require(Math.Abs(e.Seconds/remaining-1)<=.08&&e.LowerSeconds<=remaining&&e.UpperSeconds>=remaining);}
            }
        });
    }
}
