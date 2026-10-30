using System.IO.Compression;
using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

// A new instrumented pair. Neither condition is dispatched by ordinary START.
internal static class StandardTrackPairCli
{
    internal const string Revision="standard-track-pair-v1";
    internal const string CapturePolicy="native before and after EACH released track; existing stable capture/parking; unchanged motion; inter-track cadence altered by capture and is not production timing";
    internal sealed record TrackCase(int SourceSample,int Shift,string ModelId,EvidenceSeriesCase Sample,ScreenPoint[][] Paths);
    internal sealed record Manifest(string Revision,string Condition,string PairId,StandardGeometryContext Context,TrackCase[] Cases,string ProtocolId,
        string SourceProtocolId,Dictionary<string,string> SourceFiles,string SettingsSha256,string AppSha256,string CoreSha256,bool SyntheticSource,string CapturePolicy);
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    private static string Hash(string p)=>StandardBoundaryReview.Hash(p);
    private static string Id(object x)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(x))).ToLowerInvariant();
    private static Dictionary<string,string> Files(string root)=>Directory.GetFiles(root,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal)
        .ToDictionary(p=>Path.GetRelativePath(root,p).Replace('\\','/'),Hash,StringComparer.Ordinal);
    private static TrackCase[] Cases((StandardContourPilotCli.Manifest Source,StandardStraightPilotCli.BandCase[] Cases) built,string condition)
    {
        if(condition is not ("control" or "shifted"))throw new InvalidDataException("Unknown track-pair condition.");
        return new[]{13,19,12}.Select((id,index)=>
        {
            var band=built.Cases.Single(c=>c.SourceSample==id);
            var paths=(condition=="control"?built.Source.Cases.Single(c=>c.SourceSample==id).Paths:band.Paths).Select(p=>p.ToArray()).ToArray();
            var lines=paths.Select(p=>band.Sample.Reverse?new ScreenLine(p[^1].X,p[^1].Y,p[0].X,p[0].Y):new(p[0].X,p[0].Y,p[^1].X,p[^1].Y)).ToArray();
            return new TrackCase(id,condition=="control"?0:band.Shift,band.ModelId,
                band.Sample with{Index=index,Kind="track-increment-band",Stages=[new("black-band",new(0,0,0),lines)]},paths);
        }).ToArray();
    }
    private static string PairId((StandardContourPilotCli.Manifest Source,StandardStraightPilotCli.BandCase[] Cases) built,Dictionary<string,string> files,string app,string core)
        =>Id(new{Revision,context=built.Source.Context.Id,source=built.Source.ProtocolId,files,app,core,CapturePolicy,
            trackCaptureRevision=StandardEvidenceSeriesRunner.TrackCaptureRevision,control=Cases(built,"control"),shifted=Cases(built,"shifted")});
    private static string ProtocolId(string pair,string condition,TrackCase[] cases)=>Id(new{Revision,pair,condition,cases,CapturePolicy});
    internal static void Init(string reference,string parent,bool synthetic=false)
    {
        reference=Path.GetFullPath(reference).TrimEnd(Path.DirectorySeparatorChar);parent=Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);
        bool Within(string a,string b)=>a.Equals(b,StringComparison.OrdinalIgnoreCase)||a.StartsWith(b+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        if(Within(reference,parent)||Within(parent,reference)||Directory.Exists(parent)||File.Exists(parent))throw new IOException("Use a new separate track-pair directory.");
        var built=StandardStraightPilotCli.Build(reference,synthetic);var files=Files(reference);Directory.CreateDirectory(parent);
        try
        {
            string source=Path.Combine(parent,"source");Directory.CreateDirectory(source);
            foreach(string name in files.Keys){string p=Path.Combine(source,name);Directory.CreateDirectory(Path.GetDirectoryName(p)!);File.Copy(Path.Combine(reference,name),p);}
            if(JsonSerializer.Serialize(files)!=JsonSerializer.Serialize(Files(reference))||JsonSerializer.Serialize(files)!=JsonSerializer.Serialize(Files(source)))
                throw new IOException("Source changed during track-pair initialization.");
            string app=Hash(typeof(Painter).Assembly.Location),core=Hash(typeof(Settings).Assembly.Location),pair=PairId(built,files,app,core);
            foreach(string condition in new[]{"control","shifted"})
            {
                string directory=Path.Combine(parent,condition);Directory.CreateDirectory(directory);File.Copy(Path.Combine(source,"settings.json"),Path.Combine(directory,"settings.json"));
                var cases=Cases(built,condition);var m=new Manifest(Revision,condition,pair,built.Source.Context,cases,ProtocolId(pair,condition,cases),built.Source.ProtocolId,
                    files,Hash(Path.Combine(directory,"settings.json")),app,core,synthetic,CapturePolicy);
                string path=Path.Combine(directory,"protocol.json");File.WriteAllText(path,JsonSerializer.Serialize(m,Json));File.WriteAllText(path+".sha256",Hash(path));
                File.WriteAllText(Path.Combine(directory,"series-status.json"),JsonSerializer.Serialize(new{status="NOT_STARTED",requiredCases=3,requiredStages=3,requiredStrokes=30,
                    requiredStrokeCaptures=30,recordedNativeFrames=72,nativeInput=false,physicalQualityCertified=false,automaticRetry=false,automaticResume=false}));
                Read(directory,offline:true);
            }
            File.WriteAllText(Path.Combine(parent,"pair-policy.json"),JsonSerializer.Serialize(new{revision=Revision,pairId=pair,conditions=new[]{"control","shifted"},
                sourceSamples=new[]{13,19,12},sourceIsHistorical=true,comparisonUsesNewSameBuildControl=true,CapturePolicy,
                order="control then shifted; independently clear Canvas between conditions; one manual attempt each",mapping="identity",
                strokesPerCondition=30,strokeCapturesPerCondition=30,nativeFramesPerCondition=72,ordinaryStartChanged=false,
                complete="all 30 emitted strokes AND all 30 linked before/after/trace captures; any missing mandatory artifact is INCOMPLETE",
                analysis="compare per-track changed-pixel increments and final-band 45/50/55% edges, absolute drift, width, caps; descriptive, no production qualification",
                thresholds="RGB12/contrast80 unchanged; no profile promotion or retuning",physicalQualityCertified=false},Json));
        }
        catch(Exception error){File.WriteAllText(Path.Combine(parent,"init-failed.json"),JsonSerializer.Serialize(new{status="INCOMPLETE",reason=error.Message,nativeInput=false}));throw;}
    }
    internal static Manifest Read(string directory,bool offline=false)
    {
        if(!offline&&(File.Exists(Path.Combine(directory,"attempted.json"))||File.Exists(Path.Combine(directory,"started.json"))))throw new IOException("Цю спробу вже запускали. Її дані збережено; повторне малювання заборонене.");
        string path=Path.Combine(directory,"protocol.json");if(Hash(path)!=File.ReadAllText(path+".sha256").Trim())throw new InvalidDataException("Track-pair protocol changed.");
        var m=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path))??throw new InvalidDataException("Missing track-pair protocol.");
        string source=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(directory))!,"source");var files=Files(source);
        if(m.Revision!=Revision||m.CapturePolicy!=CapturePolicy||m.SourceFiles.Count!=files.Count||files.Any(p=>!m.SourceFiles.TryGetValue(p.Key,out string? h)||h!=p.Value)
            ||m.SettingsSha256!=Hash(Path.Combine(directory,"settings.json"))||m.SettingsSha256!=Hash(Path.Combine(source,"settings.json"))
            ||!offline&&(m.SyntheticSource||m.AppSha256!=Hash(typeof(Painter).Assembly.Location)||m.CoreSha256!=Hash(typeof(Settings).Assembly.Location)))
            throw new InvalidDataException("Track-pair source, settings or binary changed.");
        var built=StandardStraightPilotCli.Build(source,m.SyntheticSource);var expected=Cases(built,m.Condition);
        if(m.Context.Id!=built.Source.Context.Id||m.SourceProtocolId!=built.Source.ProtocolId||JsonSerializer.Serialize(expected)!=JsonSerializer.Serialize(m.Cases)
            ||m.PairId!=PairId(built,files,m.AppSha256,m.CoreSha256)||m.ProtocolId!=ProtocolId(m.PairId,m.Condition,m.Cases))throw new InvalidDataException("Frozen per-track paths changed.");
        if(!offline&&m.Condition=="shifted")RequireCompleteControl(directory,m);
        return m;
    }
    internal static void RequireCompleteControl(string directory,Manifest shifted)
    {
        string control=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(directory))!,"control");var m=Read(control,offline:true);
        if(shifted.Condition!="shifted"||m.Condition!="control"||m.PairId!=shifted.PairId||m.AppSha256!=shifted.AppSha256||m.CoreSha256!=shifted.CoreSha256
            ||m.SyntheticSource!=shifted.SyntheticSource)throw new InvalidDataException("A matching same-build control is required.");
        ValidateCompleted(control,m);
    }
    internal static void ValidateCompleted(string directory,Manifest m,bool requireCompleteStatus=true,int shapeSlot=3)
    {
        using var status=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"series-status.json")));var root=status.RootElement;
        if(!root.TryGetProperty("result",out var result))throw new InvalidDataException("Контрольний запуск ще не завершений.");
        int expectedCases=m.Cases.Length,expectedStrokes=m.Cases.Sum(c=>c.Paths.Length);
        if(requireCompleteStatus&&result.GetProperty("Status").GetString()!="DATA_COLLECTED"||result.GetProperty("CompletedCases").GetInt32()!=expectedCases||result.GetProperty("CompletedStages").GetInt32()!=expectedCases
            ||result.GetProperty("CompletedStrokes").GetInt32()!=expectedStrokes||root.GetProperty("emittedStrokes").GetInt32()!=expectedStrokes||root.GetProperty("completedStrokeCaptures").GetInt32()!=expectedStrokes
            ||!root.GetProperty("captureEachStroke").GetBoolean()||root.GetProperty("trackCaptureRevision").GetString()!=StandardEvidenceSeriesRunner.TrackCaptureRevision)
            throw new InvalidDataException($"Потрібні всі {expectedStrokes} штрихів і {expectedStrokes} пар кадрів пробного запуску.");
        var canvas=m.Context.Canvas;
        using var live=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"live-context.json")));var env=live.RootElement;
        if(env.GetProperty("protocolId").GetString()!=m.ProtocolId||env.GetProperty("emitter").GetString()!=StandardContourMotion.Revision
            ||env.GetProperty("windowDpi").GetInt32()!=m.Context.Dpi||env.GetProperty("origin").Deserialize<ScreenPoint>()!=m.Context.ClientOrigin
            ||env.GetProperty("client").Deserialize<ScreenSize>()!=m.Context.ClientSize||env.GetProperty("mapping").GetString()!="identity"
            ||env.GetProperty("size").GetDouble()!=3||env.GetProperty("shape").GetInt32()!=shapeSlot||env.GetProperty("captures").GetString()!=CapturePolicy
            ||env.GetProperty("speed").Deserialize<SpeedProfile>()!=StandardEvidenceSeries.Speed
            ||env.GetProperty("productionBridgeSpeed").Deserialize<SpeedProfile>()!=StandardEvidenceSeries.ProductionSpeed)
            throw new InvalidDataException("Recorded track-pair context differs from the frozen protocol.");
        PixelImage Frame(string path)=>StandardStraightPilotCli.Frame(path,canvas,m.SyntheticSource);
        var baseline=Frame(Path.Combine(directory,"baseline.png"));var confirm=Frame(Path.Combine(directory,"baseline-confirm.png"));var final=Frame(Path.Combine(directory,"final.png"));
        if(!EvidenceRegionCheck.Same(baseline,confirm,canvas,canvas))throw new InvalidDataException("Control baseline noise exceeds RGB12.");
        int count=0;
        foreach(var c in m.Cases)
        {
            string folder=Path.Combine(directory,$"sample-{c.Sample.Index:D2}"),stage=Path.Combine(folder,"black-band");
            if(JsonSerializer.Serialize(JsonSerializer.Deserialize<EvidenceSeriesCase>(File.ReadAllText(Path.Combine(folder,"sample.json"))))!=JsonSerializer.Serialize(c.Sample))
                throw new InvalidDataException("Recorded per-track sample changed.");
            var before=Frame(Path.Combine(folder,"before.png"));var current=Frame(Path.Combine(stage,"before.png"));
            if(!EvidenceRegionCheck.Same(baseline,before,canvas,c.Sample.Reservation)||!EvidenceRegionCheck.Same(before,current,canvas,canvas))throw new InvalidDataException("Control area was not clean.");
            for(int i=0;i<c.Paths.Length;i++)
            {
                string stem=Path.Combine(stage,$"stroke-{i:D3}"),trace=stem+".json.gz";
                var a=Frame(stem+"-before.png");var b=Frame(stem+"-after.png");
                using var pair=JsonDocument.Parse(File.ReadAllText(stem+"-pair.json"));var p=pair.RootElement;
                string Rgba(PixelImage x)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(x.Rgba)).ToLowerInvariant();
                if(p.GetProperty("revision").GetString()!=StandardEvidenceSeriesRunner.TrackCaptureRevision||p.GetProperty("protocolId").GetString()!=m.ProtocolId
                    ||p.GetProperty("sample").GetInt32()!=c.Sample.Index||p.GetProperty("stage").GetString()!="black-band"||p.GetProperty("track").GetInt32()!=i
                    ||p.GetProperty("before").GetString()!=Path.GetFileName(stem+"-before.png")||p.GetProperty("after").GetString()!=Path.GetFileName(stem+"-after.png")
                    ||p.GetProperty("trace").GetString()!=Path.GetFileName(trace)||p.GetProperty("traceSha256").GetString()!=Hash(trace)
                    ||p.GetProperty("beforeRgbaSha256").GetString()!=Rgba(a)||p.GetProperty("afterRgbaSha256").GetString()!=Rgba(b)
                    ||p.GetProperty("synthetic").GetBoolean()!=m.SyntheticSource||p.GetProperty("nativeInput").GetBoolean()==m.SyntheticSource
                    ||!EvidenceRegionCheck.Same(current,a,canvas,canvas)||!EvidenceRegionCheck.ChangesContained(a,b,canvas,c.Sample.Reservation))
                    throw new InvalidDataException("Per-track frame/trace link is incomplete or changed.");
                StandardStraightPilotCli.Trace(trace,new(c.SourceSample,c.ModelId,c.Sample,c.Paths),i,m.SyntheticSource);
                using var tf=File.OpenRead(trace);using var zip=new GZipStream(tf,CompressionMode.Decompress);using var tr=JsonDocument.Parse(zip);
                using var bm=JsonDocument.Parse(File.ReadAllText(stem+"-before.json"));using var am=JsonDocument.Parse(File.ReadAllText(stem+"-after.json"));
                var bc=bm.RootElement.GetProperty("captureWindow");var ac=am.RootElement.GetProperty("captureWindow");var samples=tr.RootElement.GetProperty("Samples");long freq=tr.RootElement.GetProperty("qpcFrequency").GetInt64();
                if(bc.GetProperty("qpcFrequency").GetInt64()!=freq||ac.GetProperty("qpcFrequency").GetInt64()!=freq
                    ||bc.GetProperty("finishedQpc").GetInt64()>samples[0].GetProperty("StartedQpc").GetInt64()
                    ||samples[samples.GetArrayLength()-1].GetProperty("FinishedQpc").GetInt64()>ac.GetProperty("startedQpc").GetInt64())
                    throw new InvalidDataException("Capture and input ordering differ from the protocol.");
                current=b;count++;
            }
            var after=Frame(Path.Combine(stage,"after.png"));
            if(!EvidenceRegionCheck.Same(current,after,canvas,canvas)||!EvidenceRegionCheck.Same(after,final,canvas,c.Sample.Reservation)
                ||!after.Rgba.SequenceEqual(Images.LoadNative(Path.Combine(folder,"after.png")).Rgba))throw new InvalidDataException("Recorded final band changed.");
        }
        if(count!=expectedStrokes||Directory.GetFiles(directory,"stroke-*-pair.json",SearchOption.AllDirectories).Length!=expectedStrokes
            ||Directory.GetFiles(directory,"stroke-*.json.gz",SearchOption.AllDirectories).Length!=expectedStrokes)throw new InvalidDataException("Missing or extra track evidence.");
    }
    internal static void CleanBaseline(Manifest m,PixelImage expected,PixelImage current)
    {
        if(m.Cases.Any(c=>!EvidenceRegionCheck.Same(expected,current,m.Context.Canvas,c.Sample.Reservation)))
            throw new InvalidDataException("Очисти полотно червоним кошиком. Для другого запуску теж потрібне чисте полотно.");
    }
    internal static void Run(string directory)
    {
        var m=Read(directory);StandardEvidenceSeriesRunner.Result? result=null;
        StandardEvidenceSeriesCli.RunCollection(directory,m.Context.Canvas,3,15,p=>result=p.CollectTrackPair(m,directory));
        if(result?.Status!="DATA_COLLECTED")Environment.ExitCode=1;
        else ValidateCompleted(directory,m);
    }
}
