using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

// Explicit one-attempt pilot of the generated corrected paths. Never called
// by ordinary START; neither the interpolation nor the pilot grants Solid.
internal static class StandardBandInversePilotCli
{
    internal const string Revision="standard-joint-band-inverse-pilot-v2-portable";
    internal sealed record PilotCase(int SourceSample,string CandidateId,EvidenceSeriesCase Sample,ScreenPoint[][] Paths);
    internal sealed record Manifest(string Revision,string SourceRoot,Dictionary<string,string> SourceFiles,string CandidateFileSha256,
        StandardGeometryContext Context,PilotCase[] Cases,string ProtocolId,string AppSha256,string CoreSha256,string SettingsSha256,bool SyntheticSource)
    {
        public Dictionary<string,string> ReferenceFiles{get;init;}=new(StringComparer.Ordinal);
    }
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    private static string Hash(string path)=>StandardBoundaryReview.Hash(path);
    private static Dictionary<string,string> Files(string root)=>Directory.GetFiles(root,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal)
        .ToDictionary(p=>Path.GetRelativePath(root,p).Replace('\\','/'),Hash,StringComparer.Ordinal);
    internal static string Identity(Manifest m)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(m with{ProtocolId=""}))).ToLowerInvariant();
    internal static void Init(string source,string output,bool synthetic=false)
    {
        source=Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);output=Path.GetFullPath(output).TrimEnd(Path.DirectorySeparatorChar);
        bool Within(string a,string b)=>a.Equals(b,StringComparison.OrdinalIgnoreCase)||a.StartsWith(b+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        if(Within(source,output)||Within(output,source)||Directory.Exists(output)||File.Exists(output))throw new IOException("Use a new inverse pilot directory outside source evidence.");
        // Complete raw frames and traces are validated before any protocol exists.
        Directory.CreateDirectory(output);string analysis=Path.Combine(output,"model");
        try
        {
            StandardBandResponseReview.Run(source,analysis,allowSynthetic:synthetic,inverse:true);
            var reference=StandardTrackPairCli.Read(Path.Combine(source,"control"),offline:true);var files=Files(source);
            var cases=Cases(reference,Path.Combine(analysis,"inverse-candidates.json"));
            if(cases.Length!=2||cases.Any(c=>c.Paths.Length==0))throw new InvalidDataException("Both complete vertical candidates are required; missing evidence cannot be skipped.");
            File.Copy(Path.Combine(source,"control/settings.json"),Path.Combine(output,"settings.json"));
            var m=new Manifest(Revision,source,files,Hash(Path.Combine(analysis,"inverse-candidates.json")),reference.Context,cases,"",
                Hash(typeof(Painter).Assembly.Location),Hash(typeof(Settings).Assembly.Location),Hash(Path.Combine(output,"settings.json")),synthetic)
                {ReferenceFiles=EmbedReference(source,output)};
            m=m with{ProtocolId=Identity(m)};string protocol=Path.Combine(output,"protocol.json");
            File.WriteAllText(protocol,JsonSerializer.Serialize(m,Json));File.WriteAllText(protocol+".sha256",Hash(protocol));
            File.WriteAllText(Path.Combine(output,"series-status.json"),JsonSerializer.Serialize(new{status="NOT_STARTED",requiredCases=2,
                requiredStrokes=cases.Sum(c=>c.Paths.Length),nativeInput=false,automaticRetry=false,automaticResume=false,physicalQualityCertified=false}));
            Read(output,offline:true);
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"init-failed.json"),JsonSerializer.Serialize(new{status="INCOMPLETE",reason=e.Message,nativeInput=false}));throw;}
    }
    private static readonly Dictionary<string,string> ReferenceNames=new(StringComparer.Ordinal)
    {
        ["reference/control-protocol.json"]="control/protocol.json",
        ["reference/control-protocol.json.sha256"]="control/protocol.json.sha256",
        ["reference/baseline.png"]="control/baseline.png",
        ["reference/baseline.json"]="control/baseline.json"
    };
    internal static Dictionary<string,string> EmbedReference(string source,string output)
    {
        var result=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var (local,original) in ReferenceNames)
        {
            string target=Path.Combine(output,local);Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(source,original),target);result.Add(local,Hash(target));
        }
        return result;
    }
    private static StandardTrackPairCli.Manifest Reference(string directory,Manifest m)
    {
        if(m.ReferenceFiles.Count!=ReferenceNames.Count||m.ReferenceFiles.Keys.Any(k=>!ReferenceNames.ContainsKey(k)))
            throw new InvalidDataException("У пакеті немає повного переносного еталона. Розпакуй новий архів повністю.");
        foreach(var (local,original) in ReferenceNames)
            if(!m.SourceFiles.TryGetValue(original,out var expected)||m.ReferenceFiles[local]!=expected||Hash(Path.Combine(directory,local))!=expected)
                throw new InvalidDataException("Еталон пробного запуску пошкоджений або змінений.");
        string protocol=Path.Combine(directory,"reference/control-protocol.json");
        if(Hash(protocol)!=File.ReadAllText(protocol+".sha256").Trim())throw new InvalidDataException("Reference protocol changed.");
        var reference=JsonSerializer.Deserialize<StandardTrackPairCli.Manifest>(File.ReadAllText(protocol))??throw new InvalidDataException("Missing embedded reference protocol.");
        if(reference.Revision!=StandardTrackPairCli.Revision||reference.Condition!="control"||reference.Context.Id!=m.Context.Id
            ||reference.SyntheticSource!=m.SyntheticSource||reference.SettingsSha256!=m.SettingsSha256
            ||reference.SourceFiles.Any(x=>!m.SourceFiles.TryGetValue("source/"+x.Key,out var h)||h!=x.Value))
            throw new InvalidDataException("Embedded reference provenance or context changed.");
        StandardStraightPilotCli.Frame(Path.Combine(directory,"reference/baseline.png"),m.Context.Canvas,m.SyntheticSource);
        return reference;
    }
    internal static PixelImage Baseline(string directory,Manifest m)=>StandardStraightPilotCli.Frame(Path.Combine(directory,"reference/baseline.png"),m.Context.Canvas,m.SyntheticSource);
    private static PilotCase[] Cases(StandardTrackPairCli.Manifest reference,string candidateFile)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(candidateFile));var cases=new List<PilotCase>();
        foreach(var row in doc.RootElement.EnumerateArray())
        {
            int id=row.GetProperty("sourceSample").GetInt32();var original=reference.Cases.Single(c=>c.SourceSample==id);
            if(!original.Sample.Vertical)continue;
            var c=row.GetProperty("candidate");if(c.ValueKind==JsonValueKind.Null)throw new InvalidDataException("Mandatory vertical inverse candidate is unavailable.");
            if(c.GetProperty("Revision").GetString()!=StandardBandInverse.Revision||c.GetProperty("ContextId").GetString()!=new StandardBandResponseContext(reference.Context,
                reference.AppSha256,reference.CoreSha256,StandardContourMotion.Revision,original.Sample.Speed,reference.CapturePolicy).Id
                ||c.GetProperty("DispatchAllowed").GetBoolean()||c.GetProperty("PhysicalQualityCertified").GetBoolean())throw new InvalidDataException("Unexpected inverse hypothesis contract.");
            var paths=c.GetProperty("Paths").Deserialize<ScreenPoint[][]>()??throw new InvalidDataException("Missing corrected paths.");
            if(paths.Length is <1 or >10||paths.Any(p=>p.Length!=401))throw new InvalidDataException("Incomplete inverse paths.");
            foreach(var p in paths){StandardBandContour.Validate(p,reference.Context.Canvas);if(p.Any(q=>q.X<original.Sample.Reservation.Left||q.X>=original.Sample.Reservation.Right||q.Y<original.Sample.Reservation.Top||q.Y>=original.Sample.Reservation.Bottom))throw new InvalidDataException("Inverse path outside reservation.");}
            var lines=paths.Select(p=>new ScreenLine(p[0].X,p[0].Y,p[^1].X,p[^1].Y)).ToArray();
            cases.Add(new(id,c.GetProperty("Id").GetString()!,original.Sample with{Index=cases.Count,Kind="joint-band-inverse-hypothesis",Stages=[new("black-band",new(0,0,0),lines)]},paths));
        }
        return cases.ToArray();
    }
    internal static Manifest Read(string directory,bool offline=false)
    {
        if(!offline&&(File.Exists(Path.Combine(directory,"attempted.json"))||File.Exists(Path.Combine(directory,"started.json"))))throw new IOException("Цю корекцію вже запускали; дані збережено, автоматичний повтор заборонений.");
        string path=Path.Combine(directory,"protocol.json");if(Hash(path)!=File.ReadAllText(path+".sha256").Trim())throw new InvalidDataException("Frozen inverse protocol changed.");
        var m=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path))??throw new InvalidDataException("Missing inverse protocol.");
        if(m.Revision!=Revision)throw new InvalidDataException("Це застарілий пробний пакет. Розпакуй новий переносний архів у окрему папку.");
        string candidates=Path.Combine(directory,"model/inverse-candidates.json");
        // SourceRoot is provenance only. Init already verified the complete
        // raw collection; a user may remove or move it after unpacking.
        if(m.ProtocolId!=Identity(m)||m.SourceFiles.Count==0
            ||m.CandidateFileSha256!=Hash(candidates)||m.SettingsSha256!=Hash(Path.Combine(directory,"settings.json"))
            ||!offline&&(m.SyntheticSource||m.AppSha256!=Hash(typeof(Painter).Assembly.Location)||m.CoreSha256!=Hash(typeof(Settings).Assembly.Location)))
            throw new InvalidDataException("Inverse source, candidate, settings or binary changed.");
        m.Context.Validate();var reference=Reference(directory,m);
        if(m.Context.Id!=reference.Context.Id||m.SyntheticSource!=reference.SyntheticSource||JsonSerializer.Serialize(m.Cases)!=JsonSerializer.Serialize(Cases(reference,candidates)))
            throw new InvalidDataException("Corrected paths or physical context changed.");
        return m;
    }
    internal static void LogLaunchFailure(string directory,Exception error)
    {
        try
        {
            if(!Directory.Exists(directory))return;
            File.AppendAllText(Path.Combine(directory,"launch-errors.jsonl"),JsonSerializer.Serialize(new{timeUtc=DateTimeOffset.UtcNow,
                phase="launch",error=error.ToString(),started=File.Exists(Path.Combine(directory,"started.json")),nativeInputByLogger=false})+"\n");
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){}
    }
    // The validated native frame/trace lifecycle is shared with Track Pair;
    // expected counts are derived from the frozen cases, never from output.
    internal static StandardTrackPairCli.Manifest ValidationView(Manifest m)=>new(StandardTrackPairCli.Revision,"inverse-pilot",m.ProtocolId,m.Context,
        m.Cases.Select(c=>new StandardTrackPairCli.TrackCase(c.SourceSample,0,c.CandidateId,c.Sample,c.Paths)).ToArray(),m.ProtocolId,"",
        m.SourceFiles,m.SettingsSha256,m.AppSha256,m.CoreSha256,m.SyntheticSource,StandardTrackPairCli.CapturePolicy);
    internal static void Run(string directory)
    {
        var m=Read(directory);StandardEvidenceSeriesRunner.Result? result=null;
        StandardEvidenceSeriesCli.RunCollection(directory,m.Context.Canvas,m.Cases.Length,10,p=>result=p.CollectBandInversePilot(m,directory));
        if(result?.Status!="DATA_COLLECTED")Environment.ExitCode=1;else StandardTrackPairCli.ValidateCompleted(directory,ValidationView(m));
    }
}
