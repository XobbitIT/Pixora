using CanvasForge.Core;

internal static class LocalProbeChecks
{
    private static void Require(bool value) { if(!value)throw new Exception("Local probe regression"); }
    private static (PixelImage Before,PixelImage After,ScreenLine Line) Fixture(byte color=60,int offset=0)
    {
        var before=new PixelImage(96,96);for(int i=0;i<96*96;i++)before.Set(i,new(200,200,200));
        var after=before.Clone();var line=new ScreenLine(30,48,65,48);
        for(int x=30;x<=65;x++)after.Set((48+offset)*96+x,new(color,color,color));
        return(before,after,line);
    }
    private static SpatialAxis Axis()=>new(false,0,Enumerable.Range(0,3).Select(i=>new SpatialAnchor(new(20,20+i*10,60,20+i*10),0,new(20,20,20),12)).ToList());
    public static void Run(Action<string,Action> test)
    {
        test("Paired probe lanes preserve separate physical envelopes",()=>{
            var tiles=SpeedCalibration.Tiles(new(0,0,1200,1200),24);Require(tiles.Count==62);
            foreach(var t in tiles){Require(t.Horizontal.Y1-t.ControlHorizontal.Y1>48&&t.Vertical.X1-t.ControlVertical.X1>48);
                Require(t.ControlHorizontal.Y1-24>=t.Area.Top&&t.Horizontal.Y1+24<t.Area.Bottom);
                Require(t.ControlVertical.X1-24>=t.Area.Left&&t.Vertical.X1+24<t.Area.Right);}
        });
        test("Paired probe rejects area that fits only single lanes",()=>{
            try{SpeedCalibration.Tiles(new(0,0,1000,1000),24);throw new Exception("Accepted insufficient area");}catch(InvalidOperationException){}
        });
        test("Independent slow color validates matching trial but rejects wrong color and gaps",()=>{
            var f=Fixture();var axis=Axis();var slow=ProbeAnalysis.SpatialControl(f.Before,f.After,f.Line,7,0);
            var local=ProbeSpatialCalibration.Bind(axis,slow);Require(local.Color==new Rgb(60,60,60));
            Require(ProbeSpatialCalibration.Trial(f.Before,f.After,f.Line,7,axis,local).Passed);
            Require(!ProbeSpatialCalibration.Trial(f.Before,f.After,f.Line,7,axis,new(new(20,20,20),12)).Passed);
            var wrong=Fixture(95);Require(!ProbeSpatialCalibration.Trial(wrong.Before,wrong.After,wrong.Line,7,axis,local).Passed);
            f.After.Set(48*96+46,new(200,200,200));Require(!ProbeSpatialCalibration.Trial(f.Before,f.After,f.Line,7,axis,local).Passed);
            Require(local.Color==new Rgb(60,60,60));
        });
        test("Local slow control cannot expand frozen offsets",()=>{
            var f=Fixture(60,2);var slow=ProbeAnalysis.SpatialControl(f.Before,f.After,f.Line,7,0);Require(slow.Passed);
            try{ProbeSpatialCalibration.Bind(Axis(),slow);throw new Exception("Expanded envelope");}catch(InvalidOperationException){}
            Require(Axis().AllowedOffsets.SequenceEqual(new[]{0}));
        });
        test("Contrast trace explains color rejection without granting PASS",()=>{
            var f=Fixture(70);var result=ProbeSpatialCalibration.Trial(f.Before,f.After,f.Line,7,Axis(),new(new(20,20,20),12));
            var g=result.Spatial!.Geometry!;Require(!result.Passed&&g.ObservedSlices==g.Slices&&g.ColorRejectedSlices==g.Slices);
            Require(g.ObservedRgb==new Rgb(70,70,70)&&g.MedianRgbDelta==50);
        });
        test("Blank shifted and unstable probes retain distinct diagnostics",()=>{
            var f=Fixture();var reference=new AuditReference(new(60,60,60),12);
            var blank=ProbeSpatialCalibration.Trial(f.Before,f.Before.Clone(),f.Line,7,Axis(),reference);
            Require(blank.Spatial!.Geometry!.NoChangeSlices==blank.Spatial.Slices);
            var shifted=Fixture(60,2);var r=ProbeSpatialCalibration.Trial(shifted.Before,shifted.After,shifted.Line,7,Axis(),reference);
            Require(!r.Passed&&r.Spatial!.Geometry!.OutsideEnvelopeSlices==r.Spatial.Slices);
            for(int i=0;i<200;i++)f.After.Set(i,new(0,0,0));
            var unstable=ProbeSpatialCalibration.Trial(f.Before,f.After,f.Line,7,Axis(),reference);
            Require(unstable.Failure==ProbeFailure.SceneChanged&&unstable.Spatial!.Geometry is null);
        });
        test("Snapshot may stay parked until explicit guarded completion",()=>{
            var original=new ScreenPoint(50,50);var park=new ScreenPoint(10,10);var moves=new List<ScreenPoint>();int releases=0;
            Require(CaptureCursor.Snapshot(original,park,()=>releases++,moves.Add,()=>42,()=>true,returnToOriginal:false)==42);
            Require(releases==1&&moves.SequenceEqual(new[]{park}));
            Require(!CaptureCursor.Return(original,()=>releases++,moves.Add,()=>false)&&moves.Count==1);
            Require(CaptureCursor.Return(original,()=>releases++,moves.Add,()=>true)&&moves[^1]==original&&releases==2);
        });
        test("Weak brush dot preserves measured failure and required contrast",()=>{
            var before=new PixelImage(16,16);for(int i=0;i<256;i++)before.Set(i,new(142,136,125));var after=before.Clone();after.Set(8*16+8,new(86,82,75));
            try{BrushFootprints.Measure(before,after,new(8,8),1);throw new Exception("Weak dot accepted");}
            catch(BrushContrastException e){Require(e.Metrics.PeakDelta==56&&e.Metrics.RequiredDelta==80&&e.Metrics.ChangedPixels==1);}
        });
    }
}
