using System.Security.Cryptography;
using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

// One explicit experiment, under the existing contour entry; never START.
internal static class StandardTexelPilot
{
    internal const string Revision="standard-texel-centered-pilot-v2";
    internal const string Policy="texel39-body240-1060-both-edges-control-precision-recall-v1";
    internal sealed record Line(string Label,bool Centered,int? Column,int Cross,ScreenPoint[] Path);
    internal sealed record PredictedEvent(int Row,int Sign);
    internal sealed record Prediction(string Label,PredictedEvent[] Events);
    internal sealed record Manifest(string Revision,string Policy,string ViewPolicy,StandardGeometryContext Context,Line[] Lines,Prediction[] Predictions,
        StandardTrackPairCli.TrackCase[] Cases,Dictionary<string,string> ReferenceFiles,string SettingsSha256,
        string AppSha256,string CoreSha256,string SourceUvProtocolId,string CollectionId,bool SyntheticSource,string ProtocolId);
    internal static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    internal static readonly Dictionary<string,string> Fixtures=new(StringComparer.Ordinal)
    {
        ["PLAN.json"]="045da4e9dadca9d16a74e649633f44062715428fd79fb2431f8dcb01fa4f697b",
        ["Paths.json"]="c4c6c512d24f1c7158c6068a1a25d3845e02e65d475aaaa572096e60a1c1aa2c",
        ["Predictions.json"]="0dc2f2701d627e1459b48e6625cab2352cead4de5be879bedd525df506b17797",
        ["Claude_Paths.json"]="7b580f8764eb3fbc7e1b21ac56e13291254e738fcfc8a37223a8d7c8b0683e8b"
    };
    internal static string Hash(string p)=>StandardBoundaryReview.Hash(p);
    private static bool Digest(string? s)=>s is {Length:64}&&s.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string Id(object value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value))).ToLowerInvariant();
    internal static StandardTexelColumnHypothesis Model(StandardGeometryContext context)
    {
        // Reuse the old diagnostic's exact-context gate, without changing its
        // paths, fixtures, verdict, or enabling either route in normal START.
        StandardUvPilot.BuildLines(context);
        var c=new StandardBandResponseContext(context,Hash(typeof(Painter).Assembly.Location),Hash(typeof(Settings).Assembly.Location),
            StandardContourMotion.Revision,StandardRepeatSeries.Speed,"released per-stroke native capture");
        return new(c,new(511.05,465.8570014489101,.00919832272676294,1518.4496393486306,-.009071760769555693,
            -.0028,987.5,.716,.2663,-.1562,3),new(584,200,1482,1101),"6932e7ea0503ae269ca17fc5d5bc956ff9719cc6784640daf61d78aa0cf749ad");
    }
    internal static Line[] BuildLines(StandardGeometryContext context)
    {
        var model=Model(context);int[] anchor=[600,800,995,1200,1400],column=[64,162,257,357,455],control=[638,840,1035,1240,1442];
        var lines=new List<Line>();
        for(int i=0;i<5;i++)
        {
            var path=model.CenteredVertical(model.Context,column[i],200,1100,.25).Points.ToArray();
            lines.Add(new(anchor[i]+"-TX",true,column[i],anchor[i],path));
            lines.Add(new(control[i]+"-C",false,null,control[i],Enumerable.Range(200,901).Select(y=>new ScreenPoint(control[i],y)).ToArray()));
        }
        foreach(var l in lines)StandardBandContour.Validate(l.Path,context.Canvas);
        for(int i=0;i<lines.Count;i++)for(int j=i+1;j<lines.Count;j++)
            if(lines[i].Path.Zip(lines[j].Path).Min(p=>Math.Abs(p.First.X-p.Second.X))<32)throw new InvalidDataException("Texel diagnostic paths overlap.");
        return lines.ToArray();
    }
    internal static Prediction[] Predict(StandardGeometryContext context,Line[] lines)
    {
        var model=Model(context);
        return lines.Select(l=>new Prediction(l.Label,l.Path.Zip(l.Path.Skip(1)).Where(p=>model.PredictedColumn(p.First)!=model.PredictedColumn(p.Second))
            .Select(p=>new PredictedEvent(p.Second.Y,Math.Sign(model.PredictedColumn(p.Second)-model.PredictedColumn(p.First)))).ToArray())).ToArray();
    }
    internal static StandardTrackPairCli.TrackCase[] BuildCases(StandardGeometryContext context)
    {
        var lines=BuildLines(context);var result=new List<StandardTrackPairCli.TrackCase>();
        for(int i=0;i<5;i++)
        {
            var paths=lines.Skip(i*2).Take(2).Select(l=>l.Path).ToArray();var points=paths.SelectMany(p=>p).ToArray();
            var reservation=new ScreenRect(points.Min(p=>p.X)-16,184,points.Max(p=>p.X)+17,1117);
            if(reservation.Left<context.Canvas.Left||reservation.Right>context.Canvas.Right||result.Any(c=>c.Sample.Reservation.Right>reservation.Left))
                throw new InvalidDataException("Texel observation groups overlap or leave Canvas.");
            var sample=new EvidenceSeriesCase(i,0,"texel-centered-diagnostic",true,false,900,reservation,
                [new("black-band",new(0,0,0),paths.Select(p=>new ScreenLine(p[0].X,p[0].Y,p[^1].X,p[^1].Y)).ToArray())]){Speed=StandardRepeatSeries.Speed};
            result.Add(new(i,0,Id(new{Revision,i,paths}),sample,paths));
        }
        return result.ToArray();
    }
    internal static void CheckFixtures(string fixtures,Line[] lines,Prediction[] predictions)
    {
        foreach(var (name,h) in Fixtures)if(Hash(Path.Combine(fixtures,name))!=h)throw new InvalidDataException("Frozen texel reference changed: "+name);
        using var vectors=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixtures,"Paths.json")));
        var expected=vectors.RootElement.GetProperty("lines").EnumerateArray().ToArray();
        if(expected.Length!=10||lines.Length!=10)throw new InvalidDataException("Ten texel paths are mandatory.");
        for(int i=0;i<10;i++)if(expected[i].GetProperty("label").GetString()!=lines[i].Label||!expected[i].GetProperty("path").Deserialize<ScreenPoint[]>()!.SequenceEqual(lines[i].Path))
            throw new InvalidDataException("Texel commands differ from the independently checked vectors.");
        using var forecast=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixtures,"Predictions.json")));
        var ps=forecast.RootElement.GetProperty("lines").EnumerateArray().ToArray();
        foreach(var prediction in predictions)
        {
            var p=ps.Single(p=>p.GetProperty("label").GetString()==prediction.Label);
            var events=p.GetProperty("events").EnumerateArray().Select(e=>new PredictedEvent(e.GetProperty("toY").GetInt32(),e.GetProperty("sign").GetInt32())).ToArray();
            if(!events.SequenceEqual(prediction.Events))throw new InvalidDataException("Frozen control forecast changed.");
        }
    }
    internal static void Init(string sourceUv,string directory,string? fixtures=null)
    {
        sourceUv=Path.GetFullPath(sourceUv);directory=Path.GetFullPath(directory);StandardUvPilot.Separate(sourceUv,directory);
        var source=StandardUvPilot.Read(sourceUv,offline:true);if(source.SyntheticSource)throw new InvalidDataException("Native UV source required.");
        StandardUvPilot.Verify(sourceUv,source);
        Create(directory,Settings.Load(Path.Combine(sourceUv,"settings.json")),source.Context,Path.Combine(sourceUv,"baseline.png"),
            Path.Combine(sourceUv,FrontierSessionCapture.FrameName),source.ProtocolId,fixtures??Path.Combine(AppContext.BaseDirectory,"Texel_Fixtures"));
    }
    internal static Manifest Create(string directory,Settings original,StandardGeometryContext context,string baseline,string geometry,string sourceId,string fixtures,bool synthetic=false)
    {
        directory=Path.GetFullPath(directory);StandardUvPilot.Separate(fixtures,directory);
        var lines=BuildLines(context);var predictions=Predict(context,lines);var cases=BuildCases(context);CheckFixtures(fixtures,lines,predictions);
        var settings=original.Clone();settings.Validate();
        if(!ReleaseProfile.Standard(settings)||StandardLineMotion.Enabled(settings)||settings.Text("speed_profile")!="Rapid"||StrokeTiming.Frame(settings)!=.02
            ||settings.PaintCalibration().Rect("canvas")!=context.Canvas||settings.Text("color_mode")!=context.ColorMode||!Digest(sourceId))
            throw new InvalidDataException("Texel settings or source context changed.");
        var guard=InputCommandGuard.From(settings,new(context.ClientOrigin.X,context.ClientOrigin.Y,context.ClientOrigin.X+context.ClientSize.Width,context.ClientOrigin.Y+context.ClientSize.Height));
        if(lines.Any(l=>!guard.Allows(l.Path)))throw new InvalidDataException("Texel path intersects a captured control.");
        StandardStraightPilotCli.Frame(baseline,context.Canvas,synthetic);
        if(!synthetic)StandardTexelView.ReadFrame(geometry);
        Directory.CreateDirectory(directory);var refs=new Dictionary<string,string>(StringComparer.Ordinal);
        void Copy(string from,string to){string target=Path.Combine(directory,to);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(from,target);refs.Add(to,Hash(target));}
        foreach(string name in Fixtures.Keys)Copy(Path.Combine(fixtures,name),"fixtures/"+name);
        Copy(baseline,"reference/baseline.png");Copy(Path.ChangeExtension(baseline,"json"),"reference/baseline.json");
        if(!synthetic){Copy(geometry,"reference/geometry.png");Copy(Path.ChangeExtension(geometry,"json"),"reference/geometry.json");}
        File.WriteAllText(Path.Combine(directory,"settings.json"),settings.Data.ToJsonString());
        var m=new Manifest(Revision,Policy,StandardTexelView.Policy,context,lines,predictions,cases,refs,Hash(Path.Combine(directory,"settings.json")),
            Hash(typeof(Painter).Assembly.Location),Hash(typeof(Settings).Assembly.Location),sourceId,Guid.NewGuid().ToString("N"),synthetic,"");
        m=m with{ProtocolId=Id(m)};StandardUvPilot.WriteNew(Path.Combine(directory,"protocol.json"),m);
        File.WriteAllText(Path.Combine(directory,"protocol.json.sha256"),Hash(Path.Combine(directory,"protocol.json")));
        StandardUvPilot.WriteNew(Path.Combine(directory,"series-status.json"),new{status="NOT_STARTED",requiredCases=5,requiredStrokes=10,mandatoryBodySections=8210,
            nativeInput=false,physicalQualityCertified=false,automaticRetry=false,automaticResume=false});
        Read(directory,offline:true);return m;
    }
    internal static Manifest Read(string directory,bool offline=false)
    {
        if(!offline&&(new[]{"attempted.json","started.json","events.jsonl","baseline.png","final.png",FrontierSessionCapture.FileName,"geometry-check.json","startup-failures.jsonl"}.Any(n=>File.Exists(Path.Combine(directory,n)))
            ||Directory.GetDirectories(directory,"sample-*").Length>0))throw new IOException("Цю спробу вже запускали. Дані не перезаписуються; повтор вимкнений.");
        string path=Path.Combine(directory,"protocol.json");if(Hash(path)!=File.ReadAllText(path+".sha256").Trim())throw new InvalidDataException("Texel manifest changed.");
        var m=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path))??throw new InvalidDataException("Missing texel manifest.");
        var expected=Fixtures.Keys.Select(n=>"fixtures/"+n).Concat(new[]{"reference/baseline.png","reference/baseline.json"}).Concat(m.SyntheticSource?[]:new[]{"reference/geometry.png","reference/geometry.json"}).ToArray();
        if(m.Revision!=Revision||m.Policy!=Policy||m.ViewPolicy!=StandardTexelView.Policy||m.ProtocolId!=Id(m with{ProtocolId=""})||!Guid.TryParseExact(m.CollectionId,"N",out _)
            ||!Digest(m.SourceUvProtocolId)||m.SettingsSha256!=Hash(Path.Combine(directory,"settings.json"))
            ||JsonSerializer.Serialize(m.Lines)!=JsonSerializer.Serialize(BuildLines(m.Context))||JsonSerializer.Serialize(m.Cases)!=JsonSerializer.Serialize(BuildCases(m.Context))
            ||JsonSerializer.Serialize(m.Predictions)!=JsonSerializer.Serialize(Predict(m.Context,m.Lines))
            ||m.ReferenceFiles.Count!=expected.Length||m.ReferenceFiles.Keys.Any(k=>!expected.Contains(k,StringComparer.Ordinal))
            ||!offline&&(m.SyntheticSource||m.AppSha256!=Hash(typeof(Painter).Assembly.Location)||m.CoreSha256!=Hash(typeof(Settings).Assembly.Location)))
            throw new InvalidDataException("Texel policy, paths, settings, or binaries changed.");
        CheckFixtures(Path.Combine(directory,"fixtures"),m.Lines,m.Predictions);
        foreach(var (name,h) in m.ReferenceFiles)if(Hash(Path.Combine(directory,name))!=h)throw new InvalidDataException("Texel reference changed.");
        var settings=Settings.Load(Path.Combine(directory,"settings.json"));
        if(settings.PaintCalibration().Rect("canvas")!=m.Context.Canvas||settings.Text("color_mode")!=m.Context.ColorMode||!ReleaseProfile.Standard(settings)
            ||StandardLineMotion.Enabled(settings)||settings.Text("speed_profile")!="Rapid"||StrokeTiming.Frame(settings)!=.02)throw new InvalidDataException("Texel settings context changed.");
        StandardStraightPilotCli.Frame(Path.Combine(directory,"reference/baseline.png"),m.Context.Canvas,m.SyntheticSource);
        return m;
    }
    internal static StandardTrackPairCli.Manifest View(Manifest m)=>new(StandardTrackPairCli.Revision,"texel-centered",m.ProtocolId,m.Context,m.Cases,m.ProtocolId,"",
        m.ReferenceFiles,m.SettingsSha256,m.AppSha256,m.CoreSha256,m.SyntheticSource,StandardTrackPairCli.CapturePolicy);
    internal static FrontierSessionCapture.Binding Binding(Manifest m)=>new(m.ProtocolId,m.SettingsSha256,m.AppSha256,m.CoreSha256,m.Context,m.SyntheticSource);
    internal static void Verify(string directory,Manifest? manifest=null,bool requireCompleteStatus=true)
    {
        var m=manifest??Read(directory,offline:true);StandardTrackPairCli.ValidateCompleted(directory,View(m),requireCompleteStatus);
        if(!m.SyntheticSource)
        {
            if(!File.Exists(Path.Combine(directory,FrontierSessionCapture.FileName)))throw new InvalidDataException("Fresh texel session proof missing.");
            FrontierSessionCapture.VerifyUse(directory,directory,Binding(m));StandardTexelView.Check(directory,publish:false);
        }
        var first=StandardStraightPilotCli.Frame(Path.Combine(directory,"baseline.png"),m.Context.Canvas,m.SyntheticSource);
        var old=StandardStraightPilotCli.Frame(Path.Combine(directory,"reference/baseline.png"),m.Context.Canvas,m.SyntheticSource);FrontierSessionCapture.RequireBaseline(old,first,m.Context.Canvas);
    }
    internal static void Run(string directory)
    {
        var m=Read(directory);StandardEvidenceSeriesRunner.Result? result=null;
        StandardEvidenceSeriesCli.RunCollection(directory,m.Context.Canvas,5,10,p=>result=p.CollectTexelPilot(m,directory),
            (settings,window)=>{var prepared=FrontierSessionCapture.CaptureFresh(settings,window,Binding(m),directory);StandardTexelView.Check(directory,publish:true);return prepared;});
        if(result?.Status!="DATA_COLLECTED")Environment.ExitCode=1;else Verify(directory,m);
    }
}
