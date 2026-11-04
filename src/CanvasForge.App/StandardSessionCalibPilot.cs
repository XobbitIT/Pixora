using System.Globalization;
using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

// Session calibration 1 (design stages 1+2, docs/SESSION_CALIBRATION_DESIGN_UA.md): the frozen texel profile 4d9d4e1d
// (derived family 1196, row margin 0.20) bound to the user's main-config rect and requested Max Speed. No refit, no
// coefficient/anchor change, no texel START. Stage 1 is the explicit two-frame preparation (16 ROI for the exact new rect,
// fresh confirmation, frozen product view); stage 2 is three short H controls. Research only.
internal static class StandardSessionCalibPilot
{
    internal const string Revision="standard-session-calib-pilot-v1";
    internal const string Policy="session1-frozen1196-exact-user-rect-3controls-margin020-label015-outside0-v1";
    internal const string ProtocolFile="protocol-session1.json",ProtocolSha256="72f777fc338e9de3a027977a365ed77b5ed8b5002e0fc2d4f3279ddbefa613c4";
    internal const string ConfigReference="reference/config-v3.json";
    internal const int BodySections=19;
    internal static readonly int[] ControlRows=[203,465,515];
    internal sealed record Control(string Label,string SourceLabel,int Row,int CommandY,int BodyFirst,int BodyLast,double MinimumRowMargin,ScreenRect Reservation,ScreenPoint[] Path);
    internal sealed record Manifest(string Revision,string Policy,string ViewPolicy,StandardGeometryContext Context,TexelSessionBinding Binding,string BindingId,
        string ProfileSha256,string FrozenGridId,string SessionGridId,Control[] Controls,StandardTrackPairCli.TrackCase[] Cases,Dictionary<string,string> ReferenceFiles,
        string SettingsSha256,string AppSha256,string CoreSha256,string CollectionId,string ProtocolId,bool SyntheticSource);
    internal sealed record Design(StandardGeometryContext Context,TexelSessionBinding Binding,string FrozenGridId,string SessionGridId,Control[] Controls,StandardTrackPairCli.TrackCase[] Cases);
    internal static string Hash(string path)=>StandardBoundaryReview.Hash(path);
    private static bool Digest(string? d)=>d is {Length:64}&&d.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static JsonDocument Protocol(string folder)
    {
        string path=Path.Combine(folder,ProtocolFile);
        if(Hash(path)!=ProtocolSha256)throw new InvalidDataException("Frozen session protocol changed.");
        return JsonDocument.Parse(File.ReadAllBytes(path));
    }
    private static ScreenPoint[] Points(JsonElement values)=>values.EnumerateArray().Select(v=>new ScreenPoint(v[0].GetInt32(),v[1].GetInt32())).ToArray();
    private static ScreenRect Rect(JsonElement v)=>new(v.GetProperty("Left").GetInt32(),v.GetProperty("Top").GetInt32(),v.GetProperty("Right").GetInt32(),v.GetProperty("Bottom").GetInt32());
    private static ScreenRect Box(IEnumerable<ScreenPoint> p){var a=p.ToArray();return new(a.Min(q=>q.X)-16,a.Min(q=>q.Y)-16,a.Max(q=>q.X)+17,a.Max(q=>q.Y)+17);}
    // Frozen product routes (pic8 A, Product_2/3, pic9 R/L/C) on the unchanged frozen grid; the stage-0 audit rebuilds them in the new rect.
    internal static TexelCompensatedPlan[] AuditRoutes(TexelGrid frozen,string folder)
    {
        using var doc=Protocol(folder);
        var routes=doc.RootElement.GetProperty("auditTargets").EnumerateArray().Select(t=>TexelCompensatedPlanner.Plan(true,frozen,frozen.Context,
            t.GetProperty("mask").EnumerateArray().Select(v=>v.GetString()!),Rect(t.GetProperty("indexRect")),0,default,
            TexelCompensatedPlanner.FullCoverageEndPhase,TexelCompensatedPlanner.ProductRowMargin)).ToArray();
        if(routes.Length!=6)throw new InvalidDataException("Session audit target denominator changed.");
        return routes;
    }
    // Controls are recomputed on the rebound grid with the product row margin 0.20 and must equal the frozen pic9 Ctl1/Ctl6/Ctl5 paths.
    internal static Control[] BuildControls(TexelGrid grid,string folder)
    {
        using var doc=Protocol(folder);var result=new List<Control>();
        foreach(var v in doc.RootElement.GetProperty("controls").EnumerateArray())
        {
            int row=v.GetProperty("row").GetInt32();var wanted=Points(v.GetProperty("path"));var body=v.GetProperty("bodyX");
            var span=grid.CenteredRowScreenSpan(grid.Context,row,wanted[0].X,wanted[^1].X,TexelCompensatedPlanner.ProductRowMargin);var path=span.Points.ToArray();
            if(!path.SequenceEqual(wanted)||path[0].Y!=v.GetProperty("commandY").GetInt32()||Box(path)!=Rect(v.GetProperty("reservation"))||span.MinimumRowMargin is not double margin
                ||margin<TexelCompensatedPlanner.ProductRowMargin)
                throw new InvalidDataException("Session control path differs from the frozen pic9 control or lacks margin 0.20.");
            result.Add(new(v.GetProperty("label").GetString()!,v.GetProperty("sourceLabel").GetString()!,row,path[0].Y,body[0].GetInt32(),body[1].GetInt32(),margin,Box(path),path));
        }
        if(!result.Select(c=>c.Row).SequenceEqual(ControlRows)||result.Any(c=>c.Path.Length!=31||c.BodyLast-c.BodyFirst+1!=BodySections||c.BodyFirst!=c.Path[0].X+6))
            throw new InvalidDataException("Session control denominator changed.");
        return result.ToArray();
    }
    internal static StandardTrackPairCli.TrackCase[] BuildCases(StandardGeometryContext context,Control[] controls)
    {
        return controls.Select((c,i)=>
        {
            var r=c.Reservation;
            if(r.Left<context.Canvas.Left||r.Top<context.Canvas.Top||r.Right>context.Canvas.Right||r.Bottom>context.Canvas.Bottom)
                throw new InvalidDataException("Session control reservation leaves the user Canvas.");
            StandardBandContour.Validate(c.Path,context.Canvas);
            var line=new ScreenLine(c.Path[0].X,c.Path[0].Y,c.Path[^1].X,c.Path[^1].Y);
            return new StandardTrackPairCli.TrackCase(i,0,StandardRowOffgridPilot.Id(new{Revision,kind="session-control",paths=new[]{c.Path}}),
                new(i,0,"session-control",false,false,c.Path.Length-1,r,[new("black-band",new(0,0,0),[line])]){Speed=StandardRepeatSeries.Speed},[c.Path]);
        }).ToArray();
    }
    // The candidate structure provenance is the main config's own 16-ROI reference; the live record replaces it with the fresh frame.
    private static string StructureSha(Settings settings)
    {
        string? sha=settings.Data["canvas_structure"]?["FrameSha256"]?.GetValue<string>()?.ToLowerInvariant();
        if(!Digest(sha))throw new InvalidDataException("Основний конфіг не має еталона структури полотна.");
        return sha!;
    }
    private static void CheckSettings(Settings settings,StandardGeometryContext context,StandardTrackPairCli.TrackCase[] cases,TexelSessionBinding binding)
    {
        settings.Validate();var effective=DrawingWorkflow.Effective(settings);Painter.ApplyResearchShape(effective);
        if(!ReleaseProfile.Standard(settings)||effective.Int("brush_shape_slot",3)!=3||settings.Int(Painter.ResearchShapeKey,0)!=0||StandardLineMotion.Enabled(settings)
            ||settings.Text("speed_profile")!=binding.RequestedSpeed||binding.RequestedSpeed!="Max Speed"||StrokeTiming.Frame(settings)!=.02
            ||settings.PaintCalibration().Rect("canvas")!=context.Canvas||settings.Mode!=ColorMode.RustPalette||settings.Text("color_mode")!=context.ColorMode
            ||TexelExperimentSettings.Enabled(settings)||settings.Bool("adaptive_brush")||settings.Bool("coverage_audit"))
            throw new InvalidDataException("Налаштування сесії відрізняються від замороженого основного конфігу (Standard, Size 3, круглий, 20 мс, Max Speed, палітра Rust, тексельний START вимкнено).");
        if(!settings.Palette().Any(p=>p.Color==new Rgb(0,0,0)&&p.ClickPoint.HasValue))throw new InvalidDataException("Основний конфіг не має чорного кольору палітри з точкою кліку.");
        var guard=InputCommandGuard.From(settings,new(context.ClientOrigin.X,context.ClientOrigin.Y,context.ClientOrigin.X+context.ClientSize.Width,context.ClientOrigin.Y+context.ClientSize.Height));
        if(cases.SelectMany(c=>c.Paths).Any(p=>!guard.Allows(p)))throw new InvalidDataException("Session control path intersects captured controls.");
        foreach(var a in cases)foreach(var b in cases.Where(b=>b.Sample.Index>a.Sample.Index))
            if(a.Sample.Reservation.Left<b.Sample.Reservation.Right&&b.Sample.Reservation.Left<a.Sample.Reservation.Right
                &&a.Sample.Reservation.Top<b.Sample.Reservation.Bottom&&b.Sample.Reservation.Top<a.Sample.Reservation.Bottom)
                throw new InvalidDataException("Independent session reservations overlap.");
    }
    // Stage 0 before packaging: binding from the copied main config and the real App/Core hashes; a failed audit stops here.
    internal static Design Build(Settings settings,string fixtures,string appSha256,string coreSha256)
    {
        // The user's own selected rect is bound as-is. The frozen grid is in absolute screen coordinates (Rebind keeps every
        // command point), so the rect is only a bound: the audit below requires it to contain the column/row domains and every
        // frozen route with its ±16/±17 reservation, and the live 3-control check confirms the view. The protocol's "userRect"
        // is the historical pilot rect (0108), kept in the SHA-frozen asset but no longer required.
        var frozen=TexelProduct.Grid(appSha256,coreSha256);var cal=settings.PaintCalibration();var rect=cal.Rect("canvas");
        if(!rect.Valid)throw new InvalidDataException("Основний конфіг не має вибраного полотна.");
        var effective=DrawingWorkflow.Effective(settings);Painter.ApplyResearchShape(effective);
        var binding=new TexelSessionBinding(rect,TexelProduct.ProfileSha256,frozen.Id,
            cal.SessionClient??throw new InvalidDataException("Основний конфіг не має origin сесії."),
            cal.SessionSize??throw new InvalidDataException("Основний конфіг не має розміру клієнта сесії."),cal.SessionDpi,
            effective.Int("brush_shape_slot",3),PaintTimingPlan.DefaultSize(settings),StrokeTiming.Frame(settings),
            StrokeTiming.EndHold(settings,StandardRepeatSeries.Speed),StrokeTiming.Release(settings),settings.Text("speed_profile"),
            StandardRepeatSeries.Speed,appSha256,coreSha256,TexelProduct.ViewPngSha256,StructureSha(settings));
        var audit=TexelSessionAudit.Audit(binding,TexelProduct.ProfileSha256,frozen,AuditRoutes(frozen,fixtures));
        if(!audit.Passed)throw new InvalidDataException("Офлайн-аудит прив'язки сесії не пройшов: "+string.Join("; ",audit.Reasons));
        var grid=TexelSessionAudit.Rebind(frozen,rect);var context=grid.Context.Geometry;context.Validate();
        var controls=BuildControls(grid,fixtures);var cases=BuildCases(context,controls);CheckSettings(settings,context,cases,binding);
        return new(context,binding,frozen.Id,grid.Id,controls,cases);
    }
    internal static void Init(string mainConfig,string assets,string directory)=>Create(directory,mainConfig,assets);
    internal static Manifest Create(string directory,string mainConfig,string assets,bool synthetic=false)
    {
        directory=Path.GetFullPath(directory);StandardUvPilot.Separate(Path.GetDirectoryName(Path.GetFullPath(mainConfig))!,directory);
        if(Directory.Exists(directory)&&Directory.EnumerateFileSystemEntries(directory).Any())throw new IOException("Session directory already has data; nothing is overwritten.");
        string app=Hash(typeof(Painter).Assembly.Location),core=Hash(typeof(Settings).Assembly.Location);
        // The original main config is only read: a byte copy goes to the isolated package and every later step uses the copy.
        var original=File.ReadAllBytes(mainConfig);string staging=Path.Combine(Path.GetTempPath(),"pixora-session1-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        Design design;Settings settings;
        try{File.WriteAllBytes(Path.Combine(staging,"config-v3.json"),original);settings=Settings.Load(Path.Combine(staging,"config-v3.json"));design=Build(settings,assets,app,core);}
        finally{Directory.Delete(staging,true);}
        Directory.CreateDirectory(directory);var refs=new Dictionary<string,string>(StringComparer.Ordinal);
        void Put(string to,byte[] bytes){string path=Path.Combine(directory,to);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using(var f=new FileStream(path,FileMode.CreateNew,FileAccess.Write))f.Write(bytes);refs.Add(to,Hash(path));}
        Put(ConfigReference,original);var (png,meta)=TexelProduct.ReferenceViewFiles();Put("reference/geometry.png",png);Put("reference/geometry.json",meta);
        Put("fixtures/"+ProtocolFile,File.ReadAllBytes(Path.Combine(assets,ProtocolFile)));
        if(!synthetic)StandardTexelView.ReadFrame(Path.Combine(directory,"reference/geometry.png"));
        File.WriteAllText(Path.Combine(directory,"settings.json"),settings.Data.ToJsonString());
        var m=new Manifest(Revision,Policy,StandardTexelView.Policy,design.Context,design.Binding,design.Binding.Id,TexelProduct.ProfileSha256,design.FrozenGridId,design.SessionGridId,
            design.Controls,design.Cases,refs,Hash(Path.Combine(directory,"settings.json")),app,core,Guid.NewGuid().ToString("N"),"",synthetic);
        m=m with{ProtocolId=StandardRowOffgridPilot.Id(m)};StandardUvPilot.WriteNew(Path.Combine(directory,"protocol.json"),m);
        File.WriteAllText(Path.Combine(directory,"protocol.json.sha256"),Hash(Path.Combine(directory,"protocol.json")));
        StandardUvPilot.WriteNew(Path.Combine(directory,"session-audit.json"),new{stage=0,status="PASSED",bindingId=m.BindingId,binding=m.Binding,profileSha256=m.ProfileSha256,
            frozenGridId=m.FrozenGridId,sessionGridId=m.SessionGridId,auditRoutes=new[]{"pic8-A","product2","product3","pic9-R","pic9-L","pic9-C"},
            rule="TexelSessionAudit.Audit: exact rect contains column/row domains, provenance SHA, origin/client/DPI, slot/Size 3, frame 0.02, hold/release >= profile, effective emitter Max Speed, every frozen route rebuilt with identical points and +-16/17 reservations inside the rect",
            startEnabled=false,nativeInput=false,physicalQualityCertified=false});
        StandardUvPilot.WriteNew(Path.Combine(directory,"series-status.json"),new{status="NOT_STARTED",requiredCases=design.Cases.Length,requiredStrokes=design.Cases.Length,
            nativeInput=false,productionEligible=false,physicalQualityCertified=false,automaticRetry=false,automaticResume=false});
        Read(directory,true);return m;
    }
    internal static Manifest Read(string directory,bool offline=false)
    {
        if(!offline&&(StandardRowCalibPilot.RetainedMarkers.Any(n=>File.Exists(Path.Combine(directory,n)))||Directory.GetDirectories(directory,"sample-*").Length>0))
            throw new IOException("This attempt has retained data; retry/resume disabled.");
        string path=Path.Combine(directory,"protocol.json");if(Hash(path)!=File.ReadAllText(path+".sha256").Trim())throw new InvalidDataException("Session manifest changed.");
        var m=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path))??throw new InvalidDataException("Missing session manifest.");
        string fixtures=Path.Combine(directory,"fixtures");
        var expected=new[]{ConfigReference,"reference/geometry.png","reference/geometry.json","fixtures/"+ProtocolFile};
        if(m.Revision!=Revision||m.Policy!=Policy||m.ViewPolicy!=StandardTexelView.Policy||m.ProfileSha256!=TexelProduct.ProfileSha256
            ||m.ProtocolId!=StandardRowOffgridPilot.Id(m with{ProtocolId=""})||!Guid.TryParseExact(m.CollectionId,"N",out _)||!Digest(m.AppSha256)||!Digest(m.CoreSha256)
            ||m.SettingsSha256!=Hash(Path.Combine(directory,"settings.json"))||m.ReferenceFiles.Count!=expected.Length||m.ReferenceFiles.Keys.Any(k=>!expected.Contains(k,StringComparer.Ordinal))
            ||!offline&&(m.SyntheticSource||m.AppSha256!=Hash(typeof(Painter).Assembly.Location)||m.CoreSha256!=Hash(typeof(Settings).Assembly.Location)))
            throw new InvalidDataException("Session policy, references, settings or binaries changed.");
        foreach(var (name,h) in m.ReferenceFiles)if(Hash(Path.Combine(directory,name))!=h)throw new InvalidDataException("Session reference changed.");
        if(m.ReferenceFiles["reference/geometry.png"]!=TexelProduct.ViewPngSha256)throw new InvalidDataException("Session view witness is not the frozen product view.");
        // settings.json must be exactly the copied main config as Settings.Load reads it: no edited rect, speed or timing.
        var settings=Settings.Load(Path.Combine(directory,"settings.json"));
        if(Settings.Load(Path.Combine(directory,ConfigReference)).Data.ToJsonString()!=settings.Data.ToJsonString())throw new InvalidDataException("Session settings differ from the copied main config.");
        var d=Build(settings,fixtures,m.AppSha256,m.CoreSha256);
        if(JsonSerializer.Serialize(d.Context)!=JsonSerializer.Serialize(m.Context)||JsonSerializer.Serialize(d.Binding)!=JsonSerializer.Serialize(m.Binding)||d.Binding.Id!=m.BindingId
            ||d.FrozenGridId!=m.FrozenGridId||d.SessionGridId!=m.SessionGridId||JsonSerializer.Serialize(d.Controls)!=JsonSerializer.Serialize(m.Controls)
            ||JsonSerializer.Serialize(d.Cases)!=JsonSerializer.Serialize(m.Cases))
            throw new InvalidDataException("Session binding, controls or cases differ from the frozen rebuild.");
        return m;
    }
    internal static StandardTrackPairCli.Manifest View(Manifest m)=>new(StandardTrackPairCli.Revision,"session1-research",m.ProtocolId,m.Context,m.Cases,m.ProtocolId,"",m.ReferenceFiles,m.SettingsSha256,m.AppSha256,m.CoreSha256,m.SyntheticSource,StandardTrackPairCli.CapturePolicy);
    internal static FrontierSessionCapture.Binding Binding(Manifest m)=>new(m.ProtocolId,m.SettingsSha256,m.AppSha256,m.CoreSha256,m.Context,m.SyntheticSource);
    internal static void Verify(string directory,Manifest? manifest=null,bool requireCompleteStatus=true)
    {
        var m=manifest??Read(directory,true);StandardTrackPairCli.ValidateCompleted(directory,View(m),requireCompleteStatus);
        if(!m.SyntheticSource){FrontierSessionCapture.VerifyUse(directory,directory,Binding(m));StandardTexelView.Check(directory,false);
            StandardRowPilot.RequireFreshBaseline(FrontierSessionCapture.Baseline(directory,Binding(m)),StandardStraightPilotCli.Frame(Path.Combine(directory,"baseline.png"),m.Context.Canvas,false),m.Context.Canvas);}
    }
    // Stage 1 then stage 2 in one attempt: the explicit capture (source frame + fresh confirmation, 16 ROI for the exact rect) and the
    // frozen view must pass before the Painter exists; every later stroke re-checks the editor proof. Any refusal ends before paint-down.
    internal static void Run(string directory)=>Run(directory,CancellationToken.None);
    internal static void Run(string directory,CancellationToken cancellation,bool setExitCode=true)
    {
        var m=Read(directory);StandardEvidenceSeriesRunner.Result? result=null;
        StandardEvidenceSeriesCli.RunCollection(directory,m.Context.Canvas,m.Cases.Length,10,p=>result=p.CollectSessionCalibPilot(m,directory),
            (settings,window)=>
            {
                var prepared=FrontierSessionCapture.CaptureFresh(settings,window,Binding(m),directory);
                StandardTexelView.Check(directory,true);TexelProduct.RequireView(Images.LoadNative(Path.Combine(directory,FrontierSessionCapture.FrameName)));
                return prepared;
            },showDialog:false,cancellation:cancellation);
        if(result?.Status!="DATA_COLLECTED"){if(setExitCode)Environment.ExitCode=1;}else Verify(directory,m);
    }
}

internal static class StandardSessionCalibAnalysis
{
    internal const string Confirmed="SESSION_TRANSFER_CONFIRMED",NotConfirmed="SESSION_TRANSFER_NOT_CONFIRMED",Incomplete="INCOMPLETE";
    internal sealed record ControlResult(string Label,int Row,int CommandY,int Required,int Observed,int? MeasuredRow,double? TopMedianAbsolute,double? BottomMedianAbsolute,
        bool WithinLimit,bool MatchesRow);
    internal sealed record Result(string Status,string[] Reasons,ControlResult[] Controls,int MandatorySections,int? OutsideChangedPixels)
    {public bool BindingActive=>Status==StandardSessionCalibAnalysis.Confirmed;public bool StartEnabled=>false;public bool ProductionEligible=>false;public bool PhysicalQualityCertified=>false;}
    // Frozen before collection: completeness first (else INCOMPLETE), then label arithmetic and the outside count (else NOT_CONFIRMED).
    internal static Result Analyze(StandardSessionCalibPilot.Control[] controls,TexelRowObservation rows,ScreenRect domain,
        IReadOnlyDictionary<string,StandardEdgeSection[]> lines,int? outside,IEnumerable<string>? failures=null)
    {
        var reasons=failures?.ToList()??[];int required=controls.Length*StandardSessionCalibPilot.BodySections;
        bool Valid(StandardEdgeSection[]? s,int first,int last)=>s is not null&&s.Length==last-first+1&&s.Select(r=>r.Along).SequenceEqual(Enumerable.Range(first,last-first+1))
            &&s.All(r=>double.IsFinite(r.Low)&&double.IsFinite(r.High)&&r.Low<r.High&&r.Low>=domain.Top-.5&&r.High<=domain.Bottom-.5);
        // A failed run/verification is reported by its own reason; derived "missing section" lines would only hide it.
        if(reasons.Count==0)
        {
            if(controls.Length!=StandardSessionCalibPilot.ControlRows.Length||!controls.Select(c=>c.Row).SequenceEqual(StandardSessionCalibPilot.ControlRows)
                ||lines.Count!=controls.Length||lines.Keys.Any(k=>!controls.Any(c=>c.Label==k)))reasons.Add("Session control denominator changed.");
            foreach(var c in controls)if(!lines.TryGetValue(c.Label,out var s)||!Valid(s,c.BodyFirst,c.BodyLast))reasons.Add(c.Label+": missing/invalid/duplicate/out-of-order body section.");
            if(outside is null)reasons.Add("Outside-reservation comparison unavailable.");
        }
        int Observed(StandardSessionCalibPilot.Control c)=>lines.GetValueOrDefault(c.Label)?.Count(r=>double.IsFinite(r.Low)&&double.IsFinite(r.High)&&r.Low<r.High)??0;
        if(reasons.Count>0)return new(Incomplete,reasons.ToArray(),controls.Select(c=>new ControlResult(c.Label,c.Row,c.CommandY,c.BodyLast-c.BodyFirst+1,Observed(c),null,null,null,false,false)).ToArray(),required,outside);
        // All 57 mandatory sections are valid before any label arithmetic.
        var results=controls.Select(c=>
        {
            var s=lines[c.Label];var top=s.Select(r=>StandardRowCalibAnalysis.Observation(rows,r.Low)-rows.TopPhase).ToArray();
            int j=(int)Math.Round(StandardRowCalibAnalysis.Median(top),MidpointRounding.ToEven);
            double tm=StandardRowCalibAnalysis.Median(top.Select(v=>Math.Abs(v-j)));
            double bm=StandardRowCalibAnalysis.Median(s.Select(r=>Math.Abs(StandardRowCalibAnalysis.Observation(rows,r.High)-rows.BottomPhase-(j+rows.EdgeLabelSpan))));
            bool ok=tm<=StandardRowCalibAnalysis.LabelLimit&&bm<=StandardRowCalibAnalysis.LabelLimit;
            return new ControlResult(c.Label,c.Row,c.CommandY,s.Length,s.Length,j,tm,bm,ok,j==c.Row);
        }).ToArray();
        foreach(var r in results.Where(r=>!r.WithinLimit))reasons.Add(r.Label+": median |residual| > 0.15 on an edge.");
        foreach(var r in results.Where(r=>!r.MatchesRow))reasons.Add($"{r.Label}: top label {r.MeasuredRow} differs from planned row {r.Row}.");
        if(outside!=0)reasons.Add($"{outside} pixel(s) changed by RGB>12 outside the reservations.");
        return new(reasons.Count==0?Confirmed:NotConfirmed,reasons.ToArray(),results,required,outside);
    }
    internal static Result Review(string directory,string output,bool synthetic=false)
    {
        directory=Path.GetFullPath(directory);output=Path.GetFullPath(output);StandardUvPilot.Separate(directory,output);
        var files=Directory.GetFiles(directory,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToDictionary(p=>p,StandardSessionCalibPilot.Hash,StringComparer.Ordinal);
        var m=StandardSessionCalibPilot.Read(directory,true);if(m.SyntheticSource!=synthetic)throw new InvalidDataException("Synthetic/native mismatch.");
        var failures=new List<string>();try{StandardSessionCalibPilot.Verify(directory,m);}
        catch(Exception e) when(e is IOException or InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException){failures.Add(e.Message);}
        var lines=new Dictionary<string,StandardEdgeSection[]>();var csv=new List<string>{"label,along,low,high,region,status,reason"};int? outside=null;
        string Number(double d)=>d.ToString("R",CultureInfo.InvariantCulture);
        PixelImage Frame(string path)=>StandardStraightPilotCli.Frame(path,m.Context.Canvas,synthetic);
        if(failures.Count==0)
        {
            foreach(var (c,i) in m.Controls.Select((c,i)=>(c,i)))
            {
                string folder=Path.Combine(directory,$"sample-{i:D2}/black-band");var before=Frame(Path.Combine(folder,"stroke-000-before.png"));var after=Frame(Path.Combine(folder,"stroke-000-after.png"));var body=new List<StandardEdgeSection>();
                foreach(var q in c.Path)
                {
                    bool mandatory=q.X>=c.BodyFirst&&q.X<=c.BodyLast;var row=StandardBandResponse.MeasureSection(before,after,m.Context.Canvas,false,q.Y,q.X,mandatory);
                    if(row.Edges is{} edge){if(mandatory)body.Add(edge);csv.Add($"{c.Label},{q.X},{Number(edge.Low)},{Number(edge.High)},{(mandatory?"body":"cap")},measured,");}
                    else csv.Add($"{c.Label},{q.X},,,{(mandatory?"body":"cap")},missing,{row.Reason}");
                }
                lines.Add(c.Label,body.ToArray());
            }
            var baseline=Frame(Path.Combine(directory,"baseline.png"));var final=Frame(Path.Combine(directory,"final.png"));var c0=m.Context.Canvas;int count=0;
            for(int y=c0.Top;y<c0.Bottom;y++)for(int x=c0.Left;x<c0.Right;x++)
            {
                var pt=new ScreenPoint(x,y);if(m.Cases.Any(c=>StandardRowCalibPilot.Inside(c.Sample.Reservation,pt)))continue;
                int i=(y-c0.Top)*c0.Width+x-c0.Left;if(RustSlider.Delta(baseline.Color(i),final.Color(i))>12)count++;
            }
            outside=count;
        }
        var grid=TexelSessionAudit.Rebind(TexelProduct.Grid(m.AppSha256,m.CoreSha256),m.Context.Canvas);
        var result=Analyze(m.Controls,grid.Rows,grid.RowDomain,lines,outside,failures);
        // The live binding records the fresh 16-ROI frame of this session; it is active only for a confirmed transfer and never enables START here.
        var live=m.Binding;
        if(!synthetic&&failures.Count==0)
        {var (record,_)=FrontierSessionCapture.Read(directory,Binding(m));live=live with{StructureSha256=record.FrameSha256.ToLowerInvariant()};}
        if(files.Count!=Directory.GetFiles(directory,"*",SearchOption.AllDirectories).Length||files.Any(p=>!File.Exists(p.Key)||StandardSessionCalibPilot.Hash(p.Key)!=p.Value))
            throw new IOException("Session evidence changed during review.");
        Directory.CreateDirectory(output);File.WriteAllLines(Path.Combine(output,"sections.csv"),csv);
        StandardUvPilot.WriteNew(Path.Combine(output,"session-binding.json"),new{revision="texel-session-binding-record-v1",status=result.Status,bindingActive=result.BindingActive,
            startEnabled=false,candidateBindingId=m.BindingId,liveBinding=live,liveBindingId=live.Id,m.ProfileSha256,m.FrozenGridId,m.SessionGridId,m.ProtocolId,
            note="Stage 3 (texel START from the main config) is a separate step after SESSION_TRANSFER_CONFIRMED; nothing here enables it.",
            productionEligible=false,physicalQualityCertified=false});
        StandardUvPilot.WriteNew(Path.Combine(output,"summary.json"),new{StandardSessionCalibPilot.Revision,StandardSessionCalibPilot.Policy,m.ProtocolId,result,
            requiredCases=m.Cases.Length,requiredStrokes=m.Cases.Sum(c=>c.Paths.Length),mandatoryBodySections=result.MandatorySections,completeDataBeforeArithmetic=true,
            labelRule="j=round(median(g(top)-topPhase)); CONFIRMED needs j == planned row, median|g(top)-topPhase-j|<=0.15 and median|g(bottom)-bottomPhase-(j+2)|<=0.15 on all 3 controls and 0 outside RGB>12 changes",
            incompleteRule="any missing/invalid of the 57 body sections, failed run/verification or unavailable outside comparison -> INCOMPLETE",
            failureRule="NOT_CONFIRMED or INCOMPLETE leaves the binding inactive; no fitting, no coefficient change",
            sourceFiles=files.ToDictionary(p=>Path.GetRelativePath(directory,p.Key).Replace('\\','/'),p=>p.Value),sourceUnchanged=true,synthetic,nativeInputByReviewer=false,
            productionEligible=false,physicalQualityCertified=false});
        return result;
    }
    private static FrontierSessionCapture.Binding Binding(StandardSessionCalibPilot.Manifest m)=>StandardSessionCalibPilot.Binding(m);
}
