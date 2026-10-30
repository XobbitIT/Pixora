using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

// Characterize the previously unmeasured row axis with the existing contour
// transport. No transposed column model, fitting, correction or START route.
internal static class StandardRowPilot
{
    internal const string Revision="standard-row-characterization-v2";
    internal const string BaselinePolicy="explicit-fresh-confirmed-canvas-rgb12-v1";
    internal const string Policy="row-native-local50-body560-1390-train8-holdout4-v1";
    internal const int First=520,Last=1430,BodyFirst=560,BodyLast=1390;
    internal const string Seed="pixora-row-axis-20261023-v1";
    internal sealed record Line(string Label,int Y,string Role,ScreenPoint[] Path);
    internal sealed record Manifest(string Revision,string Policy,string ViewPolicy,string BaselinePolicy,StandardGeometryContext Context,Line[] Lines,
        StandardTrackPairCli.TrackCase[] Cases,Dictionary<string,string> ReferenceFiles,string SettingsSha256,
        string AppSha256,string CoreSha256,string SourceUvProtocolId,string CollectionId,bool SyntheticSource,string ProtocolId);
    internal sealed record Edge(double Min,double Max,double SignedEventSum,StandardTexelAnalysis.Event[] Events);
    internal sealed record MeasuredLine(string Label,int Observed,int Missing,Edge? Top,Edge? Bottom,double? MeanWidth,double? WidthRange);
    internal sealed record Result(string Status,string[] Reasons,MeasuredLine[] Lines);
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    private static string Hash(string p)=>StandardBoundaryReview.Hash(p);
    private static string Id(object value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value))).ToLowerInvariant();
    private static bool Digest(string? value)=>value is {Length:64}&&value.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
    internal static Line[] BuildLines(StandardGeometryContext context)
    {
        context.Validate();
        var expected=new StandardGeometryContext(new(465,148,1510,1192),default,new(2560,1440),96,3,3,
            StandardRepeatSeries.Speed,.02,.02,.02,"Rust Palette");
        if(context.Id!=expected.Id)throw new InvalidDataException("Row pilot requires its exact captured physical context; no axis swap, recrop or scaling.");
        // Holdout data are collected last and never measured by this reviewer.
        // This is one characterization block, NOT a time-stability experiment.
        return Enumerable.Range(0,12).Select(i=>new{Y=220+80*i,Role=i%3==1?"held-out":"training"})
            .OrderBy(x=>x.Role=="training"?0:1).ThenBy(x=>Id(new{Seed,x.Y}),StringComparer.Ordinal)
            .Select(x=>new Line($"H-{x.Y}",x.Y,x.Role,Enumerable.Range(First,Last-First+1).Select(a=>new ScreenPoint(a,x.Y)).ToArray())).ToArray();
    }
    internal static StandardTrackPairCli.TrackCase[] BuildCases(StandardGeometryContext context)=>BuildLines(context).Select((l,i)=>
    {
        var reservation=new ScreenRect(First-32,l.Y-34,Last+33,l.Y+35);
        StandardBandContour.Validate(l.Path,context.Canvas);
        if(reservation.Left<context.Canvas.Left||reservation.Right>context.Canvas.Right||reservation.Top<context.Canvas.Top||reservation.Bottom>context.Canvas.Bottom)
            throw new InvalidDataException("Horizontal reservation leaves the selected Canvas.");
        var sample=new EvidenceSeriesCase(i,0,"row-axis-characterization",false,false,Last-First,reservation,
            [new("black-band",new(0,0,0),[new(First,l.Y,Last,l.Y)])]){Speed=StandardRepeatSeries.Speed};
        return new StandardTrackPairCli.TrackCase((l.Y-220)/80,0,Id(new{Revision,l}),sample,[l.Path]);
    }).ToArray();
    private static void CheckSettings(Settings settings,StandardGeometryContext context,Line[] lines)
    {
        settings.Validate();
        if(!ReleaseProfile.Standard(settings)||StandardLineMotion.Enabled(settings)||settings.Text("speed_profile")!="Rapid"||StrokeTiming.Frame(settings)!=.02
            ||settings.PaintCalibration().Rect("canvas")!=context.Canvas||settings.Text("color_mode")!=context.ColorMode)
            throw new InvalidDataException("Row pilot settings changed.");
        var guard=InputCommandGuard.From(settings,new(context.ClientOrigin.X,context.ClientOrigin.Y,context.ClientOrigin.X+context.ClientSize.Width,context.ClientOrigin.Y+context.ClientSize.Height));
        if(lines.Any(l=>!guard.Allows(l.Path)))throw new InvalidDataException("Horizontal path crosses a captured control.");
    }
    internal static void Init(string sourceUv,string directory)
    {
        sourceUv=Path.GetFullPath(sourceUv);directory=Path.GetFullPath(directory);StandardUvPilot.Separate(sourceUv,directory);
        var m=StandardUvPilot.Read(sourceUv,true);if(m.SyntheticSource)throw new InvalidDataException("Native source required.");StandardUvPilot.Verify(sourceUv,m);
        Create(directory,Settings.Load(Path.Combine(sourceUv,"settings.json")),m.Context,Path.Combine(sourceUv,"baseline.png"),
            Path.Combine(sourceUv,FrontierSessionCapture.FrameName),m.ProtocolId);
    }
    internal static Manifest Create(string directory,Settings original,StandardGeometryContext context,string baseline,string geometry,string sourceId,bool synthetic=false)
    {
        directory=Path.GetFullPath(directory);StandardUvPilot.Separate(Path.GetDirectoryName(Path.GetFullPath(baseline))!,directory);
        if(!synthetic)StandardUvPilot.Separate(Path.GetDirectoryName(Path.GetFullPath(geometry))!,directory);
        var lines=BuildLines(context);var cases=BuildCases(context);var settings=original.Clone();CheckSettings(settings,context,lines);
        if(!Digest(sourceId))throw new InvalidDataException("Source protocol identity missing.");
        StandardStraightPilotCli.Frame(baseline,context.Canvas,synthetic);if(!synthetic)StandardTexelView.ReadFrame(geometry);
        Directory.CreateDirectory(directory);var refs=new Dictionary<string,string>(StringComparer.Ordinal);
        void Copy(string from,string to){string target=Path.Combine(directory,to);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(from,target);refs.Add(to,Hash(target));}
        Copy(baseline,"reference/baseline.png");Copy(Path.ChangeExtension(baseline,"json"),"reference/baseline.json");
        if(!synthetic){Copy(geometry,"reference/geometry.png");Copy(Path.ChangeExtension(geometry,"json"),"reference/geometry.json");}
        File.WriteAllText(Path.Combine(directory,"settings.json"),settings.Data.ToJsonString());
        var m=new Manifest(Revision,Policy,StandardTexelView.Policy,BaselinePolicy,context,lines,cases,refs,Hash(Path.Combine(directory,"settings.json")),
            Hash(typeof(Painter).Assembly.Location),Hash(typeof(Settings).Assembly.Location),sourceId,Guid.NewGuid().ToString("N"),synthetic,"");
        m=m with{ProtocolId=Id(m)};StandardUvPilot.WriteNew(Path.Combine(directory,"protocol.json"),m);
        File.WriteAllText(Path.Combine(directory,"protocol.json.sha256"),Hash(Path.Combine(directory,"protocol.json")));
        StandardUvPilot.WriteNew(Path.Combine(directory,"series-status.json"),new{status="NOT_STARTED",requiredCases=12,requiredStrokes=12,
            nativeInput=false,productionEligible=false,physicalQualityCertified=false,automaticRetry=false,automaticResume=false});
        Read(directory,true);return m;
    }
    internal static Manifest Read(string directory,bool offline=false)
    {
        if(!offline&&(new[]{"attempted.json","started.json","events.jsonl","baseline.png","final.png",FrontierSessionCapture.FileName,"geometry-check.json","startup-failures.jsonl"}.Any(n=>File.Exists(Path.Combine(directory,n)))
            ||Directory.GetDirectories(directory,"sample-*").Length>0))throw new IOException("This attempt has retained data; retry/resume disabled.");
        string path=Path.Combine(directory,"protocol.json");if(Hash(path)!=File.ReadAllText(path+".sha256").Trim())throw new InvalidDataException("Row manifest changed.");
        var m=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path))??throw new InvalidDataException("Missing row manifest.");
        string[] expected=m.SyntheticSource?["reference/baseline.png","reference/baseline.json"]:["reference/baseline.png","reference/baseline.json","reference/geometry.png","reference/geometry.json"];
        if(m.Revision!=Revision||m.Policy!=Policy||m.ViewPolicy!=StandardTexelView.Policy||m.BaselinePolicy!=BaselinePolicy||m.ProtocolId!=Id(m with{ProtocolId=""})||!Guid.TryParseExact(m.CollectionId,"N",out _)
            ||!Digest(m.SourceUvProtocolId)||!Digest(m.AppSha256)||!Digest(m.CoreSha256)||m.SettingsSha256!=Hash(Path.Combine(directory,"settings.json"))
            ||JsonSerializer.Serialize(m.Lines)!=JsonSerializer.Serialize(BuildLines(m.Context))||JsonSerializer.Serialize(m.Cases)!=JsonSerializer.Serialize(BuildCases(m.Context))
            ||m.ReferenceFiles.Count!=expected.Length||m.ReferenceFiles.Keys.Any(k=>!expected.Contains(k,StringComparer.Ordinal))
            ||!offline&&(m.SyntheticSource||m.AppSha256!=Hash(typeof(Painter).Assembly.Location)||m.CoreSha256!=Hash(typeof(Settings).Assembly.Location)))
            throw new InvalidDataException("Row policy, paths, split, settings, references or binaries changed.");
        foreach(var (name,h) in m.ReferenceFiles)if(Hash(Path.Combine(directory,name))!=h)throw new InvalidDataException("Row reference changed.");
        CheckSettings(Settings.Load(Path.Combine(directory,"settings.json")),m.Context,m.Lines);
        StandardStraightPilotCli.Frame(Path.Combine(directory,"reference/baseline.png"),m.Context.Canvas,m.SyntheticSource);return m;
    }
    internal static StandardTrackPairCli.Manifest View(Manifest m)=>new(StandardTrackPairCli.Revision,"row-axis",m.ProtocolId,m.Context,m.Cases,m.ProtocolId,"",
        m.ReferenceFiles,m.SettingsSha256,m.AppSha256,m.CoreSha256,m.SyntheticSource,StandardTrackPairCli.CapturePolicy);
    internal static FrontierSessionCapture.Binding Binding(Manifest m)=>new(m.ProtocolId,m.SettingsSha256,m.AppSha256,m.CoreSha256,m.Context,m.SyntheticSource);
    internal static void RequireFreshBaseline(PixelImage explicitlyConfirmed,PixelImage actual,ScreenRect canvas)
    {
        // The reference comes ONLY from this explicit preparation's immutable
        // FrontierSessionCapture.Binding, never from a runtime mismatch. The
        // archive witnesses view geometry; its old RGB is not today's blank.
        // Keep full-Canvas RGB12, including pixels outside stroke reservations.
        FrontierSessionCapture.RequireBaseline(explicitlyConfirmed,actual,canvas);
    }
    internal static void Verify(string directory,Manifest? manifest=null,bool requireCompleteStatus=true)
    {
        var m=manifest??Read(directory,true);StandardTrackPairCli.ValidateCompleted(directory,View(m),requireCompleteStatus);
        var first=StandardStraightPilotCli.Frame(Path.Combine(directory,"baseline.png"),m.Context.Canvas,m.SyntheticSource);
        if(!m.SyntheticSource)
        {
            FrontierSessionCapture.VerifyUse(directory,directory,Binding(m));StandardTexelView.Check(directory,false);
            RequireFreshBaseline(FrontierSessionCapture.Baseline(directory,Binding(m)),first,m.Context.Canvas);
        }
    }
    internal static void Run(string directory)
    {
        var m=Read(directory);StandardEvidenceSeriesRunner.Result? result=null;
        StandardEvidenceSeriesCli.RunCollection(directory,m.Context.Canvas,12,15,p=>result=p.CollectRowPilot(m,directory),
            (settings,window)=>{var prepared=FrontierSessionCapture.CaptureFresh(settings,window,Binding(m),directory);StandardTexelView.Check(directory,true);return prepared;},showDialog:false);
        if(result?.Status!="DATA_COLLECTED")Environment.ExitCode=1;else Verify(directory,m);
    }
    internal static Result AnalyzeTraining(Line[] lines,IReadOnlyDictionary<string,StandardEdgeSection[]> source,IEnumerable<string>? failures=null)
    {
        if(lines.Length!=12||!lines.Select(l=>l.Y).Order().SequenceEqual(Enumerable.Range(0,12).Select(i=>220+80*i))||lines.Count(l=>l.Role=="training")!=8
            ||lines.Any(l=>l.Role!=((l.Y-220)/80%3==1?"held-out":"training"))||lines.Select(l=>l.Label).Distinct().Count()!=12)
            throw new InvalidDataException("Frozen row denominator or train/held-out split changed.");
        var train=lines.Where(l=>l.Role=="training").ToArray();var reasons=failures?.ToList()??[];
        if(source.Keys.Any(k=>!train.Any(l=>l.Label==k)))throw new InvalidDataException("Training analysis cannot consume held-out or unknown measurements.");
        foreach(var l in train)if(!source.TryGetValue(l.Label,out var rows)||!rows.Select(r=>r.Along).SequenceEqual(Enumerable.Range(BodyFirst,BodyLast-BodyFirst+1))
            ||rows.Any(r=>!double.IsFinite(r.Low)||!double.IsFinite(r.High)||r.Low>=r.High))reasons.Add(l.Label+": mandatory body sections missing or invalid");
        if(reasons.Count>0)return new("INCOMPLETE",reasons.ToArray(),train.Select(l=>
        {int n=(source.GetValueOrDefault(l.Label)??[]).Where(r=>r.Along>=BodyFirst&&r.Along<=BodyLast&&double.IsFinite(r.Low)&&double.IsFinite(r.High)&&r.Low<r.High).Select(r=>r.Along).Distinct().Count();return new MeasuredLine(l.Label,n,BodyLast-BodyFirst+1-n,null,null,null,null);}).ToArray());
        return new("MEASURED_NOT_QUALIFIED",[],train.Select(l=>
        {
            var rows=source[l.Label];Edge Measure(bool top){var events=StandardTexelAnalysis.Detect(rows,top);return new(rows.Min(r=>top?r.Low:r.High),rows.Max(r=>top?r.Low:r.High),events.Sum(e=>e.Delta),events);}
            var widths=rows.Select(r=>r.High-r.Low).ToArray();return new MeasuredLine(l.Label,rows.Length,0,Measure(true),Measure(false),widths.Average(),widths.Max()-widths.Min());
        }).ToArray());
    }
    internal static Result Review(string directory,string output,bool synthetic=false)
    {
        directory=Path.GetFullPath(directory);output=Path.GetFullPath(output);StandardUvPilot.Separate(directory,output);
        var initial=Directory.GetFiles(directory,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToDictionary(p=>p,Hash,StringComparer.Ordinal);
        var m=Read(directory,true);if(m.SyntheticSource!=synthetic)throw new InvalidDataException("Synthetic/native mismatch.");
        var failures=new List<string>();try{Verify(directory,m);}catch(Exception e) when(e is IOException or InvalidDataException or JsonException or KeyNotFoundException){failures.Add(e.Message);}
        var body=new Dictionary<string,StandardEdgeSection[]>();var csv=new List<string>{"label,along,top,bottom,region,status,reason"};
        if(failures.Count==0)foreach(var (l,i) in m.Lines.Select((l,i)=>(l,i)).Where(p=>p.l.Role=="training"))
        {
            string stem=Path.Combine(directory,$"sample-{i:D2}/black-band/stroke-000");
            var before=StandardStraightPilotCli.Frame(stem+"-before.png",m.Context.Canvas,synthetic);var after=StandardStraightPilotCli.Frame(stem+"-after.png",m.Context.Canvas,synthetic);var rows=new List<StandardEdgeSection>();
            for(int x=First;x<=Last;x++)
            {
                bool mandatory=x>=BodyFirst&&x<=BodyLast;string region=mandatory?"body":x<BodyFirst?"start_cap":"end_cap";
                var row=StandardBandResponse.MeasureSection(before,after,m.Context.Canvas,false,l.Y,x,mandatory);
                if(row.Edges is{} e){if(mandatory)rows.Add(e);csv.Add($"{l.Label},{x},{e.Low.ToString("R",CultureInfo.InvariantCulture)},{e.High.ToString("R",CultureInfo.InvariantCulture)},{region},measured,");}
                else csv.Add($"{l.Label},{x},,,{region},missing,{row.Reason}");
            }
            body.Add(l.Label,rows.ToArray());
        }
        var result=AnalyzeTraining(m.Lines,body,failures);
        if(initial.Count!=Directory.GetFiles(directory,"*",SearchOption.AllDirectories).Length||initial.Any(p=>!File.Exists(p.Key)||Hash(p.Key)!=p.Value))throw new IOException("Evidence changed during row review.");
        Directory.CreateDirectory(output);File.WriteAllLines(Path.Combine(output,"training-sections.csv"),csv);
        StandardUvPilot.WriteNew(Path.Combine(output,"summary.json"),new{Revision,Policy,m.ProtocolId,result,requiredStrokes=12,trainingLines=8,heldOutLines=4,
            mandatoryTrainingBodySections=6648,heldOutMeasurementsOpened=false,heldOutCaptureIntegrityChecked=failures.Count==0,
            observable="native-band-local50-radius32-v1; top/bottom are physical screen-Y; diagnostic only",
            eventDetector="D: adjacent abs>=0.3px, contiguous same sign, abs accumulated>=1.2px; events are screen diagnostics, not texel-quality verdicts",
            requiredBeforeHeldOut="Freeze row-axis model, observable/U policy, coefficients and hashes BEFORE extracting held-out edges; untouched data required after retuning",
            limitations="One block of left-to-right black Size3 strokes; no time stability, inverse correction, texture resolution, colour/caps/overlap or general B qualification",
            sourceFiles=initial.ToDictionary(p=>Path.GetRelativePath(directory,p.Key).Replace('\\','/'),p=>p.Value),sourceUnchanged=true,synthetic,nativeInputByReviewer=false,
            productionEligible=false,physicalQualityCertified=false});return result;
    }
}
