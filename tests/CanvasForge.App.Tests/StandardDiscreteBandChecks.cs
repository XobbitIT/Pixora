using System.IO;
using System.Text.Json;
using CanvasForge.App;

internal static partial class Program
{
    private static void CheckDiscreteBandFreeze(string output)
    {
        string parent=Path.Combine(output,"discrete-pair");StandardDiscreteBandCli.Init(Path.Combine(output,"track-pair"),parent,synthetic:true);
        var a=StandardDiscreteBandCli.Read(Path.Combine(parent,"ascending"),offline:true);var b=StandardDiscreteBandCli.Read(Path.Combine(parent,"descending"),offline:true);
        Assert(a.Pair.PairId==b.Pair.PairId&&a.Condition.Cases.Length==2&&b.Condition.Cases.Length==2,"Missing same-build frontier cohort");
        foreach(var (up,down) in a.Condition.Cases.Zip(b.Condition.Cases))
        {
            Assert(up.Paths.Length==down.Paths.Length&&up.Paths.Select(p=>p[0].X).SequenceEqual(down.Paths.Select(p=>p[0].X).Reverse()),"Low frontier not exposed in reverse track order");
            Assert(up.Paths.Concat(down.Paths).All(p=>p.Length==401&&p.Select(q=>q.X).Distinct().Count()==1),"Unmeasured correction warp or truncated body collected");
        }
        bool rejected=false;try{StandardDiscreteBandCli.Read(Path.Combine(parent,"ascending"));}catch(InvalidDataException){rejected=true;}
        Assert(rejected,"Synthetic frontier cohort could dispatch paint");
        foreach(string target in new[]{parent,Path.Combine(output,"track-pair"),Path.Combine(output,"track-pair/nested")})
        {rejected=false;try{StandardDiscreteBandCli.Init(Path.Combine(output,"track-pair"),target,synthetic:true);}catch(IOException){rejected=true;}Assert(rejected,"Frontier initialization could overwrite source or existing evidence");}
    }
    private static void CheckDiscreteBandPipeline(string output)
    {
        string parent=Path.Combine(output,"discrete-pair");
        foreach(string direction in new[]{"ascending","descending"})
        {
            string folder=Path.Combine(parent,direction);var p=StandardDiscreteBandCli.Read(folder,offline:true);var view=StandardDiscreteBandCli.View(p.Pair,p.Condition);
            var result=SimulateTrackPair(folder,view,revision:StandardDiscreteBandCli.Revision);int count=p.Condition.Cases.Sum(c=>c.Paths.Length);
            Assert(result.Status=="DATA_COLLECTED"&&result.CompletedStrokes==count,"Frontier condition failed: "+result.Reason);StandardTrackPairCli.ValidateCompleted(folder,view);
        }
        string review=Path.Combine(output,"discrete-review");StandardDiscreteBandCli.Review(parent,review,synthetic:true);
        using var summary=JsonDocument.Parse(File.ReadAllText(Path.Combine(review,"summary.json")));
        Assert(!summary.RootElement.GetProperty("interpolation").GetBoolean()&&!summary.RootElement.GetProperty("turnModelAvailable").GetBoolean()
            &&!summary.RootElement.GetProperty("dispatchAllowed").GetBoolean(),"Frontier hypothesis silently enabled interpolation/turns/paint");
        using var models=JsonDocument.Parse(File.ReadAllText(Path.Combine(review,"discrete-models.json")));
        Assert(models.RootElement.GetArrayLength()==2,"Mandatory frontier case omitted");
        foreach(var m in models.RootElement.EnumerateArray())
        {
            var model=m.GetProperty("model");Assert(model.GetProperty("LowFrontiers").GetArrayLength()>0&&model.GetProperty("HighFrontiers").GetArrayLength()>0,"One edge was synthesized instead of independently measured");
            Assert(!model.GetProperty("PhysicalQualityCertified").GetBoolean(),"Fixture certified physical paint");
        }
    }
    private static void CheckDiscreteBandPortableAndTamper(string output)
    {
        string original=Path.Combine(output,"discrete-pair"),moved=Path.Combine(output,"Moved Frontiers Ω space");Directory.CreateDirectory(moved);
        foreach(string file in Directory.GetFiles(original,"*",SearchOption.AllDirectories))
        {string target=Path.Combine(moved,Path.GetRelativePath(original,file));Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(file,target);}
        string source=Path.GetFullPath(Path.Combine(output,"track-pair")),hidden=Path.GetFullPath(Path.Combine(output,"track-pair-hidden")),root=Path.GetFullPath(output)+Path.DirectorySeparatorChar;
        Assert(source.StartsWith(root,StringComparison.OrdinalIgnoreCase)&&hidden.StartsWith(root,StringComparison.OrdinalIgnoreCase),"Test move escaped its fixture workspace");
        Directory.Move(source,hidden);
        try{StandardDiscreteBandCli.Read(Path.Combine(moved,"ascending"),offline:true);StandardDiscreteBandCli.Read(Path.Combine(moved,"descending"),offline:true);}
        finally{Directory.Move(hidden,source);}
        string baseline=Path.Combine(moved,"reference/baseline.png");byte[] bytes=File.ReadAllBytes(baseline);File.AppendAllText(baseline,"tamper");bool refused=false;
        try{StandardDiscreteBandCli.Read(Path.Combine(moved,"ascending"),offline:true);}catch(InvalidDataException){refused=true;}finally{File.WriteAllBytes(baseline,bytes);}
        Assert(refused,"Damaged embedded baseline accepted");
        string meta=Path.Combine(moved,"descending/sample-00/black-band/stroke-000-after.json"),saved=File.ReadAllText(meta);File.Delete(meta);refused=false;
        try{StandardDiscreteBandCli.Review(moved,Path.Combine(output,"discrete-review-missing"),synthetic:true);}catch(FileNotFoundException){refused=true;}finally{File.WriteAllText(meta,saved);}
        Assert(refused&&!Directory.Exists(Path.Combine(output,"discrete-review-missing")),"Missing mandatory native metadata published a partial model");
    }
    private static void CheckDiscreteBandFailure(string output)
    {
        string parent=Path.Combine(output,"discrete-failure");StandardDiscreteBandCli.Init(Path.Combine(output,"track-pair"),parent,synthetic:true);
        string folder=Path.Combine(parent,"ascending");var p=StandardDiscreteBandCli.Read(folder,offline:true);var view=StandardDiscreteBandCli.View(p.Pair,p.Condition);
        var result=SimulateTrackPair(folder,view,"capture_after_paint",StandardDiscreteBandCli.Revision);Assert(result.Status=="INCOMPLETE"&&result.CompletedStrokes==0,"Failed frontier capture became complete");
        using var status=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"series-status.json")));
        Assert(status.RootElement.GetProperty("emittedStrokes").GetInt32()==1&&status.RootElement.GetProperty("completedStrokeCaptures").GetInt32()==0,"Paint/capture failure counters conflated");
        bool refused=false;try{StandardDiscreteBandCli.Read(folder);}catch(IOException){refused=true;}Assert(refused,"Partial paint could be retried");
        var descending=StandardDiscreteBandCli.Read(Path.Combine(parent,"descending"),offline:true);refused=false;
        try{StandardTrackPairCli.ValidateCompleted(folder,view);}catch(InvalidDataException){refused=true;}Assert(refused,"Incomplete ascending condition accepted for reverse collection");
    }
}
