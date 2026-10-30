using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

// Two manually initiated collections at the SAME coordinates, same binaries
// and Rust PID. Canvas clearing is performed by the user between conditions.
// Runtime reads only embedded references, never the historical SourceRoot.
internal static class StandardDiscreteBandCli
{
    internal const string Revision="standard-bidirectional-frontier-pair-v1";
    internal sealed record GridCase(int SourceSample,ScreenLine Target,EvidenceSeriesCase Sample,int[] Commands);
    internal sealed record Pair(string Revision,string SourceRoot,Dictionary<string,string> SourceFiles,
        Dictionary<string,string> ReferenceFiles,StandardGeometryContext Context,GridCase[] Cases,string SettingsSha256,
        string AppSha256,string CoreSha256,bool SyntheticSource,string CollectionId,string PairId);
    internal sealed record Condition(string Revision,string Direction,string PairId,StandardTrackPairCli.TrackCase[] Cases,string ProtocolId);
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    private static readonly Dictionary<string,string> References=new(StringComparer.Ordinal)
    {
        ["reference/control-protocol.json"]="control/protocol.json",
        ["reference/control-protocol.json.sha256"]="control/protocol.json.sha256",
        ["reference/shifted-protocol.json"]="shifted/protocol.json",
        ["reference/shifted-protocol.json.sha256"]="shifted/protocol.json.sha256",
        ["reference/baseline.png"]="control/baseline.png",
        ["reference/baseline.json"]="control/baseline.json",
        ["reference/design-protocol.json"]="source/protocol.json",
        ["reference/design-protocol.json.sha256"]="source/protocol.json.sha256"
    };
    private static string Hash(string p)=>StandardBoundaryReview.Hash(p);
    private static string Id(object x)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(x))).ToLowerInvariant();
    private static Dictionary<string,string> Files(string root)=>Directory.GetFiles(root,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal)
        .ToDictionary(p=>Path.GetRelativePath(root,p).Replace('\\','/'),Hash,StringComparer.Ordinal);
    private static void Separate(string source,string output)
    {
        static bool Within(string a,string b)=>a.Equals(b,StringComparison.OrdinalIgnoreCase)||a.StartsWith(b+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        if(Within(source,output)||Within(output,source)||Directory.Exists(output)||File.Exists(output))throw new IOException("Потрібна нова окрема папка; наявні дані не перезаписуються.");
    }
    internal static void Init(string source,string output,bool synthetic=false)
    {
        source=Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);output=Path.GetFullPath(output).TrimEnd(Path.DirectorySeparatorChar);Separate(source,output);
        var original=Files(source);var a=StandardTrackPairCli.Read(Path.Combine(source,"control"),offline:true);var b=StandardTrackPairCli.Read(Path.Combine(source,"shifted"),offline:true);
        if(a.PairId!=b.PairId||a.AppSha256!=b.AppSha256||a.CoreSha256!=b.CoreSha256||a.Context.Id!=b.Context.Id||a.SyntheticSource!=synthetic||b.SyntheticSource!=synthetic)
            throw new InvalidDataException("Matching complete native source conditions are required.");
        StandardTrackPairCli.ValidateCompleted(Path.Combine(source,"control"),a);StandardTrackPairCli.ValidateCompleted(Path.Combine(source,"shifted"),b);
        var design=StandardContourPilotCli.Read(Path.Combine(source,"source"),offline:true);
        var grids=a.Cases.Where(c=>c.Sample.Vertical).Select(c=>
        {
            var other=b.Cases.Single(x=>x.SourceSample==c.SourceSample);var target=design.Design.Single(x=>x.SourceSample==c.SourceSample).Command;
            var commands=c.Paths.Concat(other.Paths).Select(p=>p[0].X).Distinct().Order().ToArray();
            return new GridCase(c.SourceSample,target,c.Sample,commands);
        }).ToArray();
        ValidateGrids(a.Context,grids);Directory.CreateDirectory(output);
        var embedded=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var (local,old) in References)
        {string p=Path.Combine(output,local);Directory.CreateDirectory(Path.GetDirectoryName(p)!);File.Copy(Path.Combine(source,old),p);embedded.Add(local,Hash(p));}
        File.Copy(Path.Combine(source,"control/settings.json"),Path.Combine(output,"settings.json"));
        var pair=new Pair(Revision,source,original,embedded,a.Context,grids,Hash(Path.Combine(output,"settings.json")),
            Hash(typeof(Painter).Assembly.Location),Hash(typeof(Settings).Assembly.Location),synthetic,Guid.NewGuid().ToString("N"),"");pair=pair with{PairId=Id(pair)};
        string path=Path.Combine(output,"pair.json");File.WriteAllText(path,JsonSerializer.Serialize(pair,Json));File.WriteAllText(path+".sha256",Hash(path));
        foreach(string direction in new[]{"ascending","descending"})
        {
            string directory=Path.Combine(output,direction);Directory.CreateDirectory(directory);File.Copy(Path.Combine(output,"settings.json"),Path.Combine(directory,"settings.json"));
            var m=Make(pair,direction);string protocol=Path.Combine(directory,"protocol.json");File.WriteAllText(protocol,JsonSerializer.Serialize(m,Json));File.WriteAllText(protocol+".sha256",Hash(protocol));
            File.WriteAllText(Path.Combine(directory,"series-status.json"),JsonSerializer.Serialize(new{status="NOT_STARTED",requiredCases=m.Cases.Length,
                requiredStrokes=m.Cases.Sum(c=>c.Paths.Length),nativeInput=false,automaticRetry=false,automaticResume=false,physicalQualityCertified=false}));
            Read(directory,offline:true);
        }
        if(JsonSerializer.Serialize(original)!=JsonSerializer.Serialize(Files(source)))throw new IOException("Source evidence changed during initialization.");
        File.WriteAllText(Path.Combine(output,"collection-policy.json"),JsonSerializer.Serialize(new{revision=Revision,pairId=pair.PairId,
            directions=new[]{"ascending","descending"},sameCoordinates=true,sameRustProcessRequired=true,manualClearBetweenConditions=true,
            commandGrid=grids.Select(g=>new{g.SourceSample,g.Commands}),strokesPerCondition=grids.Sum(g=>g.Commands.Length),
            reason="ascending exposes each high frontier; descending exposes each low frontier; no inferred +2 low edge",
            motion="constant full-length tracks at measured coordinates; no correction warp",ordinaryStartChanged=false,
            thresholds="RGB12/contrast80 unchanged",physicalQualityCertified=false,nativeInput=false},Json));
    }
    private static void ValidateGrids(StandardGeometryContext context,GridCase[] grids)
    {
        context.Validate();
        if(grids.Length!=2||grids.Select(g=>g.SourceSample).Distinct().Count()!=2)throw new InvalidDataException("Both vertical frontier positions are required.");
        for(int i=0;i<grids.Length;i++)
        {
            var g=grids[i];var r=g.Sample.Reservation;var c=context.Canvas;
            if(!g.Sample.Vertical||g.Sample.Reverse||g.Target.X1!=g.Target.X2||TransferSchedule.Length(g.Target)!=g.Sample.Length
                ||g.Sample.Length!=400||g.Commands.Length is <2 or >16||g.Commands.Zip(g.Commands.Skip(1)).Any(p=>p.Second!=p.First+1)
                ||!r.Valid||r.Left<c.Left||r.Top<c.Top||r.Right>c.Right||r.Bottom>c.Bottom
                ||g.Commands[0]<r.Left||g.Commands[^1]>=r.Right||Math.Min(g.Target.Y1,g.Target.Y2)<r.Top||Math.Max(g.Target.Y1,g.Target.Y2)>=r.Bottom)
                throw new InvalidDataException("Invalid complete frontier grid or reservation.");
            if(grids.Take(i).Any(a=>a.Sample.Reservation.Left<r.Right&&a.Sample.Reservation.Right>r.Left&&a.Sample.Reservation.Top<r.Bottom&&a.Sample.Reservation.Bottom>r.Top))
                throw new InvalidDataException("Frontier reservations overlap.");
        }
    }
    private static Condition Make(Pair pair,string direction)
    {
        if(direction is not ("ascending" or "descending"))throw new InvalidDataException("Unknown frontier direction.");
        var cases=pair.Cases.Select((g,i)=>
        {
            var commands=direction=="ascending"?g.Commands:g.Commands.Reverse();
            var paths=commands.Select(q=>Enumerable.Range(0,401).Select(k=>new ScreenPoint(q,g.Target.Y1+k*Math.Sign(g.Target.Y2-g.Target.Y1))).ToArray()).ToArray();
            foreach(var p in paths)StandardBandContour.Validate(p,pair.Context.Canvas);
            var sample=g.Sample with{Index=i,Kind="discrete-frontier-"+direction,Stages=[new("black-band",new(0,0,0),paths.Select(p=>new ScreenLine(p[0].X,p[0].Y,p[^1].X,p[^1].Y)).ToArray())]};
            return new StandardTrackPairCli.TrackCase(g.SourceSample,0,Id(new{pair.PairId,direction,g.SourceSample}),sample,paths);
        }).ToArray();
        var m=new Condition(Revision,direction,pair.PairId,cases,"");return m with{ProtocolId=Id(m)};
    }
    internal static Pair ReadPair(string parent)
    {
        string path=Path.Combine(parent,"pair.json");if(Hash(path)!=File.ReadAllText(path+".sha256").Trim())throw new InvalidDataException("Frozen frontier pair changed.");
        var m=JsonSerializer.Deserialize<Pair>(File.ReadAllText(path))??throw new InvalidDataException("Missing frontier pair.");
        if(m.Revision!=Revision||!Guid.TryParseExact(m.CollectionId,"N",out _)||m.PairId!=Id(m with{PairId=""})||m.SettingsSha256!=Hash(Path.Combine(parent,"settings.json"))
            ||m.ReferenceFiles.Count!=References.Count||m.ReferenceFiles.Keys.Any(k=>!References.ContainsKey(k)))throw new InvalidDataException("Incomplete frontier bindings.");
        foreach(var (local,old) in References)
            if(!m.SourceFiles.TryGetValue(old,out var expected)||m.ReferenceFiles[local]!=expected||Hash(Path.Combine(parent,local))!=expected)
                throw new InvalidDataException("Переносний еталон пошкоджений. Розпакуй весь пакет.");
        var reference=JsonSerializer.Deserialize<StandardTrackPairCli.Manifest>(File.ReadAllText(Path.Combine(parent,"reference/control-protocol.json")))!;
        var shifted=JsonSerializer.Deserialize<StandardTrackPairCli.Manifest>(File.ReadAllText(Path.Combine(parent,"reference/shifted-protocol.json")))!;
        var design=JsonSerializer.Deserialize<StandardContourPilotCli.Manifest>(File.ReadAllText(Path.Combine(parent,"reference/design-protocol.json")))!;
        if(reference.Revision!=StandardTrackPairCli.Revision||reference.Condition!="control"||reference.Context.Id!=m.Context.Id||reference.SettingsSha256!=m.SettingsSha256
            ||design.ProtocolId!=reference.SourceProtocolId||design.Context.Id!=m.Context.Id||reference.SyntheticSource!=m.SyntheticSource||design.SyntheticSource!=m.SyntheticSource
            ||shifted.PairId!=reference.PairId||shifted.Condition!="shifted"||shifted.Context.Id!=m.Context.Id||shifted.SyntheticSource!=m.SyntheticSource
            ||!m.Cases.Select(g=>g.SourceSample).SequenceEqual(reference.Cases.Where(c=>c.Sample.Vertical).Select(c=>c.SourceSample)))
            throw new InvalidDataException("Embedded frontier provenance changed.");
        foreach(var g in m.Cases)
        {
            var control=reference.Cases.Single(c=>c.SourceSample==g.SourceSample);var other=shifted.Cases.Single(c=>c.SourceSample==g.SourceSample);
            var commands=control.Paths.Concat(other.Paths).Select(p=>p[0].X).Distinct().Order().ToArray();
            if(g.Target!=design.Design.Single(d=>d.SourceSample==g.SourceSample).Command||!g.Commands.SequenceEqual(commands)
                ||JsonSerializer.Serialize(g.Sample)!=JsonSerializer.Serialize(control.Sample))throw new InvalidDataException("Frozen frontier grid changed.");
        }
        ValidateGrids(m.Context,m.Cases);StandardStraightPilotCli.Frame(Path.Combine(parent,"reference/baseline.png"),m.Context.Canvas,m.SyntheticSource);return m;
    }
    internal static (Pair Pair,Condition Condition) Read(string directory,bool offline=false,bool preflight=false)
    {
        if(!offline&&!preflight&&(File.Exists(Path.Combine(directory,"attempted.json"))||File.Exists(Path.Combine(directory,"started.json"))))throw new IOException("Цю умову вже запускали. Автоматичне повторення заборонене.");
        string parent=Path.GetDirectoryName(Path.GetFullPath(directory))!;var pair=ReadPair(parent);string path=Path.Combine(directory,"protocol.json");
        if(Hash(path)!=File.ReadAllText(path+".sha256").Trim())throw new InvalidDataException("Frozen frontier condition changed.");
        var m=JsonSerializer.Deserialize<Condition>(File.ReadAllText(path))??throw new InvalidDataException("Missing frontier condition.");
        if(!Path.GetFileName(Path.GetFullPath(directory)).Equals(m.Direction,StringComparison.OrdinalIgnoreCase)
            ||JsonSerializer.Serialize(m)!=JsonSerializer.Serialize(Make(pair,m.Direction))||Hash(Path.Combine(directory,"settings.json"))!=pair.SettingsSha256
            ||!offline&&(pair.SyntheticSource||pair.AppSha256!=Hash(typeof(Painter).Assembly.Location)||pair.CoreSha256!=Hash(typeof(Settings).Assembly.Location)))
            throw new InvalidDataException("Frontier paths, settings or binaries changed.");
        if(!offline&&!preflight&&m.Direction=="descending")
        {var prior=Read(Path.Combine(parent,"ascending"),offline:true);StandardTrackPairCli.ValidateCompleted(Path.Combine(parent,"ascending"),View(prior.Pair,prior.Condition));}
        return(pair,m);
    }
    internal static StandardTrackPairCli.Manifest View(Pair p,Condition c)=>new(StandardTrackPairCli.Revision,"discrete-"+c.Direction,p.PairId,p.Context,c.Cases,c.ProtocolId,"",
        p.SourceFiles,p.SettingsSha256,p.AppSha256,p.CoreSha256,p.SyntheticSource,StandardTrackPairCli.CapturePolicy);
    internal static void Verify(string directory)
    {
        var p=Read(directory,offline:true);StandardTrackPairCli.ValidateCompleted(directory,View(p.Pair,p.Condition));
        FrontierSessionCapture.VerifyUse(directory,p.Pair);
    }
    internal static void Run(string directory,bool explicitSessionCapture=false)
    {
        var (pair,condition)=Read(directory);StandardEvidenceSeriesRunner.Result? result=null;
        if(explicitSessionCapture&&condition.Direction=="descending")Verify(Path.Combine(Path.GetDirectoryName(directory)!,"ascending"));
        StandardEvidenceSeriesCli.RunCollection(directory,pair.Context.Canvas,condition.Cases.Length,10,p=>result=p.CollectDiscreteFrontiers(pair,condition,directory),
            explicitSessionCapture?(s,w)=>FrontierSessionCapture.CaptureOrLoad(s,w,pair,condition,directory):null);
        if(result?.Status!="DATA_COLLECTED")Environment.ExitCode=1;else Verify(directory);
    }
    internal static void Review(string parent,string output,bool synthetic=false)
    {
        parent=Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);output=Path.GetFullPath(output).TrimEnd(Path.DirectorySeparatorChar);Separate(parent,output);
        var original=Files(parent);var a=Read(Path.Combine(parent,"ascending"),offline:true);var b=Read(Path.Combine(parent,"descending"),offline:true);
        if(a.Pair.PairId!=b.Pair.PairId||a.Pair.SyntheticSource!=synthetic)throw new InvalidDataException("Matching native frontier conditions are required.");
        foreach(var item in new[]{a,b})Verify(Path.Combine(parent,item.Condition.Direction));
        var baseline=StandardStraightPilotCli.Frame(Path.Combine(parent,"ascending/baseline.png"),a.Pair.Context.Canvas,synthetic);
        var reversedBaseline=StandardStraightPilotCli.Frame(Path.Combine(parent,"descending/baseline.png"),a.Pair.Context.Canvas,synthetic);
        StandardTrackPairCli.CleanBaseline(View(a.Pair,a.Condition),baseline,reversedBaseline);
        uint Pid(string condition){using var j=JsonDocument.Parse(File.ReadAllText(Path.Combine(parent,condition,"live-context.json")));return j.RootElement.GetProperty("windowProcessId").GetUInt32();}
        if(Pid("ascending")!=Pid("descending"))throw new InvalidDataException("Rust перезапущено між напрямками вимірювання.");
        Directory.CreateDirectory(output);var models=new List<object>();
        foreach(var grid in a.Pair.Cases)
        {
            StandardBandResponse Import(Condition m)
            {
                var c=m.Cases.Single(c=>c.SourceSample==grid.SourceSample);string folder=Path.Combine(parent,m.Direction,$"sample-{c.Sample.Index:D2}","black-band");
                PixelImage Frame(string name)=>StandardStraightPilotCli.Frame(Path.Combine(folder,name),a.Pair.Context.Canvas,synthetic);
                var context=new StandardBandResponseContext(a.Pair.Context,a.Pair.AppSha256,a.Pair.CoreSha256,StandardContourMotion.Revision,c.Sample.Speed,StandardTrackPairCli.CapturePolicy);
                var model=new StandardBandResponse(context,grid.Target,c.Sample.Reservation,Frame("before.png"));
                for(int i=0;i<c.Paths.Length;i++)model.Add($"{m.ProtocolId}/{c.Sample.Index}/{i}",c.Paths[i],Frame($"stroke-{i:D3}-before.png"),Frame($"stroke-{i:D3}-after.png"));return model;
            }
            var ascending=Import(a.Condition);var descending=Import(b.Condition);var model=new StandardDiscreteBand(ascending,descending);var candidate=model.BestStraight(14);
            models.Add(new{grid.SourceSample,model,candidate,status=candidate is null?"UNAVAILABLE_MISSING_NATIVE_FRONTIER":"DISCRETE_STRAIGHT_HYPOTHESIS_REQUIRES_PHYSICAL_VALIDATION",
                sourceAscending=ascending,sourceDescending=descending,fullPossibleOfNewRoutePredicted=false,coverageCredit=0});
        }
        if(JsonSerializer.Serialize(original)!=JsonSerializer.Serialize(Files(parent)))throw new IOException("Frontier evidence changed during review.");
        File.WriteAllText(Path.Combine(output,"discrete-models.json"),JsonSerializer.Serialize(models,Json));
        File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{revision=StandardDiscreteBand.Revision,policy=StandardDiscreteBand.Policy,
            status="DISCRETE_FRONTIERS_NOT_PHYSICALLY_QUALIFIED",models=models.Count,pairId=a.Pair.PairId,sourceFiles=original.Count,sourceUnchanged=true,
            interpolation=false,turnModelAvailable=false,ordinaryStartChanged=false,dispatchAllowed=false,physicalQualityCertified=false,nativeInput=false,synthetic},Json));
    }
}
