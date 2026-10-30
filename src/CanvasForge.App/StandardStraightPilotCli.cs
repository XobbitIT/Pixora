using System.IO.Compression;
using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

internal static class StandardStraightPilotCli
{
    internal const string Revision="standard-measured-straight-band-pilot-v1";
    internal sealed record BandCase(int SourceSample,string ModelId,int Shift,double SourceError,double TranslationEstimate,StandardEdgeSection[] Sections,
        EvidenceSeriesCase Sample,ScreenPoint[][] Paths);
    internal sealed record Manifest(string Revision,StandardGeometryContext Context,BandCase[] Cases,string ProtocolId,
        string SourceProtocolId,Dictionary<string,string> ReferenceFiles,string SettingsSha256,string AppSha256,string CoreSha256,bool SyntheticSource);
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    private static string Hash(string p)=>StandardBoundaryReview.Hash(p);
    private static string Identity(StandardGeometryContext context,BandCase[] cases,string source,Dictionary<string,string> files)
        =>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{Revision,context=context.Id,cases,source,files}))).ToLowerInvariant();
    private static Dictionary<string,string> Files(string root)=>Directory.GetFiles(root,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal)
        .ToDictionary(p=>Path.GetRelativePath(root,p).Replace('\\','/'),Hash,StringComparer.Ordinal);
    internal static PixelImage Frame(string path,ScreenRect canvas,bool synthetic)
    {
        var image=Images.LoadNative(path);using var doc=JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(path,".json")));var m=doc.RootElement;
        if(image.Width!=canvas.Width||image.Height!=canvas.Height||m.GetProperty("canvas").Deserialize<ScreenRect>()!=canvas
            ||m.GetProperty("synthetic").GetBoolean()!=synthetic||m.GetProperty("nativeInput").GetBoolean()==synthetic
            ||!Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(image.Rgba)).Equals(m.GetProperty("rgbaSha256").GetString(),StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Reference native frame or its metadata changed.");
        var capture=m.GetProperty("captureWindow");
        if(capture.GetProperty("qpcFrequency").GetInt64()<=0||capture.GetProperty("finishedQpc").GetInt64()<capture.GetProperty("startedQpc").GetInt64())
            throw new InvalidDataException("Reference capture timing is invalid.");
        return image;
    }
    internal static void Trace(string path,StandardContourPilotCli.PilotCase @case,int index,bool synthetic)
    {
        using var f=File.OpenRead(path);using var gzip=new GZipStream(f,CompressionMode.Decompress);using var doc=JsonDocument.Parse(gzip);var t=doc.RootElement;
        var expected=@case.Paths[index];
        if(t.GetProperty("nativeInput").GetBoolean()==synthetic||t.GetProperty("problem").ValueKind!=JsonValueKind.Null
            ||t.GetProperty("pauseReason").ValueKind!=JsonValueKind.Null||t.GetProperty("emitter").GetString()!=StandardContourMotion.Revision
            ||t.GetProperty("requested").GetString()!="000000"||t.GetProperty("qpcFrequency").GetInt64()<=0
            ||t.GetProperty("sample").GetInt32()!=@case.Sample.Index||t.GetProperty("reverse").GetBoolean()!=@case.Sample.Reverse
            ||t.GetProperty("line").Deserialize<ScreenLine>()!=@case.Sample.Stages[0].Lines[index]
            ||t.GetProperty("speed").Deserialize<SpeedProfile>()!=@case.Sample.Speed
            ||!t.GetProperty("actualPath").Deserialize<ScreenPoint[]>()!.SequenceEqual(expected))throw new InvalidDataException("Reference input differs from its frozen straight control.");
        var points=new List<ScreenPoint>();long previous=0;int held=0,released=0,confirmed=0;
        foreach(var s in t.GetProperty("Samples").EnumerateArray())
        {
            long started=s.GetProperty("StartedQpc").GetInt64(),finished=s.GetProperty("FinishedQpc").GetInt64();string phase=s.GetProperty("Phase").GetString()!;
            var command=s.GetProperty("Command").Deserialize<ScreenPoint>();
            if(started<previous||finished<started||!s.GetProperty("CursorReadSucceeded").GetBoolean()||s.GetProperty("Cursor").Deserialize<ScreenPoint>()!=command
                ||s.GetProperty("RequestedWait").GetDouble()<0)throw new InvalidDataException("Reference cursor or timing is incomplete.");previous=finished;
            var issued=s.GetProperty("IssuedPoints");
            if(issued.ValueKind!=JsonValueKind.Null)
            {
                var packet=issued.Deserialize<ScreenPoint[]>()!;
                if(phase!="move-issued"||packet.Length is <1 or >48||points.Count>0&&(held!=1||released!=0))throw new InvalidDataException("Reference packet lifecycle is invalid.");
                points.AddRange(packet);
            }
            if(points.Count==0||command!=points[^1])throw new InvalidDataException("Reference cursor command has no corresponding issued point.");
            if(phase=="held"){if(held!=0||released!=0||points.Count!=1||confirmed!=1)throw new InvalidDataException("Reference down is invalid.");held++;}
            if(phase=="released"){if(held!=1||released!=0||!points.SequenceEqual(expected))throw new InvalidDataException("Reference release is invalid.");released++;}
            if(phase=="endpoint-confirmed")
            {if(!(confirmed==0&&held==0&&points.Count==1||confirmed==1&&released==1))throw new InvalidDataException("Reference confirmation is invalid.");confirmed++;}
        }
        if(held!=1||released!=1||confirmed!=2||!points.SequenceEqual(expected))throw new InvalidDataException("Reference input lifecycle is incomplete.");
    }
    internal static (StandardContourPilotCli.Manifest Source,BandCase[] Cases) Build(string reference,bool synthetic)
    {
        var source=StandardContourPilotCli.Read(reference,offline:true);
        using var state=JsonDocument.Parse(File.ReadAllText(Path.Combine(reference,"series-status.json")));var result=state.RootElement.GetProperty("result");
        if(source.Condition!="control"||source.SyntheticSource!=synthetic||result.GetProperty("Status").GetString()!="DATA_COLLECTED"
            ||result.GetProperty("CompletedCases").GetInt32()!=5||result.GetProperty("CompletedStages").GetInt32()!=5||result.GetProperty("CompletedStrokes").GetInt32()!=50)
            throw new InvalidDataException("A complete matching straight control is required.");
        var settings=Settings.Load(Path.Combine(reference,"settings.json"));using var live=JsonDocument.Parse(File.ReadAllText(Path.Combine(reference,"live-context.json")));
        var env=live.RootElement;var context=source.Context;context.Validate();
        // The source context names the ordinary geometry contract; the actual
        // experiment emitter is recorded separately and must match exactly.
        if(env.GetProperty("emitter").GetString()!=StandardContourMotion.Revision||env.GetProperty("protocolId").GetString()!=source.ProtocolId
            ||env.GetProperty("origin").Deserialize<ScreenPoint>()!=context.ClientOrigin||env.GetProperty("client").Deserialize<ScreenSize>()!=context.ClientSize
            ||env.GetProperty("windowDpi").GetInt32()!=context.Dpi||env.GetProperty("shape").GetInt32()!=context.ShapeSlot
            ||env.GetProperty("size").GetDouble()!=context.Size||env.GetProperty("mapping").GetString()!="identity"
            ||settings.Calibration.Rect("canvas")!=context.Canvas||settings.Text("color_mode")!=context.ColorMode
            ||!ReleaseProfile.Standard(settings)||StandardLineMotion.Enabled(settings)||settings.Text("speed_profile")!="Rapid"
            ||StrokeTiming.Frame(settings)!=context.FrameSeconds||StrokeTiming.EndHold(settings,StandardRepeatSeries.Speed)!=context.EndHoldSeconds
            ||StrokeTiming.Release(settings)!=context.ReleaseSeconds)
            throw new InvalidDataException("Reference paint environment differs from the protocol.");
        var canvas=source.Context.Canvas;var baseline=Frame(Path.Combine(reference,"baseline.png"),canvas,synthetic);
        var confirmation=Frame(Path.Combine(reference,"baseline-confirm.png"),canvas,synthetic);var final=Frame(Path.Combine(reference,"final.png"),canvas,synthetic);
        if(!EvidenceRegionCheck.Same(baseline,confirmation,canvas,canvas))throw new InvalidDataException("Reference baseline noise exceeds RGB12.");
        var output=new List<BandCase>();
        foreach(var (c,d) in source.Cases.Zip(source.Design))
        {
            string folder=Path.Combine(reference,$"sample-{c.Sample.Index:D2}");
            var recorded=JsonSerializer.Deserialize<EvidenceSeriesCase>(File.ReadAllText(Path.Combine(folder,"sample.json")));
            if(JsonSerializer.Serialize(recorded)!=JsonSerializer.Serialize(c.Sample))
                throw new InvalidDataException("Reference sample differs from schedule.");
            var before=Frame(Path.Combine(folder,"before.png"),canvas,synthetic);var selected=Frame(Path.Combine(folder,"black-band","before.png"),canvas,synthetic);
            var after=Frame(Path.Combine(folder,"black-band","after.png"),canvas,synthetic);
            if(!EvidenceRegionCheck.Same(baseline,before,canvas,c.Sample.Reservation)||!EvidenceRegionCheck.Same(before,selected,canvas,canvas)
                ||!EvidenceRegionCheck.ChangesContained(selected,after,canvas,c.Sample.Reservation)||!EvidenceRegionCheck.Same(after,final,canvas,c.Sample.Reservation)
                ||!after.Rgba.SequenceEqual(Images.LoadNative(Path.Combine(folder,"after.png")).Rgba))throw new InvalidDataException("Reference band changed outside its recorded stage.");
            for(int i=0;i<c.Paths.Length;i++)Trace(Path.Combine(folder,"black-band",$"stroke-{i:D3}.json.gz"),c,i,synthetic);
            var rows=StandardBandMeasurement.Central(before,after,canvas,d.Command);
            var model=StandardRigidBand.Create(source.Context,d.Command,Hash(Path.Combine(reference,"protocol.json"))+"/"+d.SourceSample,rows,c.Paths);
            var paths=model.Candidate.Select(t=>t.Points.ToArray()).ToArray();var all=c.Paths.Concat(paths).SelectMany(p=>p).ToArray();
            var reservation=new ScreenRect(all.Min(p=>p.X)-16,all.Min(p=>p.Y)-16,all.Max(p=>p.X)+17,all.Max(p=>p.Y)+17);
            if(reservation.Left<canvas.Left||reservation.Top<canvas.Top||reservation.Right>canvas.Right||reservation.Bottom>canvas.Bottom
                ||output.Any(x=>reservation.Left<x.Sample.Reservation.Right&&reservation.Right>x.Sample.Reservation.Left&&reservation.Top<x.Sample.Reservation.Bottom&&reservation.Bottom>x.Sample.Reservation.Top))
                throw new InvalidDataException("Complete straight band reservation does not fit.");
            var lines=paths.Select(p=>c.Sample.Reverse?new ScreenLine(p[^1].X,p[^1].Y,p[0].X,p[0].Y):new(p[0].X,p[0].Y,p[^1].X,p[^1].Y)).ToArray();
            var sample=c.Sample with{Kind="measured-straight-band",Reservation=reservation,Stages=[new("black-band",new(0,0,0),lines)]};
            output.Add(new(c.SourceSample,model.Id,model.Shift,model.SourceMaximumEdgeError,model.TranslationEstimateError,model.MeasuredSections.ToArray(),sample,paths));
        }
        return(source,output.ToArray());
    }
    internal static void Init(string reference,string directory,bool synthetic=false)
    {
        reference=Path.GetFullPath(reference).TrimEnd(Path.DirectorySeparatorChar);directory=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        if(reference.Equals(directory,StringComparison.OrdinalIgnoreCase)||reference.StartsWith(directory+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)
            ||directory.StartsWith(reference+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Directory.Exists(directory)||File.Exists(directory))
            throw new IOException("Use a new separate straight-band directory.");
        var built=Build(reference,synthetic);var original=Files(reference);Directory.CreateDirectory(directory);
        string stored=Path.Combine(directory,"reference");Directory.CreateDirectory(stored);
        foreach(var file in original.Keys){string p=Path.Combine(stored,file);Directory.CreateDirectory(Path.GetDirectoryName(p)!);File.Copy(Path.Combine(reference,file),p);}
        if(JsonSerializer.Serialize(Files(stored))!=JsonSerializer.Serialize(original))throw new IOException("Reference changed while copying.");
        File.Copy(Path.Combine(reference,"settings.json"),Path.Combine(directory,"settings.json"));
        var m=new Manifest(Revision,built.Source.Context,built.Cases,Identity(built.Source.Context,built.Cases,built.Source.ProtocolId,original),built.Source.ProtocolId,
            original,Hash(Path.Combine(directory,"settings.json")),Hash(typeof(Painter).Assembly.Location),Hash(typeof(Settings).Assembly.Location),synthetic);
        string path=Path.Combine(directory,"protocol.json");File.WriteAllText(path,JsonSerializer.Serialize(m,Json));File.WriteAllText(path+".sha256",Hash(path));
        File.WriteAllText(Path.Combine(directory,"series-status.json"),JsonSerializer.Serialize(new{status="NOT_STARTED",requiredCases=5,requiredStrokes=50,
            nativeInput=false,physicalQualityCertified=false,automaticRetry=false,automaticResume=false}));
        Read(directory,offline:true);
    }
    internal static Manifest Read(string directory,bool offline=false)
    {
        if(!offline&&(File.Exists(Path.Combine(directory,"attempted.json"))||File.Exists(Path.Combine(directory,"started.json"))))throw new IOException("This attempt already started; preserve its evidence.");
        string path=Path.Combine(directory,"protocol.json");if(Hash(path)!=File.ReadAllText(path+".sha256").Trim())throw new InvalidDataException("Straight-band protocol changed.");
        var m=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path))??throw new InvalidDataException("Missing straight-band protocol.");
        string reference=Path.Combine(directory,"reference");var actual=Files(reference);
        if(m.Revision!=Revision||m.ReferenceFiles.Count!=actual.Count||actual.Any(p=>!m.ReferenceFiles.TryGetValue(p.Key,out string? h)||h!=p.Value)
            ||m.SettingsSha256!=Hash(Path.Combine(directory,"settings.json"))||m.SettingsSha256!=Hash(Path.Combine(reference,"settings.json"))
            ||!offline&&(m.SyntheticSource||m.AppSha256!=Hash(typeof(Painter).Assembly.Location)||m.CoreSha256!=Hash(typeof(Settings).Assembly.Location)))
            throw new InvalidDataException("Straight-band source, environment or binary changed.");
        var expected=Build(reference,m.SyntheticSource);
        if(m.Context.Id!=expected.Source.Context.Id||m.SourceProtocolId!=expected.Source.ProtocolId||JsonSerializer.Serialize(m.Cases)!=JsonSerializer.Serialize(expected.Cases)
            ||m.ProtocolId!=Identity(m.Context,m.Cases,m.SourceProtocolId,actual))throw new InvalidDataException("Frozen straight trajectories changed.");
        return m;
    }
    internal static void CleanBaseline(Manifest m,PixelImage before,PixelImage current)
    {
        if(m.Cases.Any(c=>!EvidenceRegionCheck.Same(before,current,m.Context.Canvas,c.Sample.Reservation)))
            throw new InvalidDataException("Полотно містить попередні лінії або змінилося. Очисти його червоним кошиком перед новою спробою.");
    }
    internal static void Run(string directory)
    {
        var m=Read(directory);StandardEvidenceSeriesRunner.Result? result=null;
        StandardEvidenceSeriesCli.RunCollection(directory,m.Context.Canvas,5,10,p=>result=p.CollectStraightPilot(m,directory));
        if(result?.Status!="DATA_COLLECTED")Environment.ExitCode=1;
    }
}
