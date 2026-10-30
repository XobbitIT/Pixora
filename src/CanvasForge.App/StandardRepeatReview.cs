using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

// Read-only analysis before WPF/input lease. Held-out image/trace files are
// first opened only after the training artifact and its hash are written.
internal static class StandardRepeatReview
{
    internal static void Run(string sourceDirectory,string outputDirectory,Func<string,ScreenRect,PixelImage>? syntheticReader=null)
    {
        string source=Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar),output=Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar);
        bool Contains(string a,string b)=>string.Equals(a,b,StringComparison.OrdinalIgnoreCase)||b.StartsWith(a+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        if(Contains(source,output)||Contains(output,source)||Directory.Exists(output)||File.Exists(output))throw new IOException("Use a new output directory outside source evidence.");
        var manifest=StandardRepeatSeriesCli.ReadSeries(source,offline:true);var protocol=manifest.Protocol;var canvas=protocol.Canvas;
        using var state=JsonDocument.Parse(File.ReadAllText(Path.Combine(source,"series-status.json")));
        if(!state.RootElement.TryGetProperty("result",out var result)||result.GetProperty("Status").GetString()!="DATA_COLLECTED"
            ||result.GetProperty("CompletedCases").GetInt32()!=StandardRepeatSeries.RequiredCases
            ||result.GetProperty("CompletedStages").GetInt32()!=StandardRepeatSeries.RequiredStages
            ||result.GetProperty("CompletedStrokes").GetInt32()!=StandardRepeatSeries.RequiredStrokes)
            throw new InvalidDataException("Repeat collection is incomplete; mandatory cases cannot be omitted.");
        using var live=JsonDocument.Parse(File.ReadAllText(Path.Combine(source,"live-context.json")));
        using var started=JsonDocument.Parse(File.ReadAllText(Path.Combine(source,"started.json")));
        if(started.RootElement.GetProperty("revision").GetString()!=StandardRepeatSeries.Revision
            ||started.RootElement.GetProperty("protocolId").GetString()!=manifest.ProtocolId||live.RootElement.GetProperty("protocolId").GetString()!=manifest.ProtocolId)
            throw new InvalidDataException("Run identity differs from frozen protocol.");
        var settings=Settings.Load(Path.Combine(source,"settings.json"));
        if(settings.Calibration.Rect("canvas")!=canvas||!ReleaseProfile.Standard(settings)||StandardLineMotion.Enabled(settings)
            ||StrokeTiming.Frame(settings)!=.020||settings.Text("speed_profile")!="Rapid")throw new InvalidDataException("Repeat settings differ from collection policy.");
        var context=StandardBoundaryReview.Context(live.RootElement,settings,StandardRepeatSeries.Speed);
        if(context.Size!=3||context.ShapeSlot!=3)throw new InvalidDataException("Repeat context must use round Size3.");
        string namespaceId=StandardBoundaryReview.Hash(Path.Combine(source,"started.json"));
        string Id(EvidenceSeriesCase c)=>$"{namespaceId}/sample-{c.Index:D2}";
        Directory.CreateDirectory(output);int frames=0;var traces=new List<StandardBoundaryReview.TraceResult>();var measurements=new List<StandardRepeatMeasurement>();
        void Write(string file,object value)=>File.WriteAllText(Path.Combine(output,file),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));
        void Status(string status,string? reason=null)=>Write("review-status.json",new{status,reason,synthetic=syntheticReader is not null,nativeInput=false,
            productionEligible=false,physicalQualityCertified=false,dispatchAllowed=false});
        PixelImage Read(string path){frames++;return syntheticReader is null?StandardBoundaryReview.ReadNative(path,canvas):syntheticReader(path,canvas);}
        Status("ANALYZING");
        try
        {
            var baseline=Read(Path.Combine(source,"baseline.png"));var confirmation=Read(Path.Combine(source,"baseline-confirm.png"));
            if(!EvidenceRegionCheck.Same(baseline,confirmation,canvas,canvas))throw new InvalidDataException("Baseline noise exceeds RGB12.");
            PixelImage[] ReadCase(EvidenceSeriesCase c)
            {
                string folder=Path.Combine(source,$"sample-{c.Index:D2}");
                var recorded=JsonSerializer.Deserialize<EvidenceSeriesCase>(File.ReadAllText(Path.Combine(folder,"sample.json")));
                if(JsonSerializer.Serialize(recorded)!=JsonSerializer.Serialize(c))throw new InvalidDataException("Recorded sample differs from frozen schedule.");
                var initial=Read(Path.Combine(folder,"before.png"));
                if(!EvidenceRegionCheck.Same(baseline,initial,canvas,c.Reservation))throw new InvalidDataException("Sample position was not clean.");
                var images=new List<PixelImage>{initial};var current=initial;
                foreach(var stage in c.Stages)
                {
                    string stageFolder=Path.Combine(folder,stage.Name);var before=Read(Path.Combine(stageFolder,"before.png"));
                    var after=Read(Path.Combine(stageFolder,"after.png"));
                    if(!EvidenceRegionCheck.Same(current,before,canvas,canvas)||!EvidenceRegionCheck.ChangesContained(before,after,canvas,c.Reservation))
                        throw new InvalidDataException("Colour-selection or outside-reservation changes detected.");
                    for(int i=0;i<stage.Lines.Length;i++)traces.Add(StandardBoundaryReview.ReadTrace(Path.Combine(stageFolder,$"stroke-{i:D3}.json.gz"),c,stage,i,nativeRequired:syntheticReader is null));
                    images.Add(after);current=after;
                }
                if(!Images.LoadNative(Path.Combine(folder,"after.png")).Rgba.SequenceEqual(current.Rgba))throw new InvalidDataException("Sample final image differs from last stage.");
                return images.ToArray();
            }
            StandardRepeatMeasurement Measure(EvidenceSeriesCase c)
            {
                var images=ReadCase(c);var line=c.Stages[0].Lines[0];if(c.Reverse)line=TransferSchedule.Reverse(line);
                var measured=StandardRepeatMeasurement.Measure(Id(c),context,line,c.Reservation,images[0],images[1],images[2],images[3]);
                measurements.Add(measured);return measured;
            }
            var training=protocol.Cases.Take(StandardRepeatSeries.TrainingCases).Select(c=>(c,measurement:Measure(c))).ToArray();
            var frozen=training.GroupBy(x=>StandardRepeatSeries.Group(x.c)).Select(g=>StandardKernelCandidate.Freeze(g.Select(x=>x.measurement.Candidate).ToArray(),
                protocol.Cases.Where(c=>StandardRepeatSeries.Role(c)=="heldout"&&StandardRepeatSeries.Group(c)==g.Key).Select(Id).ToArray())).ToArray();
            Write("frozen-training.json",new{policy=StandardRepeatSeries.MeasurementPolicy,implementationSha256=GeometryHashForReview(),protocolId=manifest.ProtocolId,
                trainingIds=training.Select(x=>Id(x.c)),heldoutIds=protocol.Cases.Where(c=>StandardRepeatSeries.Role(c)=="heldout").Select(Id),frozen});
            string frozenHash=StandardBoundaryReview.Hash(Path.Combine(output,"frozen-training.json"));
            Write("freeze-receipt.json",new{frozenHash,heldoutImageOrTraceFilesOpened=0,policy="training frozen before first heldout image/trace open; no post-validation repair",productionEligible=false});
            var validation=new List<object>();
            foreach(var c in protocol.Cases.Where(c=>StandardRepeatSeries.Role(c)=="heldout"))
            {
                var measured=Measure(c);var candidate=frozen.Single(k=>k.Vertical==c.Vertical&&k.Reverse==c.Reverse&&k.Length==c.Length);
                var verdict=candidate.Validate(candidate.MandatoryHeldoutIds,[measured.Candidate]);
                validation.Add(new{sample=c.Index,group=StandardRepeatSeries.Group(c),candidate.Id,verdict});
            }
            var layers=new List<object>();
            foreach(var c in protocol.Cases.Where(c=>StandardRepeatSeries.Role(c)=="neighbour-colour"))
            {
                var images=ReadCase(c);var masks=Enumerable.Range(1,2).Select(stage=>(Color:c.Stages[stage-1].Colour.Key,Possible:(IReadOnlyList<BrushSpan>)Difference(images[stage-1],images[stage]))).ToArray();
                var replay=StandardBoundaryLayers.Replay(canvas.Width,canvas.Height,masks);int firstRgb=0,retainedRgb=0;
                for(int y=c.Reservation.Top;y<c.Reservation.Bottom;y++)for(int x=c.Reservation.Left;x<c.Reservation.Right;x++)
                {
                    int i=(y-canvas.Top)*canvas.Width+x-canvas.Left;
                    if(RustSlider.Delta(images[0].Color(i),images[1].Color(i))>80&&RustSlider.Delta(images[1].Color(i),new(0,0,0))<=12)
                    {firstRgb++;if(RustSlider.Delta(images[2].Color(i),new(0,0,0))<=12)retainedRgb++;}
                }
                layers.Add(new{sample=c.Index,c.Vertical,condition=c.Stages[1].Name,replay.Layers,firstRequestedBlackPixels=firstRgb,retainedRequestedBlackPixels=retainedRgb,
                    lostRequestedBlackPixels=firstRgb-retainedRgb,productionEligible=false});
            }
            var final=Read(Path.Combine(source,"final.png"));
            foreach(var c in protocol.Cases)
            {
                var last=Images.LoadNative(Path.Combine(source,$"sample-{c.Index:D2}","after.png"));
                if(!EvidenceRegionCheck.Same(last,final,canvas,c.Reservation))throw new InvalidDataException("A completed reservation changed later in the series.");
            }
            if(StandardBoundaryReview.Hash(Path.Combine(output,"frozen-training.json"))!=frozenHash)throw new InvalidDataException("Frozen training artifact was modified during validation.");
            Write("repeat-observations.json",measurements);Write("heldout-verdicts.json",validation);Write("neighbour-colours.json",layers);
            Write("summary.json",new{revision=StandardRepeatSeries.Revision,sourceBuild=live.RootElement.GetProperty("build").GetString(),analyzerBuild=BuildInfo.Full,
                protocolId=manifest.ProtocolId,sourceAppSha256=manifest.AppSha256,sourceCoreSha256=manifest.CoreSha256,
                frames,traces=traces.Count,cursorReadings=traces.Sum(t=>t.Readings),cursorMismatches=traces.Sum(t=>t.Mismatches),issuedPoints=traces.Sum(t=>t.Points),packets=traces.Sum(t=>t.Packets),
                trainingCases=24,heldoutCases=8,neighbourColourCases=4,candidates=frozen.Length,candidateSolidPixels=frozen.Sum(k=>k.Solid.Sum(s=>s.Right-s.Left)),
                frozenHash,synthetic=syntheticReader is not null,nativeInput=false,productionEligible=false,physicalQualityCertified=false,
                limits="candidate-only RGB12/contrast80/repeat intersection; one heldout per group; no B0 noise/mapping, no independent repeat reset, no production route/caps/overlap qualification"});
            Status(syntheticReader is null?"DATA_ANALYZED_NOT_QUALIFIED":"SYNTHETIC_PIPELINE_VERIFIED");
        }
        catch(Exception error){Status("INCOMPLETE",error.Message);throw;}
    }
    private static string GeometryHashForReview()=>StandardBoundaryReview.Hash(typeof(Settings).Assembly.Location);
    private static BrushSpan[] Difference(PixelImage a,PixelImage b)
    {
        var points=new List<ScreenPoint>();for(int y=0;y<a.Height;y++)for(int x=0;x<a.Width;x++)
            if(RustSlider.Delta(a.Color(y*a.Width+x),b.Color(y*b.Width+x))>12)points.Add(new(x,y));
        return BrushFootprints.Spans(points);
    }
}
