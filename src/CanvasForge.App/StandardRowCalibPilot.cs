using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

// Row-mapping calibration: isolated discriminating H plus one small black picture
// on the unchanged 1148 family. Research protocol only; not START or a profile.
internal static class StandardRowCalibPilot
{
    internal const string Revision="standard-row-calib-pilot-v1";
    internal const string Policy="calib16-label-median015-j2-fullfamily-picture40x15-v1";
    internal const int ProbeCount=16,RunCount=24,BodySections=19;
    internal sealed record Probe(string Label,int CommandY,int Lane,int BodyFirst,int BodyLast,ScreenRect Reservation,ScreenPoint[] Path);
    internal sealed record PictureRun(int Row,int FirstColumn,int LastColumn,int CommandY,double RowMargin,ScreenPoint[] Path);
    // Each sample starts inside paint and walks (StepX,StepY) towards the outside.
    internal sealed record Edge(string Name,bool Vertical,int StepX,int StepY,ScreenPoint[] Starts);
    internal sealed record Picture(ScreenRect IndexRect,string[] Mask,PictureRun[] Runs,ScreenRect Reservation,Edge[] Edges);
    internal sealed record Manifest(string Revision,string Policy,string ViewPolicy,string BaselinePolicy,StandardGeometryContext Context,
        StandardRowOffgridPilot.FrozenModel Model,Probe[] Probes,Picture Picture,StandardTrackPairCli.TrackCase[] Cases,Dictionary<string,string> ReferenceFiles,
        string SettingsSha256,string AppSha256,string CoreSha256,string SourceRowProtocolId,string CollectionId,string ProtocolId,bool SyntheticSource);
    internal static readonly Dictionary<string,string> Assets=new(StringComparer.Ordinal)
    {
        ["protocol-calib1.json"]="cf5bb2a11443398d7be75dfaa7a7cfff5d9d3b94ce4da56075d3a74ae94c5a33",
        ["row-family-v1.json"]="9e341f94fbce597e2a9471fea26159b3c5144f6d522ac19fa79e3839387190cd",
        ["row-family-refined-v1.json"]="44ccb078888daf42995ca01bc1c808504bcadcbf04102d9496c5b79dd107b7be"
    };
    internal static string Hash(string path)=>StandardBoundaryReview.Hash(path);
    private static void CheckHashes(string folder)
    {foreach(var (name,hash) in Assets)if(Hash(Path.Combine(folder,name))!=hash)throw new InvalidDataException("Frozen calibration asset changed.");}
    private static TexelRowInputMapping[] ReadFamily(string file,int count)
    {
        using var f=JsonDocument.Parse(File.ReadAllBytes(file));
        var family=f.RootElement.GetProperty("admissible").EnumerateArray().Select(v=>new TexelRowInputMapping(v.GetProperty("beta").GetDouble(),670,v.GetProperty("c").GetDouble(),v.GetProperty("offset").GetInt32())).ToArray();
        if(family.Length!=count||family.Distinct().Count()!=count)throw new InvalidDataException("Calibration family denominator changed.");
        return family;
    }
    // The same frozen observation candidate dc3c5681 and coarse family file as v1b.
    internal static readonly TexelRowObservation Rows=new(.47930899999997834,57612.96453407507,-.42756781793926024,.15037548668027606,2);
    internal const string ObservationSha256="dc3c56810e829ad8409a1075571f51bd99b7c0bbe5ed48a1acd1300786202e77";
    internal static StandardRowOffgridPilot.FrozenModel Parameters(string folder)
    {
        CheckHashes(folder);using var doc=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder,"protocol-calib1.json")));var c=doc.RootElement.GetProperty("candidate");
        if(c.GetProperty("observationSha256").GetString()!=ObservationSha256||c.GetProperty("scale").GetDouble()!=Rows.Scale||c.GetProperty("poleY").GetDouble()!=Rows.PoleY
            ||c.GetProperty("topPhase").GetDouble()!=Rows.TopPhase||c.GetProperty("bottomPhase").GetDouble()!=Rows.BottomPhase||c.GetProperty("edgeLabelSpan").GetInt32()!=Rows.EdgeLabelSpan)
            throw new InvalidDataException("Calibration candidate differs from the frozen observation model.");
        return new(StandardPlanePilot.Parameters().Columns,Rows,ReadFamily(Path.Combine(folder,"row-family-v1.json"),1148),ObservationSha256,Assets["row-family-v1.json"]);
    }
    internal static TexelRowInputMapping[] Refined(string folder){CheckHashes(folder);return ReadFamily(Path.Combine(folder,"row-family-refined-v1.json"),28663);}
    private static JsonDocument Protocol(string folder){CheckHashes(folder);return JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder,"protocol-calib1.json")));}
    internal static TexelGrid Grid(StandardGeometryContext context,string folder,TexelRowInputMapping[]? family=null,string? familySha=null)
    {
        _=StandardRowPilot.BuildLines(context);var columns=StandardTexelPilot.Model(context);var p=Parameters(folder);
        using var doc=Protocol(folder);
        if(context.Id!=doc.RootElement.GetProperty("context").Deserialize<StandardGeometryContext>()!.Id)throw new InvalidDataException("Calibration fixture requires its exact context.");
        return new(true,columns,p.Rows,new(520,220,1431,1101),family??p.Family,p.ObservationSha256,familySha??p.FamilySha256);
    }
    private static ScreenPoint[] Points(JsonElement values)=>values.EnumerateArray().Select(v=>new ScreenPoint(v[0].GetInt32(),v[1].GetInt32())).ToArray();
    private static ScreenRect Rect(JsonElement v)=>new(v.GetProperty("Left").GetInt32(),v.GetProperty("Top").GetInt32(),v.GetProperty("Right").GetInt32(),v.GetProperty("Bottom").GetInt32());
    internal static Probe[] BuildProbes(StandardGeometryContext context,string folder)
    {
        var grid=Grid(context,folder);using var doc=Protocol(folder);var result=new List<Probe>();
        foreach(var v in doc.RootElement.GetProperty("probes").EnumerateArray())
        {
            int y=v.GetProperty("commandY").GetInt32();var wanted=Points(v.GetProperty("path"));var body=v.GetProperty("bodyX");
            // Calibration role: exact screen command, no row claim and no margin requirement.
            var path=grid.HorizontalAtScreenY(grid.Context,y,wanted[0].X,wanted[^1].X,null,0).Points.ToArray();
            if(!path.SequenceEqual(wanted)||v.GetProperty("role").GetString()!="mapping-discriminator-not-quality")throw new InvalidDataException("Core calibration path differs from frozen oracle.");
            var reserve=new ScreenRect(path.Min(p=>p.X)-16,y-16,path.Max(p=>p.X)+17,y+17);
            if(reserve!=Rect(v.GetProperty("reservation")))throw new InvalidDataException("Calibration reservation differs from frozen oracle.");
            result.Add(new(v.GetProperty("label").GetString()!,y,v.GetProperty("lane").GetInt32(),body[0].GetInt32(),body[1].GetInt32(),reserve,path));
        }
        if(result.Count!=ProbeCount||result.Any(p=>p.Path.Length!=31||p.BodyLast-p.BodyFirst+1!=BodySections||p.BodyFirst!=p.Path[0].X+6)
            ||result.Select(p=>p.Label).Distinct().Count()!=ProbeCount)throw new InvalidDataException("Calibration denominator changed.");
        return result.ToArray();
    }
    internal static TexelTarget Target(string[] mask,ScreenRect indices)
    {
        if(mask.Length!=indices.Height||mask.Any(r=>r.Length!=indices.Width||r.Any(c=>c is not ('#' or '.'))))throw new InvalidDataException("Invalid picture mask.");
        var image=new PixelImage(indices.Width,indices.Height);
        for(int y=0;y<indices.Height;y++)for(int x=0;x<indices.Width;x++)if(mask[y][x]=='#')image.Set(y*indices.Width+x,new(0,0,0));
        return TexelTarget.Resample(true,image,indices);
    }
    internal static Picture BuildPicture(StandardGeometryContext context,string folder)
    {
        var grid=Grid(context,folder);using var doc=Protocol(folder);var p=doc.RootElement.GetProperty("picture");
        var indices=Rect(p.GetProperty("indexRect"));var mask=p.GetProperty("mask").EnumerateArray().Select(v=>v.GetString()!).ToArray();
        if(indices!=new ScreenRect(225,313,265,328)||p.GetProperty("rowMargin").GetDouble()!=.25||p.GetProperty("columnMargin").GetDouble()!=.25)throw new InvalidDataException("Picture policy changed.");
        // The same Core planner as any texel target: full 1148 family, margins 0.25.
        var plan=TexelPlanner.Plan(true,grid,grid.Context,Target(mask,indices),.25,.25);var frozen=p.GetProperty("runs").EnumerateArray().ToArray();
        if(plan.Runs.Count!=RunCount||frozen.Length!=RunCount)throw new InvalidDataException("Picture run denominator changed.");
        var runs=plan.Runs.Select((r,i)=>
        {
            var f=frozen[i];var path=r.Path.Points.ToArray();
            if(r.Row!=f.GetProperty("row").GetInt32()||r.FirstColumn!=f.GetProperty("firstColumn").GetInt32()||r.LastColumn!=f.GetProperty("lastColumn").GetInt32()
                ||r.Color!=new Rgb(0,0,0)||!path.SequenceEqual(Points(f.GetProperty("path")))||path[0].Y!=f.GetProperty("commandY").GetInt32()
                ||r.Path.MinimumRowMargin!=f.GetProperty("rowMargin").GetDouble())throw new InvalidDataException("Core picture plan differs from frozen oracle.");
            return new PictureRun(r.Row,r.FirstColumn,r.LastColumn,path[0].Y,r.Path.MinimumRowMargin!.Value,path);
        }).ToArray();
        var all=runs.SelectMany(r=>r.Path).ToArray();var reserve=new ScreenRect(all.Min(q=>q.X)-16,all.Min(q=>q.Y)-16,all.Max(q=>q.X)+17,all.Max(q=>q.Y)+17);
        if(reserve!=Rect(p.GetProperty("reservation")))throw new InvalidDataException("Picture reservation differs from frozen oracle.");
        var edges=p.GetProperty("edges").EnumerateArray().Select(e=>
        {
            bool vertical=e.GetProperty("orientation").GetString()=="V";string paint=e.GetProperty("paint").GetString()!;
            var starts=vertical?e.GetProperty("samples").EnumerateArray().Select(s=>new ScreenPoint(s[1].GetInt32(),s[0].GetInt32())).ToArray()
                :e.GetProperty("samples").EnumerateArray().Select(x=>new ScreenPoint(x.GetInt32(),e.GetProperty("startCross").GetInt32())).ToArray();
            var (dx,dy)=paint switch{"below"=>(0,-1),"above"=>(0,1),"right"=>(-1,0),"left"=>(1,0),_=>throw new InvalidDataException("Unknown edge side.")};
            if(vertical!=(dx!=0)||starts.Length<2||starts.Any(s=>!Inside(reserve,s)))throw new InvalidDataException("Invalid picture edge.");
            return new Edge(e.GetProperty("name").GetString()!,vertical,dx,dy,starts);
        }).ToArray();
        if(edges.Length!=10)throw new InvalidDataException("Picture edge denominator changed.");
        return new(indices,mask,runs,reserve,edges);
    }
    internal static bool Inside(ScreenRect r,ScreenPoint p)=>p.X>=r.Left&&p.X<r.Right&&p.Y>=r.Top&&p.Y<r.Bottom;
    internal static StandardTrackPairCli.TrackCase[] BuildCases(StandardGeometryContext context,string folder)
    {
        var probes=BuildProbes(context,folder);var picture=BuildPicture(context,folder);var cases=new List<StandardTrackPairCli.TrackCase>();
        StandardTrackPairCli.TrackCase Case(int i,string kind,ScreenRect reserve,ScreenPoint[][] paths)
        {
            if(reserve.Left<context.Canvas.Left||reserve.Top<context.Canvas.Top||reserve.Right>context.Canvas.Right||reserve.Bottom>context.Canvas.Bottom)
                throw new InvalidDataException("Calibration reservation leaves the selected Canvas.");
            foreach(var path in paths)StandardBandContour.Validate(path,context.Canvas);
            var lines=paths.Select(p=>new ScreenLine(p[0].X,p[0].Y,p[^1].X,p[^1].Y)).ToArray();
            return new(i,0,StandardRowOffgridPilot.Id(new{Revision,kind,paths}),new(i,0,kind,false,false,paths.Max(p=>p.Length)-1,reserve,[new("black-band",new(0,0,0),lines)]){Speed=StandardRepeatSeries.Speed},paths);
        }
        foreach(var (p,i) in probes.Select((p,i)=>(p,i)))cases.Add(Case(i,"calib-mapping-discriminator",p.Reservation,[p.Path]));
        cases.Add(Case(probes.Length,"calib-picture-40x15",picture.Reservation,picture.Runs.Select(r=>r.Path).ToArray()));
        return cases.ToArray();
    }
    private static void CheckSettings(Settings settings,StandardGeometryContext context,StandardTrackPairCli.TrackCase[] cases)
    {
        settings.Validate();
        if(!ReleaseProfile.Standard(settings)||StandardLineMotion.Enabled(settings)||settings.Text("speed_profile")!="Rapid"||StrokeTiming.Frame(settings)!=.02
            ||settings.PaintCalibration().Rect("canvas")!=context.Canvas||settings.Text("color_mode")!=context.ColorMode)
            throw new InvalidDataException("Calibration settings changed.");
        var guard=InputCommandGuard.From(settings,new(context.ClientOrigin.X,context.ClientOrigin.Y,context.ClientOrigin.X+context.ClientSize.Width,context.ClientOrigin.Y+context.ClientSize.Height));
        if(cases.SelectMany(c=>c.Paths).Any(p=>!guard.Allows(p)))throw new InvalidDataException("Calibration path intersects captured controls.");
        foreach(var a in cases)foreach(var b in cases.Where(b=>b.Sample.Index>a.Sample.Index))
            if(a.Sample.Reservation.Left<b.Sample.Reservation.Right&&b.Sample.Reservation.Left<a.Sample.Reservation.Right
                &&a.Sample.Reservation.Top<b.Sample.Reservation.Bottom&&b.Sample.Reservation.Top<a.Sample.Reservation.Bottom)
                throw new InvalidDataException("Independent calibration reservations overlap.");
    }
    internal static void CheckAssets(string folder,Probe[] probes,Picture picture)
    {
        _=Parameters(folder);_=Refined(folder);
        if(probes.Length!=ProbeCount||picture.Runs.Length!=RunCount||picture.Edges.Length!=10)throw new InvalidDataException("Calibration completeness denominator changed.");
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
        var probes=BuildProbes(context,assets);var picture=BuildPicture(context,assets);var cases=BuildCases(context,assets);CheckAssets(assets,probes,picture);
        var settings=original.Clone();CheckSettings(settings,context,cases);
        if(sourceId is not {Length:64}||!sourceId.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f'))throw new InvalidDataException("Missing source-row protocol identity.");
        StandardStraightPilotCli.Frame(baseline,context.Canvas,synthetic);if(!synthetic)StandardTexelView.ReadFrame(geometry);
        Directory.CreateDirectory(directory);var refs=new Dictionary<string,string>(StringComparer.Ordinal);
        void Copy(string from,string to){string path=Path.Combine(directory,to);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.Copy(from,path);refs.Add(to,Hash(path));}
        Copy(baseline,"reference/baseline.png");Copy(Path.ChangeExtension(baseline,"json"),"reference/baseline.json");
        if(!synthetic){Copy(geometry,"reference/geometry.png");Copy(Path.ChangeExtension(geometry,"json"),"reference/geometry.json");}
        foreach(string name in Assets.Keys)Copy(Path.Combine(assets,name),"fixtures/"+name);
        File.WriteAllText(Path.Combine(directory,"settings.json"),settings.Data.ToJsonString());
        var m=new Manifest(Revision,Policy,StandardTexelView.Policy,StandardRowPilot.BaselinePolicy,context,Parameters(assets),probes,picture,cases,refs,Hash(Path.Combine(directory,"settings.json")),
            Hash(typeof(Painter).Assembly.Location),Hash(typeof(Settings).Assembly.Location),sourceId,Guid.NewGuid().ToString("N"),"",synthetic);
        m=m with{ProtocolId=StandardRowOffgridPilot.Id(m)};StandardUvPilot.WriteNew(Path.Combine(directory,"protocol.json"),m);File.WriteAllText(Path.Combine(directory,"protocol.json.sha256"),Hash(Path.Combine(directory,"protocol.json")));
        StandardUvPilot.WriteNew(Path.Combine(directory,"series-status.json"),new{status="NOT_STARTED",requiredCases=ProbeCount+1,requiredStrokes=ProbeCount+RunCount,nativeInput=false,productionEligible=false,physicalQualityCertified=false,automaticRetry=false,automaticResume=false});
        Read(directory,true);return m;
    }
    internal static readonly string[] RetainedMarkers=["attempted.json","started.json","events.jsonl","baseline.png","final.png",FrontierSessionCapture.FileName,"geometry-check.json","startup-failures.jsonl"];
    internal static Manifest Read(string directory,bool offline=false)
    {
        if(!offline&&(RetainedMarkers.Any(n=>File.Exists(Path.Combine(directory,n)))||Directory.GetDirectories(directory,"sample-*").Length>0))
            throw new IOException("This attempt has retained data; retry/resume disabled.");
        string path=Path.Combine(directory,"protocol.json");if(Hash(path)!=File.ReadAllText(path+".sha256").Trim())throw new InvalidDataException("Calibration manifest changed.");
        var m=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path))??throw new InvalidDataException("Missing calibration manifest.");
        string fixtures=Path.Combine(directory,"fixtures");
        var expected=new[]{"reference/baseline.png","reference/baseline.json"}.Concat(m.SyntheticSource?[]:new[]{"reference/geometry.png","reference/geometry.json"}).Concat(Assets.Keys.Select(n=>"fixtures/"+n)).ToArray();
        bool Digest(string? s)=>s is {Length:64}&&s.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
        if(m.Revision!=Revision||m.Policy!=Policy||m.ViewPolicy!=StandardTexelView.Policy||m.BaselinePolicy!=StandardRowPilot.BaselinePolicy
            ||m.ProtocolId!=StandardRowOffgridPilot.Id(m with{ProtocolId=""})||!Guid.TryParseExact(m.CollectionId,"N",out _)||!Digest(m.SourceRowProtocolId)||!Digest(m.AppSha256)||!Digest(m.CoreSha256)
            ||JsonSerializer.Serialize(m.Model)!=JsonSerializer.Serialize(Parameters(fixtures))||JsonSerializer.Serialize(m.Probes)!=JsonSerializer.Serialize(BuildProbes(m.Context,fixtures))
            ||JsonSerializer.Serialize(m.Picture)!=JsonSerializer.Serialize(BuildPicture(m.Context,fixtures))
            ||JsonSerializer.Serialize(m.Cases)!=JsonSerializer.Serialize(BuildCases(m.Context,fixtures))||m.SettingsSha256!=Hash(Path.Combine(directory,"settings.json"))
            ||m.ReferenceFiles.Count!=expected.Length||m.ReferenceFiles.Keys.Any(k=>!expected.Contains(k,StringComparer.Ordinal))
            ||!offline&&(m.SyntheticSource||m.AppSha256!=Hash(typeof(Painter).Assembly.Location)||m.CoreSha256!=Hash(typeof(Settings).Assembly.Location)))
            throw new InvalidDataException("Calibration policy, paths, picture, model, references, settings or binaries changed.");
        foreach(var (name,h) in m.ReferenceFiles)if(Hash(Path.Combine(directory,name))!=h)throw new InvalidDataException("Calibration reference changed.");
        CheckAssets(fixtures,m.Probes,m.Picture);CheckSettings(Settings.Load(Path.Combine(directory,"settings.json")),m.Context,m.Cases);
        StandardStraightPilotCli.Frame(Path.Combine(directory,"reference/baseline.png"),m.Context.Canvas,m.SyntheticSource);return m;
    }
    internal static StandardTrackPairCli.Manifest View(Manifest m)=>new(StandardTrackPairCli.Revision,"calib-research",m.ProtocolId,m.Context,m.Cases,m.ProtocolId,"",m.ReferenceFiles,m.SettingsSha256,m.AppSha256,m.CoreSha256,m.SyntheticSource,StandardTrackPairCli.CapturePolicy);
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
        StandardEvidenceSeriesCli.RunCollection(directory,m.Context.Canvas,ProbeCount+1,20,p=>result=p.CollectCalibPilot(m,directory),
            (settings,window)=>{var prepared=FrontierSessionCapture.CaptureFresh(settings,window,Binding(m),directory);StandardTexelView.Check(directory,true);return prepared;},showDialog:false);
        if(result?.Status!="DATA_COLLECTED")Environment.ExitCode=1;else Verify(directory,m);
    }
}
