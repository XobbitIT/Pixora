using System.Text.Json;
using CanvasForge.Core;

internal static class CombinedBrushPlanChecks
{
    private sealed record Mask(double Size,int ShapeSlot,BrushSpan[] Possible,BrushSpan[] Solid);
    private static void Require(bool value,string reason="Combined brush regression"){if(!value)throw new Exception(reason);}
    private static Settings LiveMasks(bool fast)
    {
        var s=MeasuredColorChecks.Config();s.Data.Remove("brush_footprints");s.Set("fast_transfer",fast);
        var masks=JsonSerializer.Deserialize<Mask[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","beta54-size3-10-20-masks.json")))!;
        BrushFootprints.Save(s,masks.Select(p=>BrushFootprints.Build(s,p.ShapeSlot,p.Size,
            Enumerable.Repeat(new BrushStamp(p.Possible,p.Solid,new(20,20,20)),3).ToArray())).ToArray());return s;
    }
    private static double Cost(PaintPlan plan,Settings s,Dictionary<int,List<BrushStroke>> groups)
    {
        var schedule=TransferSchedule.Build(plan,s,groups);
        return PaintTimingPlan.Build(s,schedule,TransferSchedule.Order(plan,schedule)).Sum(w=>w.PlannedSeconds);
    }
    public static void Run(Action<string,Action> test)
    {
        test("Combined calibration includes intermediate Sizes without requiring Size 1",()=>{
            Require(SetupBrushSelection.Sizes(SetupBrushSelection.Combined,true).SequenceEqual(new double[]{2,3,5,7,10,15,20}));
            Require(SetupBrushSelection.AutomaticSelection("3",3)==SetupBrushSelection.Combined);
            Require(SetupBrushSelection.AutomaticSelection("3/10/20",3)=="3/10/20");
            Require(SetupBrushSelection.AutomaticSelection("60",60)=="60");
            Require(SetupBrushSelection.SizeOptions.Contains("30")&&SetupBrushSelection.SizeOptions.Contains("100"));
            Require(SetupSequence.DrawingSize(Settings.Defaults())==3);
            foreach(double size in BrushFootprints.Sizes){var s=Settings.Defaults();s.Set("probe_size",size);s.Set("precision_brush_size",size);s.Validate();}
        });
        test("Combined selection rejects malformed duplicate and unsupported values",()=>{
            foreach(var text in new[]{"","3/","3/3","4/10","NaN","3/Infinity","3//10"})
            {bool rejected=false;try{SetupBrushSelection.Sizes(text,false);}catch(ArgumentException){rejected=true;}Require(rejected,text);}
        });
        test("Every combined Size gets three disjoint clean areas",()=>{
            var sizes=SetupBrushSelection.Sizes(SetupBrushSelection.Combined,false);
            var tiles=BrushFootprints.Tiles(new(0,0,900,900),sizes);Require(tiles.Count==21);
            foreach(double size in sizes)Require(tiles.Where(t=>t.Size==size).Select(t=>t.Repeat).SequenceEqual(new[]{0,1,2}));
            for(int i=0;i<tiles.Count;i++)for(int j=i+1;j<tiles.Count;j++)
            {var a=tiles[i].Area;var b=tiles[j].Area;Require(a.Right<=b.Left||b.Right<=a.Left||a.Bottom<=b.Top||b.Bottom<=a.Top);}
            bool failed=false;try{BrushFootprints.Tiles(new(0,0,250,250),sizes);}catch(InvalidOperationException){failed=true;}Require(failed);
        });
        test("Intermediate Sizes preserve triple measurement and isolated failures",()=>{
            var s=MeasuredColorChecks.Config();var sizes=SetupBrushSelection.Sizes(SetupBrushSelection.Combined,false);
            var batch=new BrushCalibrationBatch(s,3,sizes);BrushSpan[] dot=[new(0,0,2),new(1,0,2)];
            foreach(double size in sizes)for(int repeat=1;repeat<=3;repeat++)
                if(size==7)batch.Reject(size,repeat,new(new(70,4,80,new(140,140,140),new(70,70,70))));
                else batch.Add(size,repeat,new(dot,dot,new(20,20,20)));
            Require(batch.Profiles.Count==6&&batch.Profiles.All(p=>p.Repeats==3&&p.Size!=7)&&batch.Rejected.Single().Size==7);
            BrushFootprints.Save(s,batch.Profiles);Require(SpeedCalibration.BrushReady(s,2)&&SpeedCalibration.BrushReady(s,15)&&!SpeedCalibration.BrushReady(s,7));
        });
        test("All supported Sizes coexist for seven shapes without truncating measured proof",()=>{
            var s=MeasuredColorChecks.Config();BrushSpan[] dot=[new(0,0,2),new(1,0,2)];var stamp=new BrushStamp(dot,dot,new(20,20,20));
            BrushFootprints.Save(s,Enumerable.Range(1,7).SelectMany(shape=>BrushFootprints.Sizes.Select(size=>BrushFootprints.Build(s,shape,size,[stamp,stamp,stamp]))).ToArray());
            Require(BrushFootprints.Read(s,true).Count==7*BrushFootprints.Sizes.Length);
        });
        foreach(bool blocked in new[]{false,true})test("Measured corner connectors stay in one color: blocked="+blocked,()=>{
            var s=MeasuredColorChecks.Config();s.Set("fast_transfer",true);BrushSpan[] dot=[new(0,0,2),new(1,0,2)];var stamp=new BrushStamp(dot,dot,new(20,20,20));
            var p=BrushFootprints.Build(s,3,3,[stamp,stamp,stamp]);BrushFootprints.Save(s,[p]);
            var plan=MeasuredColorChecks.Plan((x,y)=>blocked&&(x==26&&y==20||x==20&&y==26)?1:0);
            var commands=new Dictionary<int,List<BrushStroke>>{{0,[new(new(20,20,20,20),3,p.Reach,0,3,p.Id),new(new(26,26,26,26),3,p.Reach,0,3,p.Id)]}};
            var batches=TransferSchedule.Build(plan,s,commands);Require(batches[0].Count==(blocked?2:1));
            MeasuredColorChecks.Verify(plan,s,batches);
        });
        test("A certified first stroke never joins an ordinary fallback stroke",()=>{
            var s=StartPreparationChecks.VerifiedSize3();s.Set("fast_transfer",true);s.Set("input_frame_delay_ms",100);
            var profile=SpeedCalibration.Read(s)!;s.Set("speed_probe_profile",profile with{Samples=[new(3,StrokeMethod.Shift,false,8,12,1,15,3,1,profile.Samples[0].SpatialId)]});
            var first=new ScreenLine(10,10,25,10);var next=new ScreenLine(25,10,200,10);
            Require(SpeedCalibration.Resolve(s,3,first) is not null&&SpeedCalibration.Resolve(s,3,next) is null);
            var p=BrushFootprints.Find(s,3)!;
            var plan=new PaintPlan{Width=1,Height=1,Palette=[new(new(220,30,30),null,"main")],Indices=[0],Strokes=new(){{0,[]}},Counts=new(){{0,1}},Identity="mixed-routes",Mode=ColorMode.RustPalette,Preview=new(1,1)};
            var batches=TransferSchedule.Build(plan,s,new Dictionary<int,List<BrushStroke>>{{0,[new(first,3,p.Reach,0,3,p.Id),new(next,3,p.Reach,0,3,p.Id)]}});
            Require(batches[0].Count==2&&!new StrokeExecutionPlan(s).FastBatch(batches[0][0])&&new StrokeExecutionPlan(s).FastBatch(batches[0][1]));
        });
        foreach(bool fast in new[]{false,true})foreach(string pattern in new[]{"blocks","curves","islands"})
        test($"Cost selection preserves union coverage and safe colors with live beta54 masks: {pattern}, fast={fast}",()=>{
            var s=LiveMasks(fast);var plan=MeasuredColorChecks.Plan((x,y)=>pattern switch{
                "blocks"=>x<200?0:1,"curves"=>(x-200)*(x-200)+(y-160)*(y-160)<100*100?1:0,_=>(x/41+y/33)%2});
            var raw=MeasuredColorPlan.BuildGeometry(plan,s)!;
            var ordinary=s.Clone();ordinary.Set("adaptive_brush",false);var single=MeasuredColorPlan.BuildGeometry(plan,ordinary)!;
            var chosen=MeasuredColorPlan.TryBuild(plan,s)!;var choice=chosen.Diagnostics!.Choice!;
            Require(chosen.UnplannedMask.Zip(raw.UnplannedMask.Zip(single.UnplannedMask)).All(t=>t.First==(t.Second.First&&t.Second.Second)),"Coverage was discarded");
            Require(chosen.CoveredPixels>=Math.Max(raw.CoveredPixels,single.CoveredPixels));
            Require(choice.SelectedSeconds<=Math.Min(choice.SingleUnionSeconds,choice.MixedUnionSeconds)+1e-9);
            Require(Math.Abs(Cost(plan,s,chosen.Groups)-choice.SelectedSeconds)<1e-9,"Dispatch and choice costs disagree");
            // Independently rasterize actual solid spans at every command.
            var paint=new bool[128000];
            foreach(var stroke in chosen.Groups.Values.SelectMany(g=>g))
            {
                var p=BrushFootprints.Find(s,stroke.Size,stroke.ShapeSlot)!;
                for(int x=stroke.Line.X1;x<=stroke.Line.X2;x++)foreach(var row in p.Solid)
                    for(int px=row.Left;px<row.Right;px++)paint[(stroke.Line.Y1+row.Y)*400+x+px]=true;
            }
            Require(paint.Zip(chosen.UnplannedMask).All(t=>t.First!=t.Second),"Model differs from solid paint");
            var savings=chosen.Diagnostics!.StrokeSavings!;
            Require(savings.Contributions.Sum(p=>p.AddedSolidPixels)==chosen.CoveredPixels&&savings.AfterSeconds<=savings.BeforeSeconds+1e-9,
                "Coverage contributions or dispatch savings disagree with independently rasterized commands");
            MeasuredColorChecks.Verify(plan,s,TransferSchedule.Build(plan,s,chosen.Groups));
            Require(JsonSerializer.Serialize(chosen.Groups)==JsonSerializer.Serialize(MeasuredColorPlan.TryBuild(plan,s)!.Groups),"Nondeterministic choice");
            Console.WriteLine("COMBINED_PLAN_BENCHMARK "+JsonSerializer.Serialize(new{pattern,fast,choice,scope="offline replay of measured masks, not an in-game speed claim"}));
        });
        test("A tiny certified Shift span cannot slow down a long ordinary line",()=>{
            var s=StartPreparationChecks.VerifiedSize3();s.Set("fast_transfer",false);var profile=SpeedCalibration.Read(s)!;var id=profile.Samples[0].SpatialId;
            s.Set("speed_probe_profile",profile with{Samples=[new(3,StrokeMethod.Shift,false,8,12,1,15,3,1,id)]});
            var line=new ScreenLine(10,10,228,10);var batch=new PaintBatch(3,[line],1);
            Require(SpeedCalibration.Current(s)&&CalibratedMotion.Estimate(line,SpeedCalibration.Read(s)!.Samples[0])>1);
            Require(SpeedCalibration.Resolve(s,3,line) is null&&!new StrokeExecutionPlan(s).Fast);
            Require(new StrokeExecutionPlan(s).Estimate(batch)==StrokeTiming.Estimate(s,SpeedProfile.Get(s.Text("speed_profile")),218,false,false));
            s.Set("input_frame_delay_ms",100);Require(SpeedCalibration.Resolve(s,3,new(10,10,25,10)) is not null);
            Require(SpeedCalibration.Read(s)!.Samples[0].MaxLength==15&&SpeedCalibration.Read(s)!.Samples[0].SafeMs==12);
        });
    }
}
