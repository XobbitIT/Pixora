using System.IO.Compression;
using CanvasForge.Core;

internal static class MovingEnvelopeChecks
{
    private static void Require(bool value){if(!value)throw new Exception("Moving stroke envelope regression");}
    private static (PixelImage Before,PixelImage After,ScreenLine Line) Fixture()
    {
        var before=new PixelImage(64,64);for(int i=0;i<64*64;i++)before.Set(i,new(160,160,160));
        var line=new ScreenLine(12,32,51,32);var after=before.Clone();
        for(int y=33;y<=39;y++)for(int x=12;x<=51;x++)after.Set(y*64+x,y==39?new(140,140,140):new(20,20,20));
        return(before,after,line);
    }
    private static SpatialAxis Axis(ProbeAnalysisResult measured)=>new(false,0,Enumerable.Range(0,3)
        .Select(i=>new SpatialAnchor(new(12,20+i*20,51,20+i*20),measured.PerpendicularOffset,new(20,20,20),12)).ToList(),measured.SceneRadius);
    private static PixelImage Load(string name)
    {
        using var stream=new GZipStream(File.OpenRead(Path.Combine(AppContext.BaseDirectory,"Fixtures",name+".rgba.gz")),CompressionMode.Decompress);
        using var reader=new BinaryReader(stream);int w=reader.ReadInt32(),h=reader.ReadInt32();return new(w,h,reader.ReadBytes(w*h*4));
    }
    public static void Run(Action<string,Action> test)
    {
        test("Moving envelope measures connected fringe without expanding core offsets",()=>{
            var f=Fixture();var old=ProbeAnalysis.SpatialControl(f.Before,f.After,f.Line,6,0);
            Require(old.Failure==ProbeFailure.SceneChanged);
            var measured=ProbeStrokeEnvelope.Measure(f.Before,f.After,f.Line,6,0);
            Require(measured.Passed&&measured.SceneRadius==7&&Math.Abs(measured.PerpendicularOffset)<=4);
            Require(measured.CoreCoverage.Reference!.Tolerance==12&&measured.CoreCoverage.Expected==32);
            var axis=Axis(measured);var trial=ProbeSpatialCalibration.Trial(f.Before,f.After,f.Line,6,axis,new(new(20,20,20),12));
            Require(trial.Passed&&trial.SceneRadius==7&&axis.AllowedOffsets.SequenceEqual(new[]{measured.PerpendicularOffset}));
        });
        test("Frozen moving guard cannot learn a fast candidate fringe or unrelated scene patch",()=>{
            var f=Fixture();var measured=ProbeStrokeEnvelope.Measure(f.Before,f.After,f.Line,6,0);var axis=Axis(measured);
            var expanded=f.After.Clone();for(int x=12;x<=51;x++)expanded.Set(40*64+x,new(140,140,140));
            Require(ProbeSpatialCalibration.Trial(f.Before,expanded,f.Line,6,axis,new(new(20,20,20),12)).Failure==ProbeFailure.SceneChanged);
            Require(axis.SceneRadius==7);var unstable=f.After.Clone();for(int i=0;i<100;i++)unstable.Set(i,new(0,0,0));
            Require(ProbeStrokeEnvelope.Measure(f.Before,unstable,f.Line,6,0).Failure==ProbeFailure.SceneChanged);
            Require(ProbeSpatialCalibration.Trial(f.Before,unstable,f.Line,6,axis,new(new(20,20,20),12)).Failure==ProbeFailure.SceneChanged);
        });
        test("Disconnected patch inside envelope search cannot enlarge measured guard",()=>{
            var f=Fixture();for(int y=23;y<=24;y++)for(int x=12;x<=51;x++)f.After.Set(y*64+x,new(20,20,20));
            Require(ProbeStrokeEnvelope.Measure(f.Before,f.After,f.Line,6,0).Failure==ProbeFailure.SceneChanged);
        });
        test("Scene guard does not authorize shifted wrong color missing or narrow fast cores",()=>{
            var f=Fixture();var measured=ProbeStrokeEnvelope.Measure(f.Before,f.After,f.Line,6,0);var axis=Axis(measured);
            var reference=new AuditReference(new(20,20,20),12);var wrong=f.After.Clone();
            for(int y=33;y<=38;y++)for(int x=12;x<=51;x++)wrong.Set(y*64+x,new(65,65,65));
            Require(!ProbeSpatialCalibration.Trial(f.Before,wrong,f.Line,6,axis,reference).Passed);
            var missing=f.After.Clone();missing.Set((32+measured.PerpendicularOffset)*64+30,new(160,160,160));
            Require(!ProbeSpatialCalibration.Trial(f.Before,missing,f.Line,6,axis,reference).Passed);
            var shifted=f.Before.Clone();for(int x=12;x<=51;x++)shifted.Set((32+measured.PerpendicularOffset+1)*64+x,new(20,20,20));
            Require(!ProbeSpatialCalibration.Trial(f.Before,shifted,f.Line,6,axis,reference).Passed);
            Require(!ProbeSpatialCalibration.Trial(f.Before,shifted,f.Line,6,axis with{InnerRadius=1},reference).Passed);
        });
        test("Moving calibration refuses paint beyond bounded search or clipped capture",()=>{
            var f=Fixture();for(int y=39;y<=43;y++)for(int x=12;x<=51;x++)f.After.Set(y*64+x,new(140,140,140));
            Require(!ProbeStrokeEnvelope.Measure(f.Before,f.After,f.Line,6,0).Passed);
            try{(Axis(ProbeStrokeEnvelope.Measure(Fixture().Before,Fixture().After,Fixture().Line,6,0)) with{SceneRadius=11}).SceneGuard(6);throw new Exception("Invalid radius accepted");}
            catch(ArgumentException){}
        });
        for(int index=1;index<=3;index++)
        {
            int n=index;test($"Recorded beta40 slow control {n} preserves core and independently measures fringe",()=>{
                var before=Load($"beta40-moving-{n}-before");var after=Load($"beta40-moving-{n}-after");
                var line=n==3?new ScreenLine(11,32,52,32):new ScreenLine(12,32,51,32);int outer=n==3?5:6;
                var previous=ProbeAnalysis.SpatialControl(before,after,line,outer,0);
                Require(previous.Failure==ProbeFailure.SceneChanged&&previous.OutsideChanged==(n==3?94:40));
                var measured=ProbeStrokeEnvelope.Measure(before,after,line,outer,0);
                Require(measured.Passed&&measured.SceneRadius>outer&&measured.SceneRadius<=ProbeStrokeEnvelope.MeasurementRadius(outer));
                Require(measured.CoreCoverage.Expected==(n==3?34:32)&&measured.CoreCoverage.Covered==measured.CoreCoverage.Expected);
                Require(measured.CoreMeasurement.Reference==previous.CoreMeasurement.Reference&&measured.PerpendicularOffset==previous.PerpendicularOffset);
                Require(measured.CoreMeasurement.Reference!.Tolerance==12);
            });
        }
        test("Setup Size choice is independent of missing Size 1 and excludes invalid cores or other shapes",()=>{
            var s=Settings.Defaults();BrushSpan[] rows=[new(-1,-1,2),new(0,-1,2),new(1,-1,2)];var stamp=new BrushStamp(rows,rows,new(20,20,20));
            var p=BrushFootprints.Build(s,3,3,[stamp,stamp,stamp]);var empty=p with{Size=1,SolidCore=default};
            Require(SetupBrushSelection.Select([empty,p],3,3)==3&&SetupBrushSelection.Select([empty,p],3,1)==3);
            Require(SetupBrushSelection.Select([p],4,3) is null&&SetupBrushSelection.Select([empty],3,1) is null&&SetupBrushSelection.Select(null,3,3) is null);
        });
    }
}
