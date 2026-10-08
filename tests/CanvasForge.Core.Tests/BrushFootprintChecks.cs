using CanvasForge.Core;
using System.Text.Json.Nodes;

internal static class BrushFootprintChecks
{
    private static void Assert(bool value,string message="Footprint assertion failed")
    {if(!value)throw new Exception(message);}
    private static Settings Config()
    {
        var s=Settings.Defaults();var c=s.Calibration;c.SetRect("canvas",new(0,0,400,320));
        c.SetSession(new(0,0),96,new(1920,1080));c.SetRect("size_track",new(500,200,750,230));
        c.SetRect("brush_shapes",new(500,100,850,140));s.SetCalibration(c);
        s.SetPalette([new(new(220,30,30),new(600,400),"main"),new(new(30,220,30),new(640,400),"main")]);
        AdaptiveBrush.Prepare(s);s.Set("adaptive_brush",true);s.Set("adaptive_max_size",100);
        return s;
    }
    private static BrushSpan[] Rectangle(int l,int t,int r,int b)=>Enumerable.Range(t,b-t).Select(y=>new BrushSpan(y,l,r)).ToArray();
    private static BrushFootprint Profile(Settings s,int shape,double size,BrushSpan[] possible,BrushSpan[] solid)
        =>BrushFootprints.Build(s,shape,size,Enumerable.Repeat(new BrushStamp(possible,solid,new(20,20,20)),3).ToArray());
    public static void Run(Action<string,Action> test)
    {
        test("Physical reach 4 cannot bridge a legacy safety inset 7",()=>
        {
            var expected=new bool[40*40];for(int y=10;y<30;y++)for(int x=5;x<35;x++)expected[y*40+x]=true;
            var missing=new bool[expected.Length];missing[10*40+20]=true;
            var result=CoverageAudit.PlanRepair(missing,expected,new(0,0,40,40),7,physicalReach:4);
            Assert(result.TargetPixels==1&&result.UnreachablePixels==1&&result.Strokes.Count==0);
        });
        test("Repeated masks retain command offsets and union variation instead of recentering",()=>
        {
            var s=Config();var stamps=new List<BrushStamp>();
            for(int repeat=0;repeat<3;repeat++)
            {
                var before=new PixelImage(64,64);for(int i=0;i<4096;i++)before.Set(i,new(180,170,150));
                var after=before.Clone();foreach(var point in BrushFootprints.Points(Rectangle(1+repeat,3,5+repeat,6)))after.Set((32+point.Y)*64+32+point.X,new(20,20,20));
                stamps.Add(BrushFootprints.Measure(before,after,new(32,32),1,stamps.FirstOrDefault()?.Reference));
            }
            var p=BrushFootprints.Build(s,3,1,stamps);
            Assert(p.SafetyBounds==new ScreenRect(1,3,7,6)&&p.SolidCore==new ScreenRect(3,3,5,6)&&p.Reach==6);
            Assert(!BrushFootprints.Points(p.Solid).Contains(new(0,0))&&p.Repeats==3);
        });
        test("A sparse texture has no invented stable core",()=>
        {
            var s=Config();var stamps=Enumerable.Range(0,3).Select(k=>new BrushStamp(Rectangle(k,0,k+1,1),Rectangle(k,0,k+1,1),new(20,20,20))).ToArray();
            var p=BrushFootprints.Build(s,6,10,stamps);Assert(p.Possible.Length==1&&!p.SolidCore.Valid&&p.SolidPixels==0);
        });
        test("Measured possible corners and holes govern safety instead of a bounding square",()=>
        {
            var s=Config();BrushSpan[] cross=[new(-1,0,1),new(0,-1,2),new(1,0,1)];var p=Profile(s,3,1,cross,cross);
            var expected=new bool[25];foreach(var point in BrushFootprints.Points(cross))expected[(point.Y+2)*5+point.X+2]=true;
            Assert(BrushFootprints.Safe(p,2,2,5,5,(x,y)=>expected[y*5+x]));
            expected[2*5+1]=false;Assert(!BrushFootprints.Safe(p,2,2,5,5,(x,y)=>expected[y*5+x]));
        });
        test("Mask repair paints edge targets from asymmetric safe commands",()=>
        {
            var s=Config();var p=Profile(s,3,1,Rectangle(0,3,2,5),Rectangle(0,3,2,5));
            var expected=new bool[40*40];for(int y=10;y<25;y++)for(int x=5;x<30;x++)expected[y*40+x]=true;
            var missing=new bool[expected.Length];for(int x=8;x<24;x++)missing[10*40+x]=true;
            var result=CoverageAudit.PlanRepair(missing,expected,new(0,0,40,40),[p]);
            var painted=new HashSet<ScreenPoint>();
            foreach(var op in result.Operations)for(int x=op.Line.X1;x<=op.Line.X2;x++)foreach(var point in BrushFootprints.Points(p.Possible))
            {var pixel=new ScreenPoint(x+point.X,op.Line.Y1+point.Y);Assert(expected[pixel.Y*40+pixel.X]);painted.Add(pixel);}
            Assert(result.TargetPixels==16&&result.UnreachablePixels==0&&result.PossibleOnlyPixels==0&&result.Operations.Count<16);
            for(int x=8;x<24;x++)Assert(painted.Contains(new(x,10)),"Repair's actual simulated footprint misses its target");
        });
        test("Repair separates possible reach from stable core and never targets Unknown",()=>
        {
            var s=Config();var p=Profile(s,3,1,Rectangle(-1,-1,2,2),[]);
            var expected=Enumerable.Repeat(true,400).ToArray();var gaps=new bool[400];gaps[10*20+10]=true;
            var result=CoverageAudit.PlanRepair(gaps,expected,new(0,0,20,20),[p]);
            Assert(result.TargetPixels==1&&result.PossibleOnlyPixels==1&&result.UnreachablePixels==0&&result.Strokes.Count==0);
            Array.Clear(gaps);Assert(CoverageAudit.PlanRepair(gaps,expected,new(0,0,20,20),[p]).TargetPixels==0);
        });
        test("Footprint persistence rejects tampering and does not fall back to stale padded radii",()=>
        {
            var s=Config();var p=Profile(s,3,1,Rectangle(1,2,4,5),Rectangle(1,2,4,5));BrushFootprints.Save(s,[p]);
            Assert(AdaptiveBrush.CalibrationCurrent(s)&&BrushFootprints.Read(s).Count==1);
            var edited=s.Clone();Assert(BrushFootprints.Read(edited).Count==1);
            edited.Data["brush_footprints"]![0]!["SolidCore"]!["Right"]=999;
            Assert(BrushFootprints.Read(edited).Count==0,"In-place config edits bypassed profile validation");
            var changed=s.Clone();changed.Set("brush_footprints",new[]{p with{SolidCore=new(0,0,99,99)}});Assert(BrushFootprints.Read(changed).Count==0);
            changed=s.Clone();var c=changed.Calibration;c.SetSession(new(1,0),96,new(1920,1080));changed.SetCalibration(c);
            changed.Set("brush_calibration_points",new double[][]{[1,9,1],[3,15,1]});changed.Set("brush_calibration_context",AdaptiveBrush.Context(changed));
            Assert(!AdaptiveBrush.CalibrationCurrent(changed),"Moved masks were replaced with legacy safety radii");
            changed=s.Clone();changed.Set("brush_shape_slot",4);Assert(!AdaptiveBrush.CalibrationCurrent(changed));
        });
        test("All seven shape profiles coexist and get independent speed contexts",()=>
        {
            var s=Config();var profiles=Enumerable.Range(1,7).Select(shape=>Profile(s,shape,1,Rectangle(-1,-1,2,2),Rectangle(-1,-1,2,2))).ToArray();
            BrushFootprints.Save(s,profiles);Assert(BrushFootprints.Read(s,true).Count==7&&BrushFootprints.Read(s).Count==1);
            var other=BrushFootprints.ForShape(s,4);Assert(BrushFootprints.Find(other,1)!.ShapeSlot==4&&SpeedCalibration.Context(other)!=SpeedCalibration.Context(s));
            Assert(SpeedCalibration.Resolve(s,1,new(10,10,100,10),4) is null);
            BrushFootprints.Save(s,[Profile(s,3,100,Rectangle(-80,-80,81,81),Rectangle(-10,-10,11,11)),
                Profile(s,4,100,Rectangle(-180,-180,181,181),Rectangle(-10,-10,11,11))]);
            Assert(BrushFootprints.CaptureRadius(s,100,3)==80&&BrushFootprints.CaptureRadius(s,100,4)==180,
                "Snapshot used the default shape's radius after an automatic switch");
            Assert(BrushFootprints.CaptureRadius(s,100,7)==258,"Unmeasured alternative shape inherited default radius");
        });
        test("A verified shape route is available only with its own spatial ID and speed evidence",()=>
        {
            var s=Config();s.Set("calibrated_strokes",true);var pixels=Rectangle(-1,-1,2,2);
            BrushFootprints.Save(s,[Profile(s,3,1,pixels,pixels),Profile(s,4,1,pixels,pixels)]);
            SpatialProbeProfile Spatial(Settings cfg)
            {
                var fp=SpeedCalibration.Footprint(cfg,1);var tiles=ProbeSpatialCalibration.Tiles(cfg.Calibration.Rect("canvas"),fp.Outer);
                return new(Guid.NewGuid().ToString("N"),ProbeSpatialCalibration.Context(cfg),DateTimeOffset.UtcNow,1,fp.Outer,
                    [new(false,fp.Inner,tiles.Take(3).Select(t=>new SpatialAnchor(t.Horizontal,0,new(20,20,20),12)).ToList()),
                     new(true,fp.Inner,tiles.Skip(3).Take(3).Select(t=>new SpatialAnchor(t.Vertical,0,new(20,20,20),12)).ToList())]);
            }
            var spatial=Spatial(s);ProbeSpatialCalibration.Save(s,spatial);
            var proof=new SpeedProbeProfile(SpeedCalibration.Context(s),DateTimeOffset.UtcNow,[new(1,StrokeMethod.Paced,false,8,12,1,40,3,1,spatial.Id)]);
            s.Set("shape_speed_profiles",new Dictionary<string,SpeedProbeProfile>{{"3",proof}});
            Assert(SpeedCalibration.Use(s)&&SpeedCalibration.Resolve(s,1,new(20,20,100,20),3) is not null);
            Assert(SpeedCalibration.Resolve(s,1,new(20,20,100,20),4) is null);
            var other=BrushFootprints.ForShape(s,4);var own=Spatial(other);ProbeSpatialCalibration.Save(other,own);
            s.Data["shape_spatial_profiles"]=other.Data["shape_spatial_profiles"]!.DeepClone();
            var ownProof=new SpeedProbeProfile(SpeedCalibration.Context(other),DateTimeOffset.UtcNow,[new(1,StrokeMethod.Shift,false,8,12,1,40,3,1,own.Id)]);
            s.Set("shape_speed_profiles",new Dictionary<string,SpeedProbeProfile>{{"3",proof},{"4",ownProof}});
            Assert(SpeedCalibration.Resolve(s,1,new(20,20,100,20),4)?.Method==StrokeMethod.Shift);
            var profile=BrushFootprints.Find(s,1,4)!;
            var replacement=Profile(s,4,1,Rectangle(-1,-1,3,2),pixels);BrushFootprints.Save(s,[replacement]);
            Assert(replacement.Id!=profile.Id&&SpeedCalibration.Resolve(s,1,new(20,20,100,20),4) is null);
            Assert(SpeedCalibration.Resolve(s,1,new(20,20,100,20),3) is not null,"Changing another shape invalidated this shape's proof");
        });
        test("Large calibration uses separate nonoverlapping triples and fails before input if Canvas is small",()=>
        {
            foreach(double size in new[]{40d,60,100})
            {
                var tiles=BrushFootprints.Tiles(new(100,200,1149,1246),[size]);Assert(tiles.Count==3);
                for(int i=0;i<tiles.Count;i++)for(int j=i+1;j<tiles.Count;j++)
                {var a=tiles[i].Area;var b=tiles[j].Area;Assert(a.Right<=b.Left||b.Right<=a.Left||a.Bottom<=b.Top||b.Bottom<=a.Top);}
            }
            try{BrushFootprints.Tiles(new(0,0,300,300),[100]);throw new Exception("Clipped calibration accepted");}catch(InvalidOperationException){}
            var cfg=Config();foreach(double size in BrushFootprints.Sizes){cfg.Set("probe_size",size);cfg.Validate();Assert(AdaptiveBrush.CalibrationSettings(cfg,size).Number("brush_size_value")==size);}
        });
        test("Measured adaptive planning chooses profitable shapes and reports centres without solid coverage",()=>
        {
            var s=Config();s.Set("adaptive_auto_shape",true);s.Set("fast_transfer",false);
            var small=Profile(s,3,1,Rectangle(0,2,2,4),Rectangle(0,2,2,4));
            var large=Profile(s,4,40,Rectangle(-22,-22,23,23),Rectangle(-20,-20,21,21));BrushFootprints.Save(s,[small,large]);
            var indices=new int[400*320];for(int y=0;y<320;y++)for(int x=200;x<400;x++)indices[y*400+x]=1;
            var strokes=new Dictionary<int,List<Stroke>>{{0,Enumerable.Range(0,320).Select(y=>new Stroke(0,0,y,199,y)).ToList()},
                {1,Enumerable.Range(0,320).Select(y=>new Stroke(1,200,y,399,y)).ToList()}};
            var plan=new PaintPlan{Width=400,Height=320,Palette=s.Palette().ToArray(),Indices=indices,Strokes=strokes,Counts=new(){{0,64000},{1,64000}},Identity="mask-test",Mode=ColorMode.RustPalette,Preview=new PixelImage(400,320)};
            var baseline=Coverage.Build(plan,s);var result=MeasuredColorPlan.TryBuild(plan,s)!;var ops=AdaptiveBrush.Build(plan,s);Assert(ops.Values.SelectMany(x=>x).Any(p=>p.ShapeSlot==4&&p.Size==40));
            foreach(var (color,lines) in ops)
            {
                var physical=new HashSet<ScreenPoint>();
                foreach(var op in lines)
                {
                    int dx=Math.Sign(op.Line.X2-op.Line.X1),dy=Math.Sign(op.Line.Y2-op.Line.Y1);
                    var profile=op.ProfileId is null?null:BrushFootprints.Find(s,op.Size,op.ShapeSlot);
                    for(int k=0;k<=TransferSchedule.Length(op.Line);k++)
                    {
                        int x=op.Line.X1+k*dx,y=op.Line.Y1+k*dy;
                        if(profile is null){physical.Add(new(x,y));continue;}
                        foreach(var pixel in BrushFootprints.Points(profile.Possible))Assert(indices[(y+pixel.Y)*400+x+pixel.X]==color,"Possible mask spills into another color");
                        foreach(var pixel in BrushFootprints.Points(profile.Solid))physical.Add(new(x+pixel.X,y+pixel.Y));
                    }
                }
                foreach(var line in baseline[color])for(int k=0;k<=TransferSchedule.Length(line);k++)
                {
                    int x=line.X1+k*Math.Sign(line.X2-line.X1),y=line.Y1+k*Math.Sign(line.Y2-line.Y1);
                    Assert(physical.Contains(new(x,y))==!result.UnplannedMask[y*400+x],"Reported guarantee differs from the actual solid sweep");
                }
            }
            var groups=TransferSchedule.Build(plan,s,ops);var timing=PaintTimingPlan.Build(s,groups,TransferSchedule.Order(plan,groups));
            Assert(timing.Any(p=>p.RateKey=="brush_shape")&&timing.Count(p=>p.RateKey=="color")==2);
        });
        test("Transfer never joins strokes across different shapes or measurement IDs",()=>
        {
            var s=Config();s.Set("fast_transfer",true);s.Set("adaptive_brush",false);
            var plan=Planner.Build(new PixelImage(8,8),s);
            var source=new Dictionary<int,List<BrushStroke>>{{0,[new(new(10,10,20,10),3,ShapeSlot:3),new(new(20,10,30,10),3,ShapeSlot:4)]}};
            var groups=TransferSchedule.Build(plan,s,source);Assert(groups[0].Count==2&&groups[0][1].ShapeSlot==4);
        });
        test("Adaptive planning and transfer honor cancellation before expensive coverage work",()=>
        {
            var s=Config();s.Set("adaptive_brush",false);var plan=Planner.Build(new PixelImage(8,8),s);using var cancel=new CancellationTokenSource();cancel.Cancel();
            foreach(Action work in new Action[]{()=>AdaptiveBrush.Build(plan,s,cancel.Token),()=>TransferSchedule.Build(plan,s,cancel.Token)})
                try{work();throw new Exception("Cancellation ignored");}catch(OperationCanceledException){}
        });
    }
}
