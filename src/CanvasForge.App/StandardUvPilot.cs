using System.Security.Cryptography;
using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

// An explicit diagnostic under the existing contour entry. Never a START
// route, profile import, automatic phase search or production qualification.
internal static class StandardUvPilot
{
    internal const string Revision="standard-uv-columns-c3-prime-v1";
    internal const string Policy="c3-prime-0028-0030-v1";
    internal sealed record Line(string Label,double AnchorX,bool Tilted,double U,ScreenPoint[] Path);
    internal sealed record Manifest(string Revision,string Policy,StandardGeometryContext Context,Line[] Lines,
        StandardTrackPairCli.TrackCase[] Cases,Dictionary<string,string> ReferenceFiles,string SettingsSha256,
        string AppSha256,string CoreSha256,string SourcePairId,string CollectionId,bool SyntheticSource,string ProtocolId);
    internal static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    internal static readonly Dictionary<string,string> Fixtures=new(StringComparer.Ordinal)
    {
        ["Paths.json"]="60d6aa69626a8cd0234d06d313757fc061081509facd3769fba105d838980216",
        ["PLAN.json"]="c508a2c8fca38482915b944a423fa787ddfcf0b9277a5e65b0870199e90de20a",
        ["Analysis/analytic-results.json"]="affaa39cd15e61729fe16f91fbf4f91956b1d3979c6f857a76e30fbf2d75877a",
        ["Analysis/extractor-results.json"]="11316a98a588d30ad26973bc281b4a0d4e683bc997423b690d8b63d7abf47b3b",
        ["Analysis/extractor-csharp.json"]="a43bbe8541c902261c560e046d4053cdaf484bd0c2c1390919244350ad3f1cef",
        ["Analysis/synthetic-inputs.csv"]="32b85e7f0e4923b483216f914df7f3963f35431166af56267c2067245f72cc2e",
        ["Analysis/extractor-rows.csv"]="765da3a7b26dc9fef444b1536cdd2434ceff19a5a8b2f76c23fc606792fa0779",
        ["Receipt.json"]="7547c70a882a12dd61cb958fbf0c3a7c99c831fe632c443485e9f84f9793a038"
    };
    internal static string Hash(string p)=>StandardBoundaryReview.Hash(p);
    private static string Id(object value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value))).ToLowerInvariant();
    internal static double Left(int y)=>467.5127+.0091983*(y-180);
    internal static double Width(int y)=>1516.8167-.0090718*(y-180)-Left(y);
    internal static double Drift(Line line)=>900*(.0091983+line.U*(-.0090718-.0091983));
    internal static Line[] BuildLines(StandardGeometryContext context)
    {
        context.Validate();
        if(context.Canvas!=new ScreenRect(465,148,1510,1192)||context.ClientOrigin!=default||context.ClientSize!=new ScreenSize(2560,1440)
            ||context.Dpi!=96||context.ShapeSlot!=3||context.Size!=3||context.Speed!=StandardRepeatSeries.Speed
            ||context.FrameSeconds!=.02||context.EndHoldSeconds!=.02||context.ReleaseSeconds!=.02||context.RequestedHex!="000000")
            throw new InvalidDataException("UV diagnostic requires the exact preregistered physical geometry and emitter timing; no recrop or rescale.");
        (string Label,double X,bool Tilted)[] design=[("600-C",600,false),("600-T1",620,true),("600-T2",640.5,true),("600-T3",661.25,true),
            ("995-C",995,false),("995-T1",1015,true),("995-T2",1035.5,true),("995-T3",1056.25,true),
            ("1400-C",1400,false),("1400-T1",1420,true),("1400-T2",1440.5,true),("1400-T3",1461.25,true)];
        return design.Select(d=>
        {
            double u=(d.X-Left(650))/Width(650);
            var path=Enumerable.Range(200,901).Select(y=>new ScreenPoint((int)Math.Round(d.Tilted?Left(y)+u*Width(y):d.X,MidpointRounding.ToEven),y)).ToArray();
            StandardBandContour.Validate(path,context.Canvas);return new Line(d.Label,d.X,d.Tilted,u,path);
        }).ToArray();
    }
    internal static StandardTrackPairCli.TrackCase[] BuildCases(StandardGeometryContext context)
    {
        var lines=BuildLines(context);var result=new List<StandardTrackPairCli.TrackCase>();
        for(int group=0;group<3;group++)
        {
            var paths=lines.Skip(4*group).Take(4).Select(l=>l.Path).ToArray();var points=paths.SelectMany(p=>p).ToArray();
            var r=new ScreenRect(points.Min(p=>p.X)-16,184,points.Max(p=>p.X)+17,1117);
            if(r.Left<context.Canvas.Left||r.Right>context.Canvas.Right||r.Top<context.Canvas.Top||r.Bottom>context.Canvas.Bottom
                ||result.Any(c=>c.Sample.Reservation.Left<r.Right&&c.Sample.Reservation.Right>r.Left))
                throw new InvalidDataException("UV groups overlap or exceed the exact Canvas.");
            var sample=new EvidenceSeriesCase(group,0,"uv-columns-diagnostic",true,false,900,r,
                [new("black-band",new(0,0,0),paths.Select(p=>new ScreenLine(p[0].X,p[0].Y,p[^1].X,p[^1].Y)).ToArray())]){Speed=StandardRepeatSeries.Speed};
            result.Add(new(group,0,Id(new{Revision,group,paths}),sample,paths));
        }
        return result.ToArray();
    }
    internal static void Separate(string source,string output)
    {
        source=Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);output=Path.GetFullPath(output).TrimEnd(Path.DirectorySeparatorChar);
        static bool Within(string a,string b)=>a.Equals(b,StringComparison.OrdinalIgnoreCase)||a.StartsWith(b+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        if(Within(source,output)||Within(output,source)||Directory.Exists(output)||File.Exists(output))throw new IOException("Потрібна нова окрема папка. Попередні дані не перезаписуються.");
    }
    internal static void RequireFixtures(string root)
    {
        foreach(var (name,expected) in Fixtures)
            if(Hash(Path.Combine(root,name))!=expected)throw new InvalidDataException("Frozen C3-prime reference fixture changed: "+name);
    }
    internal static void Init(string sourcePair,string directory,string? fixtureRoot=null)
    {
        sourcePair=Path.GetFullPath(sourcePair);directory=Path.GetFullPath(directory);Separate(sourcePair,directory);
        var pair=StandardDiscreteBandCli.ReadPair(sourcePair);
        if(pair.SyntheticSource)throw new InvalidDataException("Native source context required.");
        foreach(string direction in new[]{"ascending","descending"})StandardDiscreteBandCli.Verify(Path.Combine(sourcePair,direction));
        Create(directory,Settings.Load(Path.Combine(sourcePair,"settings.json")),pair.Context,
            Path.Combine(sourcePair,"ascending/baseline.png"),pair.PairId,fixtureRoot??Path.Combine(AppContext.BaseDirectory,"Uv_C3_Fixtures"));
    }
    internal static Manifest Create(string directory,Settings source,StandardGeometryContext context,string baseline,string sourcePairId,string fixtures,bool synthetic=false)
    {
        directory=Path.GetFullPath(directory);Separate(fixtures,directory);
        if(Directory.Exists(directory)||File.Exists(directory))throw new IOException("UV experiment directory already exists.");
        RequireFixtures(fixtures);var lines=BuildLines(context);var cases=BuildCases(context);var settings=source.Clone();
        StandardUvAnalysis.CheckFixtures(fixtures,lines);
        if(!ReleaseProfile.Standard(settings)||StandardLineMotion.Enabled(settings)||settings.Text("speed_profile")!="Rapid"
            ||StrokeTiming.Frame(settings)!=.02||settings.Calibration.Rect("canvas")!=context.Canvas||settings.Text("color_mode")!=context.ColorMode)
            throw new InvalidDataException("Frozen source settings differ from the UV protocol.");
        settings.Validate();
        var guard=InputCommandGuard.From(settings,new(context.ClientOrigin.X,context.ClientOrigin.Y,context.ClientOrigin.X+context.ClientSize.Width,context.ClientOrigin.Y+context.ClientSize.Height));
        if(lines.Any(l=>!guard.Allows(l.Path)))throw new InvalidDataException("UV path intersects a captured control or leaves the permitted region.");
        StandardStraightPilotCli.Frame(baseline,context.Canvas,synthetic);
        Directory.CreateDirectory(directory);var refs=new Dictionary<string,string>(StringComparer.Ordinal);
        void Copy(string from,string relative)
        {string to=Path.Combine(directory,relative);Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Copy(from,to);refs.Add(relative,Hash(to));}
        foreach(string name in Fixtures.Keys)Copy(Path.Combine(fixtures,name),"fixtures/"+name);
        Copy(baseline,"reference/baseline.png");Copy(Path.ChangeExtension(baseline,"json"),"reference/baseline.json");
        File.WriteAllText(Path.Combine(directory,"settings.json"),settings.Data.ToJsonString());
        var m=new Manifest(Revision,Policy,context,lines,cases,refs,Hash(Path.Combine(directory,"settings.json")),
            Hash(typeof(Painter).Assembly.Location),Hash(typeof(Settings).Assembly.Location),sourcePairId,Guid.NewGuid().ToString("N"),synthetic,"");
        m=m with{ProtocolId=Id(m)};WriteNew(Path.Combine(directory,"protocol.json"),m);
        File.WriteAllText(Path.Combine(directory,"protocol.json.sha256"),Hash(Path.Combine(directory,"protocol.json")));
        WriteNew(Path.Combine(directory,"series-status.json"),new{status="NOT_STARTED",requiredCases=3,requiredStrokes=12,mandatorySections=10812,
            nativeInput=false,physicalQualityCertified=false,automaticRetry=false,automaticResume=false});
        Read(directory,offline:true);return m;
    }
    internal static void WriteNew(string path,object value)
    {using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write);JsonSerializer.Serialize(file,value,Json);}
    internal static Manifest Read(string directory,bool offline=false)
    {
        if(!offline&&(new[]{"attempted.json","started.json","events.jsonl","baseline.png","final.png",FrontierSessionCapture.FileName,"startup-failures.jsonl"}.Any(n=>File.Exists(Path.Combine(directory,n)))
            ||Directory.Exists(directory)&&Directory.GetDirectories(directory,"sample-*").Length>0))
            throw new IOException("Цей дослід вже запускали або містить залишки збору; автоматичне повторення вимкнене.");
        string path=Path.Combine(directory,"protocol.json");if(Hash(path)!=File.ReadAllText(path+".sha256").Trim())throw new InvalidDataException("Frozen UV manifest changed.");
        var m=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path))??throw new InvalidDataException("Missing UV manifest.");
        if(m.Revision!=Revision||m.Policy!=Policy||m.ProtocolId!=Id(m with{ProtocolId=""})||!Guid.TryParseExact(m.CollectionId,"N",out _)
            ||m.SettingsSha256!=Hash(Path.Combine(directory,"settings.json"))
            ||JsonSerializer.Serialize(m.Lines)!=JsonSerializer.Serialize(BuildLines(m.Context))||JsonSerializer.Serialize(m.Cases)!=JsonSerializer.Serialize(BuildCases(m.Context))
            ||m.ReferenceFiles.Count!=Fixtures.Count+2||m.ReferenceFiles.Keys.Any(k=>!Fixtures.Keys.Select(n=>"fixtures/"+n).Concat(["reference/baseline.png","reference/baseline.json"]).Contains(k,StringComparer.Ordinal))
            ||!offline&&(m.SyntheticSource||m.AppSha256!=Hash(typeof(Painter).Assembly.Location)||m.CoreSha256!=Hash(typeof(Settings).Assembly.Location)))
            throw new InvalidDataException("UV protocol, paths, settings or binaries changed.");
        RequireFixtures(Path.Combine(directory,"fixtures"));
        StandardUvAnalysis.CheckPaths(Path.Combine(directory,"fixtures"),m.Lines);
        foreach(var (name,expected) in m.ReferenceFiles)if(Hash(Path.Combine(directory,name))!=expected)throw new InvalidDataException("UV portable reference changed.");
        var settings=Settings.Load(Path.Combine(directory,"settings.json"));
        if(settings.Calibration.Rect("canvas")!=m.Context.Canvas||settings.Text("color_mode")!=m.Context.ColorMode||!ReleaseProfile.Standard(settings)
            ||StandardLineMotion.Enabled(settings)||settings.Text("speed_profile")!="Rapid"||StrokeTiming.Frame(settings)!=.02)
            throw new InvalidDataException("UV settings context changed.");
        StandardStraightPilotCli.Frame(Path.Combine(directory,"reference/baseline.png"),m.Context.Canvas,m.SyntheticSource);return m;
    }
    internal static StandardTrackPairCli.Manifest View(Manifest m)=>new(StandardTrackPairCli.Revision,"uv-columns",m.ProtocolId,m.Context,m.Cases,m.ProtocolId,"",
        m.ReferenceFiles,m.SettingsSha256,m.AppSha256,m.CoreSha256,m.SyntheticSource,StandardTrackPairCli.CapturePolicy);
    internal static FrontierSessionCapture.Binding Binding(Manifest m)=>new(m.ProtocolId,m.SettingsSha256,m.AppSha256,m.CoreSha256,m.Context,m.SyntheticSource);
    internal static void Verify(string directory,Manifest? manifest=null,bool requireCompleteStatus=true)
    {
        var m=manifest??Read(directory,offline:true);StandardTrackPairCli.ValidateCompleted(directory,View(m),requireCompleteStatus);
        // A live UV run must have its own fresh proof, never an optional
        // historical reference accepted merely because the file is absent.
        if(!m.SyntheticSource&&!File.Exists(Path.Combine(directory,FrontierSessionCapture.FileName)))throw new InvalidDataException("UV session reference is missing.");
        if(!m.SyntheticSource)FrontierSessionCapture.VerifyUse(directory,directory,Binding(m));
        var initial=StandardStraightPilotCli.Frame(Path.Combine(directory,"baseline.png"),m.Context.Canvas,m.SyntheticSource);
        var historical=StandardStraightPilotCli.Frame(Path.Combine(directory,"reference/baseline.png"),m.Context.Canvas,m.SyntheticSource);
        FrontierSessionCapture.RequireBaseline(historical,initial,m.Context.Canvas);
    }
    internal static void Run(string directory)
    {
        var m=Read(directory);StandardEvidenceSeriesRunner.Result? result=null;
        StandardEvidenceSeriesCli.RunCollection(directory,m.Context.Canvas,3,10,p=>result=p.CollectUvPilot(m,directory),
            (settings,window)=>FrontierSessionCapture.CaptureFresh(settings,window,Binding(m),directory));
        if(result?.Status!="DATA_COLLECTED")Environment.ExitCode=1;else Verify(directory,m);
    }
}
