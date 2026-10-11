using CanvasForge.Core;

internal static class GroupedProbeAndCoverageChecks
{
    private static void Require(bool ok,string message="Grouped probe/coverage regression"){if(!ok)throw new Exception(message);}
    public static void Run(Action<string,Action> test)
    {
        test("Grouped probe selects only current measured Sizes and lists unavailable requests",()=>{
            var s=MeasuredColorChecks.Config();var measured=ProbeBatchSequence.Select(s,ProbeBatchSequence.Measured);
            Require(measured.Ready.SequenceEqual(new[]{3d,10,20})&&measured.Unavailable.Length==0);
            var requested=ProbeBatchSequence.Select(s,SetupBrushSelection.Combined);
            Require(requested.Ready.SequenceEqual(new[]{3d,10,20})&&requested.Unavailable.SequenceEqual(new[]{2d,5,7,15}));
            var c=s.Calibration;c.SetRect("canvas",new(0,0,401,320));s.SetCalibration(c);
            Require(ProbeBatchSequence.Select(s,ProbeBatchSequence.Measured).Ready.Length==0);
        });
        test("Grouped probe reuses spatial proof and clears Canvas between independent protocols",()=>{
            var calls=new List<string>();
            var r=ProbeBatchSequence.Run([3,10],(size,spatial,_)=>{calls.Add($"clean:{size}:{spatial}");return Task.FromResult(true);},size=>size==3,
                (size,spatial,_)=>{calls.Add($"run:{size}:{spatial}");return Task.FromResult(true);},(_,_,_)=>{},default).GetAwaiter().GetResult();
            Require(r==new ProbeBatchResult(2,0,false)&&calls.SequenceEqual(new[]{"clean:3:False","run:3:False","clean:10:True","run:10:True","clean:10:False","run:10:False"}));
        });
        test("Grouped probe isolates a Size failure and preserves successful earlier/later runs",()=>{
            var routes=new List<double>();var states=new List<ProbeBatchState>();
            var r=ProbeBatchSequence.Run([3,10,20],(_,_,_)=>Task.FromResult(true),_=>true,
                (size,_,_)=>{if(size==10)return Task.FromResult(false);routes.Add(size);return Task.FromResult(true);},(_,state,_)=>states.Add(state),default).GetAwaiter().GetResult();
            Require(r==new ProbeBatchResult(2,1,false)&&routes.SequenceEqual(new[]{3d,20})&&states.Count(x=>x==ProbeBatchState.Failed)==1);
        });
        test("Declined clean Canvas stops a grouped probe without starting the next Size",()=>{
            var routes=new List<double>();var r=ProbeBatchSequence.Run([3,10],(size,_,_)=>Task.FromResult(size==3),_=>true,
                (size,_,_)=>{routes.Add(size);return Task.FromResult(true);},(_,_,_)=>{},default).GetAwaiter().GetResult();
            Require(r==new ProbeBatchResult(1,0,true)&&routes.SequenceEqual(new[]{3d}));
        });
        test("Grouped probe cancellation stops before another protocol and invalid requests fail closed",()=>{
            using var cancel=new CancellationTokenSource();int count=0;bool stopped=false;
            try{ProbeBatchSequence.Run([3,10],(_,_,_)=>Task.FromResult(true),_=>true,(_,_,_)=>{count++;cancel.Cancel();return Task.FromResult(true);},(_,_,_)=>{},cancel.Token).GetAwaiter().GetResult();}
            catch(OperationCanceledException){stopped=true;}Require(stopped&&count==1);
            foreach(double[] bad in new[]{Array.Empty<double>(),new[]{3d,3},new[]{4d}})
            {bool invalid=false;try{ProbeBatchSequence.Run(bad,(_,_,_)=>Task.FromResult(true),_=>true,(_,_,_)=>Task.FromResult(true),(_,_,_)=>{},default).GetAwaiter().GetResult();}catch(ArgumentException){invalid=true;}Require(invalid);}
        });
        test("Probe Size group preferences do not invalidate a painting checkpoint",()=>{
            var s=MeasuredColorChecks.Config();var im=new PixelImage(2,2);var first=PlanIdentity.Compute(im,s,s.Palette());
            s.Set("probe_sizes","3/10/20");Require(first==PlanIdentity.Compute(im,s,s.Palette()));
        });
        test("Captured 64-color Rust palette is the only source of RGB and click positions",()=>{
            var s=MeasuredColorChecks.Config(false);s.Set("cell_px",5);s.Set("color_mode","Rust Palette");
            var palette=Enumerable.Range(0,64).Select(i=>new PaletteEntry(new((byte)(i*4),(byte)(255-i*3),(byte)(i*2)),new(600+i%4*20,400+i/4*20),"main")).ToArray();s.SetPalette(palette);
            var im=new PixelImage(16,16);for(int i=0;i<256;i++)im.Set(i,new((byte)i,(byte)(255-i),(byte)(i*17%256)));
            var plan=Planner.Build(im,s);Require(plan.Palette.SequenceEqual(palette)&&plan.Indices.All(i=>i<0||i<64));
            Require(plan.Counts.Keys.All(i=>plan.Palette[i].ClickPoint==palette[i].ClickPoint&&plan.Palette[i].Source!="hex"));
        });
        foreach(int? background in new int?[]{null,0,1})test("Zero-command color groups never schedule palette/input/ETA work, background="+background,()=>{
            var s=MeasuredColorChecks.Config(false);var plan=MeasuredColorChecks.Plan().withBackground(background);
            var batches=new Dictionary<int,List<PaintBatch>>{{0,[]},{1,[new(3,[new(20,20,35,20)],1)]}};
            var order=TransferSchedule.Order(plan,batches);Require(order.SequenceEqual(new[]{1}));
            var work=PaintTimingPlan.Build(s,batches,order);Require(work.Count(w=>w.Id.StartsWith("color:"))==1);
        });
        test("Solid-mask redundancy removal preserves physical model for duplicates and partial overlap",()=>{
            var s=MeasuredColorChecks.Config(false);BrushSpan[] solid=[new(0,0,3),new(1,1,2)],possible=[new(0,-1,4),new(1,0,3)];
            var p=BrushFootprints.Build(s,3,3,Enumerable.Repeat(new BrushStamp(possible,solid,new(20,20,20)),3).ToArray());BrushFootprints.Save(s,[p]);
            var plan=MeasuredColorChecks.Plan((_,_)=>0);
            BrushStroke Stroke(int x1,int x2)=>new(new(x1,30,x2,30),3,p.Reach,0,3,p.Id);
            var groups=new Dictionary<int,List<BrushStroke>>{{0,[Stroke(20,50),Stroke(20,50),Stroke(40,70),Stroke(10,25)]}};
            var source=new MeasuredColorResult(groups,128000,0,new bool[128000]){Diagnostics=new(0,0,0,0)};
            var result=MeasuredStrokeOptimization.Apply(plan,s,source,default);var before=Raster(groups,p);var after=Raster(result.Groups,p);
            Require(before.SequenceEqual(after),"Trimming lost actual solid-mask coverage");
            var d=result.Diagnostics!.StrokeSavings!;Require(d.RemovedStrokes==1&&d.TrimmedStrokes==2&&d.AfterSeconds<=d.BeforeSeconds);
            Require(d.Contributions.Sum(x=>x.AddedSolidPixels)==before.Count(x=>x)&&d.Contributions.All(x=>x.Size==3));
            MeasuredColorChecks.Verify(plan,s,TransferSchedule.Build(plan,s,result.Groups));
        });
        test("Possible-only brush paint never removes a necessary detail command",()=>{
            var s=MeasuredColorChecks.Config(false);BrushSpan[] possible=[new(0,-5,6)],solid=[new(0,0,1)];
            var p=BrushFootprints.Build(s,3,3,Enumerable.Repeat(new BrushStamp(possible,solid,new(20,20,20)),3).ToArray());BrushFootprints.Save(s,[p]);
            var plan=MeasuredColorChecks.Plan((_,_)=>0);var groups=new Dictionary<int,List<BrushStroke>>{{0,[new(new(30,30,30,30),3,p.Reach,0,3,p.Id),new(new(31,30,31,30),3,p.Reach,0,3,p.Id)]}};
            var r=MeasuredStrokeOptimization.Apply(plan,s,new(groups,128000,2,new bool[128000]){Diagnostics=new(0,0,0,0)},default);
            Require(r.Groups[0].Count==2&&r.Diagnostics!.StrokeSavings!.RemovedStrokes==0&&r.Diagnostics.StrokeSavings.Contributions.Single().AddedSolidPixels==2);
        });
    }
    private static bool[] Raster(Dictionary<int,List<BrushStroke>> groups,BrushFootprint p)
    {
        var covered=new bool[128000];foreach(var stroke in groups.Values.SelectMany(g=>g))
            for(int x=stroke.Line.X1;x<=stroke.Line.X2;x++)foreach(var row in p.Solid)for(int dx=row.Left;dx<row.Right;dx++)covered[(stroke.Line.Y1+row.Y)*400+x+dx]=true;
        return covered;
    }
    private static PaintPlan withBackground(this PaintPlan plan,int? background)=>new(){Width=plan.Width,Height=plan.Height,Palette=plan.Palette,Indices=plan.Indices,Strokes=plan.Strokes,Counts=plan.Counts,Identity=plan.Identity,Mode=plan.Mode,Preview=plan.Preview,BackgroundColor=background};
}
