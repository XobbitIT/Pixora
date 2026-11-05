using CanvasForge.Core;

// F1: offline speed report. Same plan and timing as START/preview, no input.
internal static class SpeedReportChecks
{
    private static void Require(bool value,string problem){if(!value)throw new Exception(problem);}

    internal static PixelImage Fill(){var i=new PixelImage(200,200);for(int k=0;k<i.Width*i.Height;k++)i.Set(k,new(40,120,200));return i;}
    internal static PixelImage Checker(){var i=new PixelImage(200,200);for(int y=0;y<200;y++)for(int x=0;x<200;x++)i.Set(y*200+x,((x/2+y/2)&1)==0?new(0,0,0):new(255,255,255));return i;}
    internal static PixelImage Gradient(){var i=new PixelImage(200,200);for(int y=0;y<200;y++)for(int x=0;x<200;x++)i.Set(y*200+x,new((byte)(x*255/199),(byte)(y*255/199),128));return i;}
    private static Settings Standard(bool hex,string limit)
    {
        var s=FastColorFixtures.Config(hex,limit);s.Set("release_profile","Standard");return s;
    }
    private static Settings WithTransport(Settings s)
    {
        var copy=s.Clone();copy.Set("motion_transport_rate",new MotionTransportRate(100,0.00015,0.09));return copy;
    }

    // Deterministic stroke counts of the current Standard plan (regression table).
    private static readonly Dictionary<string,int> Strokes=new()
    {
        ["fill"]=260,["checker"]=31332,["gradient"]=3676,["anime"]=28068,
    };

    internal static void Table()
    {
        Console.WriteLine("| image | variant | colors | strokes | color changes | ETA, s | dE |");
        Console.WriteLine("|---|---|---:|---:|---:|---:|---:|");
        foreach(var (name,image) in new[]{("fill",Fill()),("checker",Checker()),("gradient",Gradient()),("anime",FastColorFixtures.Anime(200,200))})
        {
            var r=SpeedReport.Build(image,WithTransport(Standard(true,"Auto")));
            Console.WriteLine(FormattableString.Invariant($"| {name} | current | {r.Colors} | {r.Strokes} | {r.ColorChanges} | {r.Seconds:F0} | |"));
            foreach(var v in r.Variants)Console.WriteLine(FormattableString.Invariant($"| {name} | {v.Name} | {v.Colors} | {v.Strokes} | {v.ColorChanges} | {v.Seconds:F0} | {v.DeltaE:F2} |"));
        }
    }

    public static void Run(Action<string,Action> test)
    {
        test("F1 speed report components sum to the preview ETA, with and without measured transport",()=>{
            foreach(var image in new[]{FastColorFixtures.Anime(200,200),Gradient()})
            foreach(bool hex in new[]{true,false})
            foreach(bool measured in new[]{false,true})
            {
                var s=Standard(hex,"Auto");if(measured)s=WithTransport(s);
                var r=SpeedReport.Build(image,s,variants:false);
                var plan=Planner.Build(image,s.Clone());
                double eta=Coverage.EstimateSeconds(plan,s.Clone());
                Require(Math.Abs(r.Breakdown.Sum(p=>p.Seconds)-r.Seconds)<1e-6,"Components do not sum to the report ETA");
                Require(Math.Abs(r.Seconds-eta)<1e-6,$"Report ETA {r.Seconds} differs from the preview ETA {eta}");
                Require(r.Breakdown.All(p=>p.Name!="motion_other"),"Standard strokes were not classified");
                Require(!measured||r.Breakdown.Single(p=>p.Name=="transport_other").Seconds>0,"Measured transport costs are missing");
                Require(new[]{"settle","frame","end_hold","release"}.All(k=>r.Breakdown.Any(p=>p.Name==k&&p.Seconds>0)),"Per-stroke waits are missing");
            }
        });
        test("F1 the current variant is the START plan: same identity, counts and settings untouched",()=>{
            var image=FastColorFixtures.Photo(200,200);var s=Standard(true,"Auto");string before=s.Data.ToJsonString();
            var r=SpeedReport.Build(image,s);var plan=Planner.Build(image,s.Clone());
            var groups=TransferSchedule.Build(plan,DrawingWorkflow.Effective(s.Clone()));
            Require(r.Identity==plan.Identity&&r.Colors==plan.ColorCount&&r.Strokes==groups.Values.Sum(x=>x.Count),"Report plan differs from the START plan");
            Require(s.Data.ToJsonString()==before,"The report mutated the settings");
        });
        test("F1 regression table: deterministic stroke counts and sane what-if variants",()=>{
            foreach(var (name,image) in new[]{("fill",Fill()),("checker",Checker()),("gradient",Gradient()),("anime",FastColorFixtures.Anime(200,200))})
            {
                var a=SpeedReport.Build(image,Standard(true,"Auto"));var b=SpeedReport.Build(image,Standard(true,"Auto"));
                Require(a.Strokes==b.Strokes&&a.Seconds==b.Seconds&&a.Identity==b.Identity,$"{name}: report is not deterministic");
                Require(a.Strokes==Strokes[name],$"{name}: {a.Strokes} strokes, expected {Strokes[name]}");
                var v=a.Variants.ToDictionary(x=>x.Name);
                Require(SpeedReport.ColorLimits.All(l=>v.ContainsKey($"limit {l}"))&&v.ContainsKey("Auto — faster"),$"{name}: color variants missing");
                Require(v["pitch 2"].Strokes<=a.Strokes&&v["pitch 3"].Strokes<=v["pitch 2"].Strokes,$"{name}: pitch variants do not reduce strokes");
                Require(v["pitch 2"].Note.Contains("NOT PHYSICALLY PROVEN")&&v.Values.Single(x=>x.Kind=="join").Note.Contains("UPPER BOUND"),$"{name}: unproven variants are not marked");
                var join=v.Values.Single(x=>x.Kind=="join");
                Require(join.Strokes<=a.Strokes&&join.Seconds<=a.Seconds+1e-9,$"{name}: ideal join is not an upper bound on savings");
            }
        });
        test("F1 ideal join chooses stroke directions optimally and the report keeps hours past one day",()=>{
            var speed=SpeedProfile.Get("Max Speed");
            // A greedy forward start ends L0 at x=10 and misses L1; reversing L0 joins it.
            var (joins,_)=SpeedReport.BestChain([new(0,0,10,0),new(0,1,5,1)],speed);
            Require(joins==1,"Join estimate missed a reachable connection");
            var (zigzag,cost)=SpeedReport.BestChain([new(0,0,10,0),new(10,1,0,1),new(0,2,10,2),new(50,9,60,9)],speed);
            Require(zigzag==2&&cost>0,"Boustrophedon chain or a far stroke is counted wrongly");
            // Mixed fixed costs (Codex round 26): a line, an adjacent line and a far dot.
            var mixed=SpeedReport.BestChain([new(0,0,10,0),new(10,0,20,0),new(100,100,100,100)],[0.100,0.100,0.065],speed);
            Require(mixed.Joins==1&&Math.Abs(mixed.Saved-0.100)<1e-12,$"Join saving uses an average instead of the joined stroke cost: {mixed.Saved}");
            // The saving follows the strokes actually joined, not the stroke count.
            var weighted=SpeedReport.BestChain([new(0,0,10,0),new(0,1,10,1),new(0,2,10,2)],[0.3,0.05,0.2],speed);
            Require(weighted.Joins==2&&Math.Abs(weighted.Saved-(0.25-weighted.Connector))<1e-12,"Weighted join saving is wrong");
            var day=new SpeedReportResult(1,1,"x",1,1,1,1,1,1,25*3600+61,[new("start_delay",25*3600+61)],[]);
            Require(SpeedReport.Markdown(day).Contains("25:01:01"),"Durations of a day or more are truncated");
        });
        test("F1 refuses the texel route instead of reporting an unrelated plan",()=>{
            var s=Standard(false,"Auto");s.Set(TexelExperimentSettings.EnabledKey,true);
            try{SpeedReport.Build(Fill(),s);throw new Exception("Texel route was reported");}catch(InvalidOperationException){}
        });
    }
}
