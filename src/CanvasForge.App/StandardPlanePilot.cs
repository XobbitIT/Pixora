using System.Security.Cryptography;
using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

// Explicit research protocol through the existing contour entry. Not START,
// a qualifying profile, a new transport or permission inferred from a model.
internal static class StandardPlanePilot
{
    internal const string Revision="standard-texel-plane-pilot-v1";
    internal const string Policy="plane-position8-long2-control2-descriptive16-phase020-v1";
    internal sealed record Probe(string Label,string Role,bool Vertical,int? ConstantIndex,int BodyFirst,int BodyLast,ScreenPoint[][] Paths);
    internal sealed record FrozenModel(StandardTexelColumnParameters Columns,StandardTexelRowParameters Rows,string ColumnSource,string RowSource,string Compatibility);
    internal sealed record Manifest(string Revision,string Policy,string ViewPolicy,string BaselinePolicy,StandardGeometryContext Context,
        FrozenModel Model,Probe[] Probes,StandardTrackPairCli.TrackCase[] Cases,Dictionary<string,string> ReferenceFiles,string SettingsSha256,
        string AppSha256,string CoreSha256,string SourceRowProtocolId,string CollectionId,string ProtocolId,bool SyntheticSource);
    internal static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    internal static readonly Dictionary<string,string> Assets=new(StringComparer.Ordinal)
    {
        ["blueprint-v2.json"]="4c20c70681eef95ba0882eae9b45085264e5ebbea75463bd83c373c048ac2bf6",
        ["phase-policy-v1.json"]="9d0752f752c3d6b6cb772c07587a17a4fab5eee3da3081cd55f7eae1ca42abe9",
        ["execution-policy-v1.json"]="ab2152cc40280db6031c6cdef373a35d4c498b990ce2e6fa56d1f0d7809e196d"
    };
    internal static FrozenModel Parameters()=>new(new(511.05,465.8570014489101,.00919832272676294,1518.4496393486306,-.009071760769555693,
        -.0028,987.5,.716,.2663,-.1562,3),new(.4915400000000115,57612.96453407507,-.0018000000000000498,670,.8,-1,.24339025634890218,-.11300837053563842,3),
        "6932e7ea0503ae269ca17fc5d5bc956ff9719cc6784640daf61d78aa0cf749ad","d7f5b23ab992dbd13d5f902685c8a9c6e39a35ae63210f58592980d0796434d2","9fc45c42b754963685c91bbdfca41f7d65b2e21724f6edc0dd452394dc9f836a");
    internal static string Hash(string path)=>StandardBoundaryReview.Hash(path);
    internal static string Id(object value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value))).ToLowerInvariant();
    private static bool Digest(string? s)=>s is {Length:64}&&s.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
    internal static StandardTexelPlaneHypothesis Model(StandardGeometryContext context)
    {
        _ = StandardRowPilot.BuildLines(context); // Exact palette and row geometry, not just column-compatible geometry.
        var columns=StandardTexelPilot.Model(context);var p=Parameters();
        return new(columns,new(columns.Context,p.Rows,new(520,220,1431,1101),p.RowSource),p.Compatibility);
    }
    internal static Probe[] BuildProbes(StandardGeometryContext context)
    {
        var model=Model(context);var result=new List<Probe>();
        void Add(string label,string role,bool vertical,int? constant,ScreenPoint[] path,int trim=16)
        {int first=vertical?path[0].Y:path[0].X,last=vertical?path[^1].Y:path[^1].X;result.Add(new(label,role,vertical,constant,first+trim,last-trim,[path]));}
        foreach(var (label,row,a,b) in new[]{("H1",133,155,195),("H2",206,260,300),("H3",331,140,180),("H4",406,280,320)})
            Add(label,"position",false,row,model.DenseHorizontal(model.Context,row,a,b,.25).Points.ToArray());
        foreach(var (label,col,a,b) in new[]{("V1",243,120,160),("V2",366,200,240),("V3",241,320,360),("V4",377,400,440)})
            Add(label,"position",true,col,model.DenseVertical(model.Context,col,a,b,.25).Points.ToArray());
        foreach(var (label,col,x) in new[]{("LongLeft",70,693),("LongRight",460,1329)})
        {
            var points=model.DenseVertical(model.Context,col,120,545,.25).Points.ToArray();
            Add(label,"mechanism-centered",true,col,points,40);
            Add(label+"Control","screen-control",true,null,points.Select(p=>new ScreenPoint(x,p.Y)).ToArray(),40);
        }
        result.Add(new("Block16","descriptive-only",false,null,0,0,Enumerable.Range(470,16)
            .Select(row=>model.DenseHorizontal(model.Context,row,225,240,.25).Points.ToArray()).ToArray()));
        return result.ToArray();
    }
    internal static StandardTrackPairCli.TrackCase[] BuildCases(StandardGeometryContext context)=>BuildProbes(context).Select((probe,i)=>
    {
        var points=probe.Paths.SelectMany(x=>x).ToArray();
        // A change-containment reservation, not an inferred Solid or footprint.
        var reserve=new ScreenRect(points.Min(p=>p.X)-16,points.Min(p=>p.Y)-16,points.Max(p=>p.X)+17,points.Max(p=>p.Y)+17);
        if(reserve.Left<context.Canvas.Left||reserve.Top<context.Canvas.Top||reserve.Right>context.Canvas.Right||reserve.Bottom>context.Canvas.Bottom)
            throw new InvalidDataException("Plane reservation leaves the exact captured Canvas.");
        foreach(var path in probe.Paths)StandardBandContour.Validate(path,context.Canvas);
        var lines=probe.Paths.Select(p=>new ScreenLine(p[0].X,p[0].Y,p[^1].X,p[^1].Y)).ToArray();
        return new StandardTrackPairCli.TrackCase(i,0,Id(new{Revision,probe}),
            new(i,0,"plane-"+probe.Role,probe.Vertical,false,probe.Paths.Max(p=>p.Length)-1,reserve,[new("black-band",new(0,0,0),lines)]){Speed=StandardRepeatSeries.Speed},probe.Paths);
    }).ToArray();
    internal static void CheckAssets(string folder,Probe[] probes)
    {
        foreach(var (name,hash) in Assets)if(Hash(Path.Combine(folder,name))!=hash)throw new InvalidDataException("Frozen plane asset changed.");
        using var doc=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder,"blueprint-v2.json")));
        var cases=doc.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        if(cases.Length!=probes.Length)throw new InvalidDataException("Plane denominator changed.");
        foreach(var (c,p) in cases.Zip(probes))
        {
            var tracks=p.Role=="descriptive-only"?c.GetProperty("strokes").EnumerateArray().Select(x=>x.GetProperty("points")).ToArray():[c.GetProperty("points")];
            if(c.GetProperty("label").GetString()!=p.Label||c.GetProperty("role").GetString()!=p.Role||tracks.Length!=p.Paths.Length)
                throw new InvalidDataException("Plane role/order changed.");
            foreach(var (t,path) in tracks.Zip(p.Paths))
                if(!t.EnumerateArray().Select(v=>new ScreenPoint(v.GetProperty("x").GetInt32(),v.GetProperty("y").GetInt32())).SequenceEqual(path))
                    throw new InvalidDataException("Plane commands differ from independent frozen blueprint.");
            if(p.Role!="descriptive-only"&&(c.GetProperty("bodyScreenRange")[0].GetInt32()!=p.BodyFirst||c.GetProperty("bodyScreenRange")[1].GetInt32()!=p.BodyLast))
                throw new InvalidDataException("Plane mandatory body changed.");
        }
    }
    private static void CheckSettings(Settings settings,StandardGeometryContext context,StandardTrackPairCli.TrackCase[] cases)
    {
        settings.Validate();
        if(!ReleaseProfile.Standard(settings)||StandardLineMotion.Enabled(settings)||settings.Text("speed_profile")!="Rapid"||StrokeTiming.Frame(settings)!=.02
            ||settings.PaintCalibration().Rect("canvas")!=context.Canvas||settings.Text("color_mode")!=context.ColorMode)
            throw new InvalidDataException("Plane settings changed.");
        var guard=InputCommandGuard.From(settings,new(context.ClientOrigin.X,context.ClientOrigin.Y,context.ClientOrigin.X+context.ClientSize.Width,context.ClientOrigin.Y+context.ClientSize.Height));
        if(cases.SelectMany(c=>c.Paths).Any(p=>!guard.Allows(p)))throw new InvalidDataException("Plane path intersects captured controls.");
        foreach(var a in cases)foreach(var b in cases.Where(b=>b.Sample.Index>a.Sample.Index))
            if(a.Sample.Reservation.Left<b.Sample.Reservation.Right&&b.Sample.Reservation.Left<a.Sample.Reservation.Right
                &&a.Sample.Reservation.Top<b.Sample.Reservation.Bottom&&b.Sample.Reservation.Top<a.Sample.Reservation.Bottom)
                throw new InvalidDataException("Independent plane reservations overlap.");
    }
    internal static void Init(string sourceRow,string assets,string directory)
    {
        var m=StandardRowPilot.Read(sourceRow,true);if(m.SyntheticSource)throw new InvalidDataException("Native source required.");StandardRowPilot.Verify(sourceRow,m);
        Create(directory,Settings.Load(Path.Combine(sourceRow,"settings.json")),m.Context,Path.Combine(sourceRow,"baseline.png"),
            Path.Combine(sourceRow,FrontierSessionCapture.FrameName),assets,m.ProtocolId);
    }
    internal static Manifest Create(string directory,Settings original,StandardGeometryContext context,string baseline,string geometry,string assets,string sourceId,bool synthetic=false)
    {
        directory=Path.GetFullPath(directory);StandardUvPilot.Separate(Path.GetDirectoryName(Path.GetFullPath(baseline))!,directory);
        var probes=BuildProbes(context);var cases=BuildCases(context);CheckAssets(assets,probes);var settings=original.Clone();CheckSettings(settings,context,cases);
        if(!Digest(sourceId))throw new InvalidDataException("Missing source-row protocol identity.");
        StandardStraightPilotCli.Frame(baseline,context.Canvas,synthetic);if(!synthetic)StandardTexelView.ReadFrame(geometry);
        Directory.CreateDirectory(directory);var refs=new Dictionary<string,string>(StringComparer.Ordinal);
        void Copy(string from,string to){string path=Path.Combine(directory,to);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.Copy(from,path);refs.Add(to,Hash(path));}
        Copy(baseline,"reference/baseline.png");Copy(Path.ChangeExtension(baseline,"json"),"reference/baseline.json");
        if(!synthetic){Copy(geometry,"reference/geometry.png");Copy(Path.ChangeExtension(geometry,"json"),"reference/geometry.json");}
        foreach(string name in Assets.Keys)Copy(Path.Combine(assets,name),"fixtures/"+name);
        File.WriteAllText(Path.Combine(directory,"settings.json"),settings.Data.ToJsonString());
        var m=new Manifest(Revision,Policy,StandardTexelView.Policy,StandardRowPilot.BaselinePolicy,context,Parameters(),probes,cases,refs,Hash(Path.Combine(directory,"settings.json")),
            Hash(typeof(Painter).Assembly.Location),Hash(typeof(Settings).Assembly.Location),sourceId,Guid.NewGuid().ToString("N"),"",synthetic);
        m=m with{ProtocolId=Id(m)};StandardUvPilot.WriteNew(Path.Combine(directory,"protocol.json"),m);File.WriteAllText(Path.Combine(directory,"protocol.json.sha256"),Hash(Path.Combine(directory,"protocol.json")));
        StandardUvPilot.WriteNew(Path.Combine(directory,"series-status.json"),new{status="NOT_STARTED",requiredCases=13,requiredStrokes=28,nativeInput=false,productionEligible=false,physicalQualityCertified=false,automaticRetry=false,automaticResume=false});
        Read(directory,true);return m;
    }
    internal static Manifest Read(string directory,bool offline=false)
    {
        if(!offline&&(new[]{"attempted.json","started.json","events.jsonl","baseline.png","final.png",FrontierSessionCapture.FileName,"geometry-check.json","startup-failures.jsonl"}.Any(n=>File.Exists(Path.Combine(directory,n)))
            ||Directory.GetDirectories(directory,"sample-*").Length>0))throw new IOException("This attempt has retained data; retry/resume disabled.");
        string path=Path.Combine(directory,"protocol.json");if(Hash(path)!=File.ReadAllText(path+".sha256").Trim())throw new InvalidDataException("Plane manifest changed.");
        var m=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path))??throw new InvalidDataException("Missing plane manifest.");
        var expected=new[]{"reference/baseline.png","reference/baseline.json"}.Concat(m.SyntheticSource?[]:new[]{"reference/geometry.png","reference/geometry.json"}).Concat(Assets.Keys.Select(n=>"fixtures/"+n)).ToArray();
        if(m.Revision!=Revision||m.Policy!=Policy||m.ViewPolicy!=StandardTexelView.Policy||m.BaselinePolicy!=StandardRowPilot.BaselinePolicy
            ||m.ProtocolId!=Id(m with{ProtocolId=""})||!Guid.TryParseExact(m.CollectionId,"N",out _)||!Digest(m.SourceRowProtocolId)||!Digest(m.AppSha256)||!Digest(m.CoreSha256)
            ||JsonSerializer.Serialize(m.Model)!=JsonSerializer.Serialize(Parameters())||JsonSerializer.Serialize(m.Probes)!=JsonSerializer.Serialize(BuildProbes(m.Context))
            ||JsonSerializer.Serialize(m.Cases)!=JsonSerializer.Serialize(BuildCases(m.Context))||m.SettingsSha256!=Hash(Path.Combine(directory,"settings.json"))
            ||m.ReferenceFiles.Count!=expected.Length||m.ReferenceFiles.Keys.Any(k=>!expected.Contains(k,StringComparer.Ordinal))
            ||!offline&&(m.SyntheticSource||m.AppSha256!=Hash(typeof(Painter).Assembly.Location)||m.CoreSha256!=Hash(typeof(Settings).Assembly.Location)))
            throw new InvalidDataException("Plane policy, paths, model, references, settings or binaries changed.");
        foreach(var (name,h) in m.ReferenceFiles)if(Hash(Path.Combine(directory,name))!=h)throw new InvalidDataException("Plane reference changed.");
        CheckAssets(Path.Combine(directory,"fixtures"),m.Probes);CheckSettings(Settings.Load(Path.Combine(directory,"settings.json")),m.Context,m.Cases);
        StandardStraightPilotCli.Frame(Path.Combine(directory,"reference/baseline.png"),m.Context.Canvas,m.SyntheticSource);return m;
    }
    internal static StandardTrackPairCli.Manifest View(Manifest m)=>new(StandardTrackPairCli.Revision,"plane-research",m.ProtocolId,m.Context,m.Cases,m.ProtocolId,"",m.ReferenceFiles,m.SettingsSha256,m.AppSha256,m.CoreSha256,m.SyntheticSource,StandardTrackPairCli.CapturePolicy);
    internal static FrontierSessionCapture.Binding Binding(Manifest m)=>new(m.ProtocolId,m.SettingsSha256,m.AppSha256,m.CoreSha256,m.Context,m.SyntheticSource);
    internal static void Verify(string directory,Manifest? manifest=null,bool requireCompleteStatus=true)
    {
        var m=manifest??Read(directory,true);StandardTrackPairCli.ValidateCompleted(directory,View(m),requireCompleteStatus);
        if(!m.SyntheticSource){FrontierSessionCapture.VerifyUse(directory,directory,Binding(m));StandardTexelView.Check(directory,false);
            StandardRowPilot.RequireFreshBaseline(FrontierSessionCapture.Baseline(directory,Binding(m)),StandardStraightPilotCli.Frame(Path.Combine(directory,"baseline.png"),m.Context.Canvas,false),m.Context.Canvas);}
    }
    internal static void Run(string directory)
    {
        var m=Read(directory);StandardEvidenceSeriesRunner.Result? result=null;
        StandardEvidenceSeriesCli.RunCollection(directory,m.Context.Canvas,13,20,p=>result=p.CollectPlanePilot(m,directory),
            (settings,window)=>{var prepared=FrontierSessionCapture.CaptureFresh(settings,window,Binding(m),directory);StandardTexelView.Check(directory,true);return prepared;},showDialog:false);
        if(result?.Status!="DATA_COLLECTED")Environment.ExitCode=1;else Verify(directory,m);
    }
}
