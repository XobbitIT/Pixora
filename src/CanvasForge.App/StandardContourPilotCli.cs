using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

// This explicit experiment is the only live caller of the new contour emitter.
// It never imports a profile into ordinary painting or claims qualification.
internal static class StandardContourPilotCli
{
    internal const string Revision="standard-contour-paired-pilot-v1";
    internal sealed record DesignCase(int SourceSample,ScreenLine Command,string EvidenceId,StandardEdgeSection[] Edges);
    internal sealed record PilotCase(int SourceSample,string ModelId,EvidenceSeriesCase Sample,ScreenPoint[][] Paths);
    internal sealed record Manifest(string Revision,string Condition,StandardGeometryContext Context,DesignCase[] Design,PilotCase[] Cases,
        string ProtocolId,string SettingsSha256,string AppSha256,string CoreSha256,bool SyntheticSource);
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    private static string Hash(string path)=>StandardBoundaryReview.Hash(path);
    private static string Identity(string condition,StandardGeometryContext context,DesignCase[] design,PilotCase[] cases)
        =>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{Revision,condition,context,design,cases}))).ToLowerInvariant();

    internal static void Init(string source,string directory,Func<string,ScreenRect,PixelImage>? syntheticReader=null)
    {
        source=Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);directory=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        bool Contains(string a,string b)=>a.Equals(b,StringComparison.OrdinalIgnoreCase)||b.StartsWith(a+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        if(Contains(source,directory)||Contains(directory,source)||Directory.Exists(directory)||File.Exists(directory))
            throw new IOException("Use a new pilot parent outside source evidence.");
        // Validate the original source before consuming any inverse candidate.
        Directory.CreateDirectory(directory);
        try
        {
            string review=Path.Combine(directory,"source-review");StandardEdgeCorrectionReview.Run(source,review,syntheticReader);
            var original=StandardRepeatSeriesCli.ReadSeries(source,offline:true);
            var settings=Settings.Load(Path.Combine(source,"settings.json"));settings.Set("standard_native_line",false);
            using var live=JsonDocument.Parse(File.ReadAllText(Path.Combine(source,"live-context.json")));
            var context=StandardBoundaryReview.Context(live.RootElement,settings,StandardRepeatSeries.Speed);
            using var candidates=JsonDocument.Parse(File.ReadAllText(Path.Combine(review,"edge-correction-candidates.json")));
            // Three separated V lanes include two observed failures and a low-
            // residual control; two H controls. All positions were reviewed.
            int[] chosen=[6,13,31,19,12];var design=new List<DesignCase>();
            foreach(int index in chosen)
            {
                var sample=original.Protocol.Cases[index];var item=candidates.RootElement.EnumerateArray().Single(c=>c.GetProperty("sample").GetInt32()==index);
                if(!item.TryGetProperty("candidate",out var candidate))throw new InvalidDataException("Mandatory pilot source edges are unavailable.");
                var edges=candidate.GetProperty("Sections").Deserialize<StandardEdgeSection[]>()!;
                var line=sample.Stages[0].Lines[0];if(sample.Reverse)line=TransferSchedule.Reverse(line);
                design.Add(new(index,line,Hash(Path.Combine(source,$"sample-{index:D2}","pass-1","after.png")),edges));
            }
            Create(directory,settings,context,design.ToArray(),syntheticReader is not null);
        }
        catch(Exception error)
        {
            File.WriteAllText(Path.Combine(directory,"init-failed.json"),JsonSerializer.Serialize(new{status="INCOMPLETE",reason=error.Message,nativeInput=false}));throw;
        }
    }
    internal static void Create(string parent,Settings settings,StandardGeometryContext context,DesignCase[] design,bool synthetic=false)
    {
        context.Validate();if(design.Length!=5||design.Select(c=>c.SourceSample).Distinct().Count()!=5)throw new InvalidDataException("Pilot requires all five fixed source cases.");
        settings=settings.Clone();ReleaseProfile.ApplyStandard(settings);settings.Set("standard_native_line",false);settings.Set("input_frame_delay_ms",20);settings.Set("speed_profile","Rapid");
        if(settings.Calibration.Rect("canvas")!=context.Canvas||settings.Text("color_mode")!=context.ColorMode)throw new InvalidDataException("Pilot source context differs from settings.");
        foreach(string condition in new[]{"control","candidate"})
        {
            var cases=BuildCases(context,design,condition);string directory=Path.Combine(parent,condition);
            if(Directory.Exists(directory)||File.Exists(directory))throw new IOException("Pilot condition already exists.");Directory.CreateDirectory(directory);
            string config=Path.Combine(directory,"settings.json");File.WriteAllText(config,settings.Data.ToJsonString());
            var m=new Manifest(Revision,condition,context,design,cases,Identity(condition,context,design,cases),Hash(config),Hash(typeof(Painter).Assembly.Location),Hash(typeof(Settings).Assembly.Location),synthetic);
            string path=Path.Combine(directory,"protocol.json");File.WriteAllText(path,JsonSerializer.Serialize(m,Json));File.WriteAllText(path+".sha256",Hash(path));
            File.WriteAllText(Path.Combine(directory,"series-status.json"),JsonSerializer.Serialize(new{status="NOT_STARTED",requiredCases=5,requiredStages=5,requiredStrokes=50,
                nativeInput=false,syntheticSource=synthetic,physicalQualityCertified=false,experimental=true,automaticRetry=false,automaticResume=false}));
        }
        File.WriteAllText(Path.Combine(parent,"pair-policy.json"),JsonSerializer.Serialize(new{revision=Revision,conditions=new[]{"control","candidate"},sourceContext=context.Id,
            order="control then candidate on independently cleared Canvas, same saved geometry; one attempt each",sourceSamples=design.Select(d=>d.SourceSample),
            casesPerCondition=5,strokesPerCase=10,targetBandWidthPhysicalPx=14,
            capPolicy="original control offsets retained near endpoints; unvalidated command ramps; evaluate full native footprint plus fixed central window",
            verdictPolicy="prospective corrected-path comparison only; no automatic model promotion; no B0 or general qualification; missing mandatory data => INCOMPLETE",
            observable="meanRGB native before/after; +/-32px cross window; unique contrast>80 component; before background median at +/-28..32; local45/50/55% edges",
            measurementWindow="fixed20..80% command length; every section mandatory; max/P95 absolute two-edge target error and width range; full caps Possible delta>12 recorded separately",
            pairedBaseline="max RGB delta<=12 in same reserved area; mismatch leaves physical comparison INCOMPLETE",
            nativeInput=false,syntheticSource=synthetic,physicalQualityCertified=false},Json));
        File.WriteAllText(Path.Combine(parent,"Scenario_UA.md"),"""
            # Порівняння реального коректора країв

            Це експериментальний емітер, не виправлення, автоматично ввімкнене у Standard.
            Джерело — вже записані виміри вашого Canvas. Геометрія, DPI, вікно та режим кольору мають збігатися.
            Обидва прогони малюють ті самі п'ять смуг по 10 проходів при Max Speed. Змінюються лише траєкторії.

            1. Підготуйте повністю чисте полотно. Закрийте Pixora. Збережіть Interval 0.01 і Opacity 1.00 у Rust.
            2. Запустіть `Pixora.exe --standard-contour-pilot run PATH_TO_CONTROL`. За 10 секунд поверніться до відкритого редактора Rust.
            3. Після DATA_COLLECTED очистіть Canvas повністю. Не змінюйте вікно, zoom, Canvas або розмір пензля.
            4. Запустіть ту саму команду з PATH_TO_CANDIDATE. Автоматично запишуться кадри до/після смуг, фінальний кадр і команди/реальні читання курсора.
            5. Збережіть обидві папки, включно з INCOMPLETE у разі зупинки. Повторення/відновлення того самого каталогу заборонене.

            Caps біля кінців ще не коригуються; їхня зміна перевірятиметься окремо за повними знімками.
            Порівнюються якість і товщина фактичного сліду. Це не сценарій загальної перевірки F6/Alt+Tab.
            """);
    }
    internal static PilotCase[] BuildCases(StandardGeometryContext context,DesignCase[] design,string condition)
    {
        if(condition is not ("control" or "candidate"))throw new InvalidDataException("Unknown pilot condition.");
        var output=new List<PilotCase>();
        foreach(var d in design)
        {
            var model=StandardBandContour.Create(context,d.Command,d.EvidenceId,d.Edges);bool reverse=d.Command.X2<d.Command.X1||d.Command.Y2<d.Command.Y1;
            var paths=(condition=="control"?model.Control:model.Candidate).Select(p=>p.Points.ToArray()).ToArray();
            var lines=model.Control.Select(t=>{var a=t.Points[0];var b=t.Points[^1];return reverse?new ScreenLine(b.X,b.Y,a.X,a.Y):new(a.X,a.Y,b.X,b.Y);}).ToArray();
            // Same reservation in both conditions, including commanded ramps,
            // caps and a 16px observation margin. Never clip to fit the Canvas.
            var all=model.Control.Concat(model.Candidate).SelectMany(t=>t.Points).ToArray();
            var reservation=new ScreenRect(all.Min(p=>p.X)-16,all.Min(p=>p.Y)-16,all.Max(p=>p.X)+17,all.Max(p=>p.Y)+17);
            var canvas=context.Canvas;
            if(reservation.Left<canvas.Left||reservation.Top<canvas.Top||reservation.Right>canvas.Right||reservation.Bottom>canvas.Bottom
                ||output.Any(c=>reservation.Left<c.Sample.Reservation.Right&&reservation.Right>c.Sample.Reservation.Left&&reservation.Top<c.Sample.Reservation.Bottom&&reservation.Bottom>c.Sample.Reservation.Top))
                throw new InvalidDataException("Pilot reservations overlap or exceed the captured Canvas.");
            var sample=new EvidenceSeriesCase(output.Count,0,"contour-pilot",d.Command.X1==d.Command.X2,reverse,TransferSchedule.Length(d.Command),reservation,
                [new("black-band",new(0,0,0),lines)]){Speed=StandardRepeatSeries.Speed};
            output.Add(new(d.SourceSample,model.Id,sample,paths));
        }
        return output.ToArray();
    }
    internal static Manifest Read(string directory,bool offline=false)
    {
        if(!offline&&(File.Exists(Path.Combine(directory,"attempted.json"))||File.Exists(Path.Combine(directory,"started.json"))))throw new IOException("Pilot was already attempted; no automatic retry/resume.");
        string path=Path.Combine(directory,"protocol.json");if(Hash(path)!=File.ReadAllText(path+".sha256").Trim())throw new InvalidDataException("Pilot protocol changed.");
        var m=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path))??throw new InvalidDataException("Missing pilot manifest.");
        var expected=BuildCases(m.Context,m.Design,m.Condition);
        if(m.Revision!=Revision||m.Cases.Length!=5||m.Design.Length!=5||!offline&&m.SyntheticSource||JsonSerializer.Serialize(expected)!=JsonSerializer.Serialize(m.Cases)
            ||m.ProtocolId!=Identity(m.Condition,m.Context,m.Design,m.Cases)||m.SettingsSha256!=Hash(Path.Combine(directory,"settings.json"))
            ||!offline&&(m.AppSha256!=Hash(typeof(Painter).Assembly.Location)||m.CoreSha256!=Hash(typeof(Settings).Assembly.Location)))throw new InvalidDataException("Frozen pilot context or actual path changed.");
        if(!offline&&m.Condition=="candidate")
        {
            string control=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(directory))!,"control");var original=Read(control,offline:true);
            using var state=JsonDocument.Parse(File.ReadAllText(Path.Combine(control,"series-status.json")));
            var result=state.RootElement.GetProperty("result");
            if(original.Condition!="control"||original.Context.Id!=m.Context.Id||original.SettingsSha256!=m.SettingsSha256||original.AppSha256!=m.AppSha256||original.CoreSha256!=m.CoreSha256
                ||JsonSerializer.Serialize(original.Design)!=JsonSerializer.Serialize(m.Design)||result.GetProperty("Status").GetString()!="DATA_COLLECTED"
                ||result.GetProperty("CompletedCases").GetInt32()!=5||result.GetProperty("CompletedStrokes").GetInt32()!=50||result.GetProperty("CompletedStages").GetInt32()!=5)
                throw new InvalidDataException("A complete matching control condition is required before the candidate.");
        }
        return m;
    }
    internal static void ValidateCleanBaseline(Manifest manifest,PixelImage control,PixelImage current)
    {
        if(manifest.Cases.Any(c=>!EvidenceRegionCheck.Same(control,current,manifest.Context.Canvas,c.Sample.Reservation)))
            throw new InvalidDataException("candidate_baseline_mismatch: очисти полотно; геометрія та освітлення мають збігатися з контрольним прогоном.");
    }
    internal static void Run(string directory)
    {
        // Native comparison rejected this variable-width/ramped route. Keep
        // its reader/builders for reproducible history, never paint it again.
        throw new InvalidOperationException("Попередній коректор створював нерівності й вимкнений. Використай новий запуск прямих смуг.");
    }
}
