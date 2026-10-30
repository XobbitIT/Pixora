using System.Security.Cryptography;
using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

// Explicit research protocol through the existing contour entry. Not START,
// a qualifying profile, a new transport or permission inferred from a model.
internal static class StandardRowOffgridPilot
{
    internal const string Revision="standard-row-offgrid-pilot-v1b";
    internal const string Policy="offgrid-joint6-centered15-control16-median015-v1b";
    internal sealed record Probe(string Label,string Role,bool Vertical,int? ConstantIndex,int BodyFirst,int BodyLast,ScreenPoint[][] Paths);
    internal sealed record FrozenModel(StandardTexelColumnParameters Columns,TexelRowObservation Rows,TexelRowInputMapping[] Family,string ObservationSha256,string FamilySha256);
    internal sealed record Manifest(string Revision,string Policy,string ViewPolicy,string BaselinePolicy,StandardGeometryContext Context,
        FrozenModel Model,Probe[] Probes,StandardTrackPairCli.TrackCase[] Cases,Dictionary<string,string> ReferenceFiles,string SettingsSha256,
        string AppSha256,string CoreSha256,string SourceRowProtocolId,string CollectionId,string ProtocolId,bool SyntheticSource);
    internal static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    internal static readonly Dictionary<string,string> Assets=new(StringComparer.Ordinal)
    {
        ["protocol-v1b.json"]="78d88a380d124686a7c1b03c91b30a162394791ee9f30f4f665702377ad8ea9c",
        ["row-family-v1.json"]="9e341f94fbce597e2a9471fea26159b3c5144f6d522ac19fa79e3839387190cd"
    };
    internal static FrozenModel Parameters(string folder)
    {
        foreach(var (name,hash) in Assets)if(Hash(Path.Combine(folder,name))!=hash)throw new InvalidDataException("Frozen offgrid asset changed.");
        using var f=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder,"row-family-v1.json")));
        var family=f.RootElement.GetProperty("admissible").EnumerateArray().Select(v=>new TexelRowInputMapping(v.GetProperty("beta").GetDouble(),670,v.GetProperty("c").GetDouble(),v.GetProperty("offset").GetInt32())).ToArray();
        if(family.Length!=1148)throw new InvalidDataException("Offgrid input-family denominator changed.");
        return new(StandardPlanePilot.Parameters().Columns,new(.47930899999997834,57612.96453407507,-.42756781793926024,.15037548668027606,2),family,
            "dc3c56810e829ad8409a1075571f51bd99b7c0bbe5ed48a1acd1300786202e77",Assets["row-family-v1.json"]);
    }
    internal static string Hash(string path)=>StandardBoundaryReview.Hash(path);
    internal static string Id(object value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value))).ToLowerInvariant();
    private static bool Digest(string? s)=>s is {Length:64}&&s.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
    internal static TexelGrid Grid(StandardGeometryContext context,string folder)
    {
        _=StandardRowPilot.BuildLines(context); var columns=StandardTexelPilot.Model(context); var p=Parameters(folder);
        using var doc=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder,"protocol-v1b.json")));
        var expected=doc.RootElement.GetProperty("context").Deserialize<StandardGeometryContext>()!;
        if(context.Id!=expected.Id)throw new InvalidDataException("Offgrid fixture requires its exact context.");
        return new(true,columns,p.Rows,new(520,220,1431,1101),p.Family,p.ObservationSha256,p.FamilySha256);
    }
    internal static Probe[] BuildProbes(StandardGeometryContext context,string folder)
    {
        var grid=Grid(context,folder); using var doc=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder,"protocol-v1b.json")));
        var result=new List<Probe>();
        ScreenPoint[] DecodePath(JsonElement values)=>values.EnumerateArray().Select(v=>new ScreenPoint(v.GetProperty("x").GetInt32(),v.GetProperty("y").GetInt32())).ToArray();
        foreach(var v in doc.RootElement.GetProperty("probes").EnumerateArray())
        {
            int y=v.GetProperty("commandY").GetInt32(),index=v.GetProperty("aliasTopIndex").GetInt32();var wanted=DecodePath(v.GetProperty("path"));
            var path=grid.CenteredRowScreenSpan(grid.Context,index,wanted[0].X,wanted[^1].X,.25).Points.ToArray();
            if(!path.SequenceEqual(wanted))throw new InvalidDataException("Core offgrid path differs from frozen oracle.");
            var body=v.GetProperty("bodyX");result.Add(new(v.GetProperty("label").GetString()!,"grid-probe",false,index,body[0].GetInt32(),body[1].GetInt32(),[path]));
        }
        foreach(var v in doc.RootElement.GetProperty("blocks").EnumerateArray())
        {
            var all=v.GetProperty("paths").EnumerateArray().Select(DecodePath).ToArray();bool centered=v.GetProperty("aliasTopIndices").ValueKind!=JsonValueKind.Null;
            var indices=centered?v.GetProperty("aliasTopIndices").EnumerateArray().Select(x=>x.GetInt32()).ToArray():[];
            var paths=all.Select((wanted,i)=>(centered?grid.CenteredRowScreenSpan(grid.Context,indices[i],wanted[0].X,wanted[^1].X,.25):grid.HorizontalAtScreenY(grid.Context,wanted[0].Y,wanted[0].X,wanted[^1].X,null,0)).Points.ToArray()).ToArray();
            if(paths.Where((p,i)=>!p.SequenceEqual(all[i])).Any())throw new InvalidDataException("Core block path differs from frozen oracle.");
            var body=v.GetProperty("bodyX");result.Add(new(v.GetProperty("label").GetString()!,centered?"centered-prefix":"boundary-control",false,centered?indices[0]:null,body[0].GetInt32(),body[1].GetInt32(),paths));
        }
        if(result.Count!=8||result.Sum(p=>p.Paths.Length)!=37||result.Sum(p=>p.Paths.Sum(a=>a.Length))!=1687)
            throw new InvalidDataException("Offgrid path denominator changed.");
        return result.ToArray();
    }
    internal static StandardTrackPairCli.TrackCase[] BuildCases(StandardGeometryContext context,string folder)=>BuildProbes(context,folder).Select((probe,i)=>
    {
        var points=probe.Paths.SelectMany(x=>x).ToArray();
        var reserve=new ScreenRect(points.Min(p=>p.X)-16,points.Min(p=>p.Y)-16,points.Max(p=>p.X)+17,points.Max(p=>p.Y)+17);
        if(reserve.Left<context.Canvas.Left||reserve.Top<context.Canvas.Top||reserve.Right>context.Canvas.Right||reserve.Bottom>context.Canvas.Bottom)
            throw new InvalidDataException("Offgrid reservation leaves the selected Canvas.");
        foreach(var path in probe.Paths)StandardBandContour.Validate(path,context.Canvas);
        var lines=probe.Paths.Select(p=>new ScreenLine(p[0].X,p[0].Y,p[^1].X,p[^1].Y)).ToArray();
        return new StandardTrackPairCli.TrackCase(i,0,Id(new{Revision,probe}),new(i,0,"offgrid-"+probe.Role,false,false,probe.Paths.Max(p=>p.Length)-1,reserve,[new("black-band",new(0,0,0),lines)]){Speed=StandardRepeatSeries.Speed},probe.Paths);
    }).ToArray();
    internal static void CheckAssets(string folder,Probe[] probes)
    {
        _=Parameters(folder);
        if(probes.Length!=8||probes.Take(6).Any(p=>p.Role!="grid-probe"||p.Paths.Length!=1||p.BodyLast-p.BodyFirst+1!=89)
            ||probes[6].Paths.Length!=15||probes[7].Paths.Length!=16||probes.Skip(6).Any(p=>p.BodyFirst!=936||p.BodyLast!=954))
            throw new InvalidDataException("Offgrid completeness denominator changed.");
    }
    private static void CheckSettings(Settings settings,StandardGeometryContext context,StandardTrackPairCli.TrackCase[] cases)
    {
        settings.Validate();
        if(!ReleaseProfile.Standard(settings)||StandardLineMotion.Enabled(settings)||settings.Text("speed_profile")!="Rapid"||StrokeTiming.Frame(settings)!=.02
            ||settings.PaintCalibration().Rect("canvas")!=context.Canvas||settings.Text("color_mode")!=context.ColorMode)
            throw new InvalidDataException("Offgrid settings changed.");
        var guard=InputCommandGuard.From(settings,new(context.ClientOrigin.X,context.ClientOrigin.Y,context.ClientOrigin.X+context.ClientSize.Width,context.ClientOrigin.Y+context.ClientSize.Height));
        if(cases.SelectMany(c=>c.Paths).Any(p=>!guard.Allows(p)))throw new InvalidDataException("Offgrid path intersects captured controls.");
        foreach(var a in cases)foreach(var b in cases.Where(b=>b.Sample.Index>a.Sample.Index))
            if(a.Sample.Reservation.Left<b.Sample.Reservation.Right&&b.Sample.Reservation.Left<a.Sample.Reservation.Right
                &&a.Sample.Reservation.Top<b.Sample.Reservation.Bottom&&b.Sample.Reservation.Top<a.Sample.Reservation.Bottom)
                throw new InvalidDataException("Independent offgrid reservations overlap.");
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
        var probes=BuildProbes(context,assets);var cases=BuildCases(context,assets);CheckAssets(assets,probes);var settings=original.Clone();CheckSettings(settings,context,cases);
        if(!Digest(sourceId))throw new InvalidDataException("Missing source-row protocol identity.");
        StandardStraightPilotCli.Frame(baseline,context.Canvas,synthetic);if(!synthetic)StandardTexelView.ReadFrame(geometry);
        Directory.CreateDirectory(directory);var refs=new Dictionary<string,string>(StringComparer.Ordinal);
        void Copy(string from,string to){string path=Path.Combine(directory,to);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.Copy(from,path);refs.Add(to,Hash(path));}
        Copy(baseline,"reference/baseline.png");Copy(Path.ChangeExtension(baseline,"json"),"reference/baseline.json");
        if(!synthetic){Copy(geometry,"reference/geometry.png");Copy(Path.ChangeExtension(geometry,"json"),"reference/geometry.json");}
        foreach(string name in Assets.Keys)Copy(Path.Combine(assets,name),"fixtures/"+name);
        File.WriteAllText(Path.Combine(directory,"settings.json"),settings.Data.ToJsonString());
        var m=new Manifest(Revision,Policy,StandardTexelView.Policy,StandardRowPilot.BaselinePolicy,context,Parameters(assets),probes,cases,refs,Hash(Path.Combine(directory,"settings.json")),
            Hash(typeof(Painter).Assembly.Location),Hash(typeof(Settings).Assembly.Location),sourceId,Guid.NewGuid().ToString("N"),"",synthetic);
        m=m with{ProtocolId=Id(m)};StandardUvPilot.WriteNew(Path.Combine(directory,"protocol.json"),m);File.WriteAllText(Path.Combine(directory,"protocol.json.sha256"),Hash(Path.Combine(directory,"protocol.json")));
        StandardUvPilot.WriteNew(Path.Combine(directory,"series-status.json"),new{status="NOT_STARTED",requiredCases=8,requiredStrokes=37,nativeInput=false,productionEligible=false,physicalQualityCertified=false,automaticRetry=false,automaticResume=false});
        Read(directory,true);return m;
    }
    internal static Manifest Read(string directory,bool offline=false)
    {
        if(!offline&&(new[]{"attempted.json","started.json","events.jsonl","baseline.png","final.png",FrontierSessionCapture.FileName,"geometry-check.json","startup-failures.jsonl"}.Any(n=>File.Exists(Path.Combine(directory,n)))
            ||Directory.GetDirectories(directory,"sample-*").Length>0))throw new IOException("This attempt has retained data; retry/resume disabled.");
        string path=Path.Combine(directory,"protocol.json");if(Hash(path)!=File.ReadAllText(path+".sha256").Trim())throw new InvalidDataException("Offgrid manifest changed.");
        var m=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path))??throw new InvalidDataException("Missing offgrid manifest.");
        var expected=new[]{"reference/baseline.png","reference/baseline.json"}.Concat(m.SyntheticSource?[]:new[]{"reference/geometry.png","reference/geometry.json"}).Concat(Assets.Keys.Select(n=>"fixtures/"+n)).ToArray();
        if(m.Revision!=Revision||m.Policy!=Policy||m.ViewPolicy!=StandardTexelView.Policy||m.BaselinePolicy!=StandardRowPilot.BaselinePolicy
            ||m.ProtocolId!=Id(m with{ProtocolId=""})||!Guid.TryParseExact(m.CollectionId,"N",out _)||!Digest(m.SourceRowProtocolId)||!Digest(m.AppSha256)||!Digest(m.CoreSha256)
            ||JsonSerializer.Serialize(m.Model)!=JsonSerializer.Serialize(Parameters(Path.Combine(directory,"fixtures")))||JsonSerializer.Serialize(m.Probes)!=JsonSerializer.Serialize(BuildProbes(m.Context,Path.Combine(directory,"fixtures")))
            ||JsonSerializer.Serialize(m.Cases)!=JsonSerializer.Serialize(BuildCases(m.Context,Path.Combine(directory,"fixtures")))||m.SettingsSha256!=Hash(Path.Combine(directory,"settings.json"))
            ||m.ReferenceFiles.Count!=expected.Length||m.ReferenceFiles.Keys.Any(k=>!expected.Contains(k,StringComparer.Ordinal))
            ||!offline&&(m.SyntheticSource||m.AppSha256!=Hash(typeof(Painter).Assembly.Location)||m.CoreSha256!=Hash(typeof(Settings).Assembly.Location)))
            throw new InvalidDataException("Offgrid policy, paths, model, references, settings or binaries changed.");
        foreach(var (name,h) in m.ReferenceFiles)if(Hash(Path.Combine(directory,name))!=h)throw new InvalidDataException("Offgrid reference changed.");
        CheckAssets(Path.Combine(directory,"fixtures"),m.Probes);CheckSettings(Settings.Load(Path.Combine(directory,"settings.json")),m.Context,m.Cases);
        StandardStraightPilotCli.Frame(Path.Combine(directory,"reference/baseline.png"),m.Context.Canvas,m.SyntheticSource);return m;
    }
    internal static StandardTrackPairCli.Manifest View(Manifest m)=>new(StandardTrackPairCli.Revision,"offgrid-research",m.ProtocolId,m.Context,m.Cases,m.ProtocolId,"",m.ReferenceFiles,m.SettingsSha256,m.AppSha256,m.CoreSha256,m.SyntheticSource,StandardTrackPairCli.CapturePolicy);
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
        StandardEvidenceSeriesCli.RunCollection(directory,m.Context.Canvas,8,20,p=>result=p.CollectOffgridPilot(m,directory),
            (settings,window)=>{var prepared=FrontierSessionCapture.CaptureFresh(settings,window,Binding(m),directory);StandardTexelView.Check(directory,true);return prepared;},showDialog:false);
        if(result?.Status!="DATA_COLLECTED")Environment.ExitCode=1;else Verify(directory,m);
    }
}
