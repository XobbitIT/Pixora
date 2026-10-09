using CanvasForge.Core;

internal static class BrushSignalChecks
{
    private static void Require(bool value){if(!value)throw new Exception("Brush signal regression");}
    private static Settings Config()
    {
        var s=Settings.Defaults();var c=s.Calibration;c.SetRect("canvas",new(0,0,1200,1200));
        c.SetRect("size_track",new(1300,100,1550,140));c.SetRect("brush_shapes",new(1300,30,1650,70));
        c.SetSession(new(0,0),96,new(1920,1440));s.SetCalibration(c);AdaptiveBrush.Prepare(s);return s;
    }
    private static PixelImage Frame(byte level)
    {var im=new PixelImage(96,96);for(int i=0;i<96*96;i++)im.Set(i,new(level,level,level));return im;}
    private static void Dot(BrushCalibrationBatch batch,int repeat,byte delta=70,int noise=0,int offset=0)
    {
        var before=Frame(140);var background=Frame((byte)(140-noise));var after=before.Clone();
        for(int y=47;y<50;y++)for(int x=47+offset;x<50+offset;x++)after.Set(y*96+x,new((byte)(140-delta),(byte)(140-delta),(byte)(140-delta)));
        batch.Record(1,repeat,background,before,after,new(48,48));
    }
    public static void Run(Action<string,Action> test)
    {
        test("Three repeatable weak dots retain evidence without a paint mask",()=>{
            var s=Config();var batch=new BrushCalibrationBatch(s,3,[1]);
            Dot(batch,1,69,2);Dot(batch,2,70,3);Dot(batch,3,68,1);
            var summary=batch.Diagnostics.Single();Require(summary.State==BrushSignalState.WeakRepeatable&&summary.SpatialAgreement==1);
            Require(batch.Profiles.Count==0&&!batch.ShouldMeasure(1)&&batch.Rejected.Count==1);
            BrushSignalDiagnostics.Save(s,batch.Diagnostics);Require(!SpeedCalibration.BrushReady(s,1)&&!AdaptiveBrush.CalibrationCurrent(s));
            Require(BrushSignalDiagnostics.Read(s).Single().Samples.Select(p=>p.Contrast.PeakDelta).SequenceEqual(new[]{69,70,68}));
        });
        test("Three strong square dots still certify only their own shape",()=>{
            var s=Config();var batch=new BrushCalibrationBatch(s,4,[1]);for(int i=1;i<=3;i++)Dot(batch,i,110);
            Require(batch.Diagnostics.Single().State==BrushSignalState.Verified&&batch.Profiles.Count==1);
            BrushFootprints.Save(s,batch.Profiles);BrushSignalDiagnostics.Save(s,batch.Diagnostics);
            Require(!SpeedCalibration.BrushReady(s,1)&&SpeedCalibration.BrushReady(BrushFootprints.ForShape(s,4),1));
        });
        test("Incomplete mixed and displaced weak dots never certify or claim repeatability",()=>{
            var batch=new BrushCalibrationBatch(Config(),3,[1]);Dot(batch,1);Dot(batch,2);Require(batch.Diagnostics.Count==0&&batch.Profiles.Count==0);
            Dot(batch,3,110);Require(batch.Diagnostics.Single().State==BrushSignalState.Rejected&&batch.Profiles.Count==0);
            batch=new(Config(),3,[1]);Dot(batch,1);Dot(batch,2,70,0,4);Dot(batch,3,70,0,-4);
            Require(batch.Diagnostics.Single().State==BrushSignalState.Rejected&&batch.Diagnostics[0].SpatialAgreement<.75);
        });
        test("Background noise and varying contrast remain rejected even with a visible dot",()=>{
            var noisy=new BrushCalibrationBatch(Config(),3,[1]);for(int i=1;i<=3;i++)Dot(noisy,i,110,20);
            Require(noisy.Profiles.Count==0&&noisy.Diagnostics.Single().State==BrushSignalState.Rejected);
            var varying=new BrushCalibrationBatch(Config(),3,[1]);Dot(varying,1,40);Dot(varying,2,70);Dot(varying,3,50);
            Require(varying.Diagnostics.Single().State==BrushSignalState.Rejected);
            var none=new BrushCalibrationBatch(Config(),3,[1]);for(int i=1;i<=3;i++)Dot(none,i,0);
            Require(none.Diagnostics.Single().State==BrushSignalState.Rejected&&none.Profiles.Count==0);
        });
        test("Clipped or changed scenes still abort before a diagnostic transaction",()=>{
            var batch=new BrushCalibrationBatch(Config(),3,[1]);var before=Frame(140);var after=before.Clone();after.Set(0,new(0,0,0));
            try{batch.Record(1,1,before,before,after,new(48,48));throw new Exception("Clipping accepted");}catch(InvalidOperationException){}
            Require(batch.ShouldMeasure(1)&&batch.Profiles.Count==0&&batch.Diagnostics.Count==0);
            Dot(batch,1);try{Dot(batch,1);throw new Exception("Duplicate attempt accepted");}catch(InvalidOperationException){}
        });
        test("Signal context becomes stale after geometry changes and preserves other shapes",()=>{
            var s=Config();var round=new BrushCalibrationBatch(s,3,[1]);var square=new BrushCalibrationBatch(s,4,[1]);
            for(int i=1;i<=3;i++){Dot(round,i);Dot(square,i);}
            BrushSignalDiagnostics.Save(s,round.Diagnostics);BrushSignalDiagnostics.Save(s,square.Diagnostics);
            Require(BrushSignalDiagnostics.Read(s,true).Count==2&&BrushSignalDiagnostics.Read(s).Count==1);
            var c=s.Calibration;c.SetSession(new(1,0),96,new(1920,1440));s.SetCalibration(c);
            Require(BrushSignalDiagnostics.Read(s,true).All(r=>r.State==BrushSignalState.Stale));
        });
        test("Retry scope leaves successful independent Sizes untouched",()=>{
            var s=Config();BrushSpan[] points=[new(0,0,2),new(1,0,2)];var stamp=new BrushStamp(points,points,new(20,20,20));
            BrushFootprints.Save(s,new[]{3d,10d}.Select(size=>BrushFootprints.Build(s,3,size,[stamp,stamp,stamp])).ToArray());
            Require(BrushSignalDiagnostics.RetrySizes(s,[1,3,10,20]).SequenceEqual(new[]{1d,20d}));
            Require(BrushSignalDiagnostics.RetrySizes(s,[3]).Length==0&&BrushFootprints.Read(s).Count==2);
        });
    }
}
