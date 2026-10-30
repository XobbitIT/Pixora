using System.IO;
using System.Text.Json;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void CheckBandInversePilotFreeze(string output)
    {
        string folder=Path.Combine(output,"inverse-pilot"),source=Path.Combine(output,"track-pair");
        // The old ideal renderer produces identical control/shifted placement.
        // It must NOT be promoted to inverse calibration data.
        bool unavailable=false;try{StandardBandInversePilotCli.Init(source,Path.Combine(output,"inverse-pilot-unavailable"),synthetic:true);}catch(InvalidDataException){unavailable=true;}
        Assert(unavailable,"Identical synthetic placements produced an inverse pilot");
        // Separate, explicitly synthetic protocol fixture tests only dispatch
        // lifecycle, ownership, interruption and artifact verification. It is
        // not a fit to the ideal renderer and can never dispatch native input.
        CreateInversePilotFixture(source,folder);
        var m=StandardBandInversePilotCli.Read(folder,offline:true);
        Assert(m.Cases.Length==2&&m.Cases.All(c=>c.Sample.Vertical),"Mandatory vertical pilot omitted");
        Assert(m.Cases.All(c=>c.Paths.Length==c.Sample.Stages[0].Lines.Length&&c.Paths.All(p=>p.Length==401)),"Full pilot command missing");
        Assert(m.Cases.All(c=>c.Sample.Speed==m.Context.Speed),"Actual pilot speed is not bound to calibration speed");
        bool rejected=false;try{StandardBandInversePilotCli.Read(folder);}catch(InvalidDataException){rejected=true;}
        Assert(rejected,"Synthetic corrected paths could dispatch native paint");
        foreach(string target in new[]{folder,m.SourceRoot,Path.Combine(m.SourceRoot,"nested")})
        {
            rejected=false;try{StandardBandInversePilotCli.Init(m.SourceRoot,target,synthetic:true);}catch(IOException){rejected=true;}
            Assert(rejected,"Inverse initialization could overwrite source/existing pilot");
        }
    }
    private static void CreateInversePilotFixture(string source,string folder)
    {
        Directory.CreateDirectory(Path.Combine(folder,"model"));var reference=StandardTrackPairCli.Read(Path.Combine(source,"control"),offline:true);
        var rows=reference.Cases.Where(c=>c.Sample.Vertical).Select(c=>
        {
            var target=c.Sample.Stages[0].Lines[0];var paths=c.Paths.Select(p=>p.Select(q=>new ScreenPoint(q.X+(q.Y%20<10?0:1),q.Y)).ToArray()).ToArray();
            var context=new StandardBandResponseContext(reference.Context,reference.AppSha256,reference.CoreSha256,StandardContourMotion.Revision,c.Sample.Speed,reference.CapturePolicy);
            return new{sourceSample=c.SourceSample,controlScore=new StandardBandScore(0,1,1,1,0,0),shiftedScore=new StandardBandScore(0,1,1,1,0,0),candidate=new{
                Revision=StandardBandInverse.Revision,ContextId=context.Id,Id=new string(c.SourceSample==19?'c':'d',64),DispatchAllowed=false,PhysicalQualityCertified=false,
                Target=target,TargetWidth=14,Prediction=new StandardInverseScore(0,0,0),Paths=paths,
                Rows=Enumerable.Range(Math.Min(target.Y1,target.Y2),401).Select(y=>new StandardInverseRow(y,0,target.X1-7,target.X1+7)).ToArray()}};
        }).ToArray();
        string candidate=Path.Combine(folder,"model/inverse-candidates.json");File.WriteAllText(candidate,JsonSerializer.Serialize(rows));
        var cases=rows.Select((r,i)=>{
            var c=reference.Cases.Single(c=>c.SourceSample==r.sourceSample);var paths=r.candidate.Paths;
            var sample=c.Sample with{Index=i,Kind="joint-band-inverse-hypothesis",Stages=[new("black-band",new(0,0,0),paths.Select(p=>new ScreenLine(p[0].X,p[0].Y,p[^1].X,p[^1].Y)).ToArray())]};
            return new StandardBandInversePilotCli.PilotCase(c.SourceSample,r.candidate.Id,sample,paths);
        }).ToArray();
        File.Copy(Path.Combine(source,"control/settings.json"),Path.Combine(folder,"settings.json"));
        var files=Directory.GetFiles(source,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToDictionary(p=>Path.GetRelativePath(source,p).Replace('\\','/'),StandardBoundaryReview.Hash,StringComparer.Ordinal);
        var m=new StandardBandInversePilotCli.Manifest(StandardBandInversePilotCli.Revision,source,files,StandardBoundaryReview.Hash(candidate),reference.Context,cases,"",
            StandardBoundaryReview.Hash(typeof(Painter).Assembly.Location),StandardBoundaryReview.Hash(typeof(Settings).Assembly.Location),StandardBoundaryReview.Hash(Path.Combine(folder,"settings.json")),true)
            {ReferenceFiles=StandardBandInversePilotCli.EmbedReference(source,folder)};
        m=m with{ProtocolId=StandardBandInversePilotCli.Identity(m)};string protocol=Path.Combine(folder,"protocol.json");
        File.WriteAllText(protocol,JsonSerializer.Serialize(m));File.WriteAllText(protocol+".sha256",StandardBoundaryReview.Hash(protocol));
    }
    private static void CheckBandInversePilotPortable(string output)
    {
        string folder=Path.Combine(output,"inverse-pilot"),protocol=Path.Combine(folder,"protocol.json");
        var m=StandardBandInversePilotCli.Read(folder,offline:true);
        string missing=Path.Combine(output,"raw-collection-no-longer-present");Assert(!Directory.Exists(missing),"Portable negative fixture unexpectedly exists");
        m=m with{SourceRoot=missing};m=m with{ProtocolId=StandardBandInversePilotCli.Identity(m)};
        File.WriteAllText(protocol,JsonSerializer.Serialize(m));File.WriteAllText(protocol+".sha256",StandardBoundaryReview.Hash(protocol));
        var portable=StandardBandInversePilotCli.Read(folder,offline:true);
        Assert(portable.ProtocolId==m.ProtocolId&&StandardBandInversePilotCli.Baseline(folder,portable).Width==m.Context.Canvas.Width,"Portable pilot still depends on removed raw collection");
        string moved=Path.Combine(output,"Moved Pilot Ω space");Directory.CreateDirectory(moved);
        foreach(string file in Directory.GetFiles(folder,"*",SearchOption.AllDirectories))
        {string target=Path.Combine(moved,Path.GetRelativePath(folder,file));Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(file,target);}
        Assert(StandardBandInversePilotCli.Read(moved,offline:true).ProtocolId==m.ProtocolId,"Moving portable bundle changed eligibility");
        string baseline=Path.Combine(moved,"reference/baseline.png");var original=File.ReadAllBytes(baseline);File.AppendAllText(baseline,"changed");
        bool rejected=false;try{StandardBandInversePilotCli.Read(moved,offline:true);}catch(InvalidDataException){rejected=true;}finally{File.WriteAllBytes(baseline,original);}
        Assert(rejected,"Damaged embedded baseline accepted");
        StandardBandInversePilotCli.LogLaunchFailure(moved,new IOException("portable-launch-log-fixture"));
        Assert(File.ReadAllText(Path.Combine(moved,"launch-errors.jsonl")).Contains("portable-launch-log-fixture"),"Early failure disappeared without log");
        Assert(StandardBandInversePilotCli.Read(moved,offline:true).ProtocolId==m.ProtocolId,"Launch log invalidated immutable reference");
    }
    private static void CheckBandInversePilotPipeline(string output)
    {
        string folder=Path.Combine(output,"inverse-pilot");var m=StandardBandInversePilotCli.Read(folder,offline:true);var view=StandardBandInversePilotCli.ValidationView(m);
        var result=SimulateTrackPair(folder,view);Assert(result.Status=="DATA_COLLECTED"&&result.CompletedStrokes==m.Cases.Sum(c=>c.Paths.Length),"Corrected paths failed full native-like lifecycle: "+result.Reason);
        StandardTrackPairCli.ValidateCompleted(folder,view);
        string evaluation=Path.Combine(output,"inverse-pilot-evaluation");StandardBandInverseEvaluation.Run(folder,evaluation,synthetic:true);
        using var evaluated=JsonDocument.Parse(File.ReadAllText(Path.Combine(evaluation,"physical-comparison.json")));
        Assert(evaluated.RootElement.GetProperty("results").GetArrayLength()==2&&!evaluated.RootElement.GetProperty("physicalQualityCertified").GetBoolean(),"Corrected physical readback missing or became certification");
        bool fakeRejected=false;try{StandardBandInverseEvaluation.Run(folder,Path.Combine(output,"inverse-pilot-fake-native"));}catch(InvalidDataException){fakeRejected=true;}
        Assert(fakeRejected,"Synthetic corrected readback accepted as native");
        bool rejected=false;try{StandardBandInversePilotCli.Read(folder);}catch(IOException){rejected=true;}Assert(rejected,"Completed correction could be retried");
        string path=Path.Combine(folder,"sample-00/black-band/stroke-000-pair.json"),original=File.ReadAllText(path);File.Delete(path);
        try{rejected=false;try{StandardTrackPairCli.ValidateCompleted(folder,view);}catch(FileNotFoundException){rejected=true;}Assert(rejected,"Missing corrected-stroke evidence accepted");}
        finally{File.WriteAllText(path,original);}
    }
    private static void CheckBandInversePilotFailureAndTamper(string output)
    {
        string folder=Path.Combine(output,"inverse-pilot");var m=StandardBandInversePilotCli.Read(folder,offline:true);
        string failed=Path.Combine(output,"inverse-pilot-failure");Directory.CreateDirectory(failed);File.Copy(Path.Combine(folder,"settings.json"),Path.Combine(failed,"settings.json"));
        var result=SimulateTrackPair(failed,StandardBandInversePilotCli.ValidationView(m),"capture_after_paint");
        Assert(result.Status=="INCOMPLETE"&&result.CompletedStrokes==0,"Capture failure continued corrected painting");
        using var status=JsonDocument.Parse(File.ReadAllText(Path.Combine(failed,"series-status.json")));
        Assert(status.RootElement.GetProperty("emittedStrokes").GetInt32()==1&&status.RootElement.GetProperty("completedStrokeCaptures").GetInt32()==0,"Actual corrected input conflated with captured evidence");
        string file=Path.Combine(folder,"model/inverse-candidates.json"),original=File.ReadAllText(file);File.AppendAllText(file," ");
        bool rejected=false;try{StandardBandInversePilotCli.Read(folder,offline:true);}catch(InvalidDataException){rejected=true;}finally{File.WriteAllText(file,original);}
        Assert(rejected,"Changed candidate model accepted");
        file=Path.Combine(folder,"settings.json");original=File.ReadAllText(file);File.AppendAllText(file," ");
        rejected=false;try{StandardBandInversePilotCli.Read(folder,offline:true);}catch(InvalidDataException){rejected=true;}finally{File.WriteAllText(file,original);}
        Assert(rejected,"Changed frozen settings accepted");
    }
}
