using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

// Read-only native prefix importer. This entry point is handled before WPF,
// SingleInstanceLease and any Painter/native-input lifecycle is constructed.
internal static class StandardBandResponseReview
{
    internal static void Run(string sourceDirectory,string outputDirectory,bool allowSynthetic=false,bool inverse=false)
    {
        string source=Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar),output=Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar);
        static bool Within(string a,string b)=>a.Equals(b,StringComparison.OrdinalIgnoreCase)||a.StartsWith(b+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        if(Within(source,output)||Within(output,source)||Directory.Exists(output)||File.Exists(output))throw new IOException("Use a new band-response output directory outside source evidence.");
        static Dictionary<string,string> Files(string directory)=>Directory.GetFiles(directory,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .ToDictionary(p=>Path.GetRelativePath(directory,p).Replace('\\','/'),StandardBoundaryReview.Hash,StringComparer.Ordinal);
        var original=Files(source);Directory.CreateDirectory(output);var json=new JsonSerializerOptions{WriteIndented=true};
        void Write(string name,object value)=>File.WriteAllText(Path.Combine(output,name),JsonSerializer.Serialize(value,json));
        void Status(string status,string? reason=null)=>Write("response-status.json",new{status,reason,nativeInputByReviewer=false,
            physicalQualityCertified=false,productionEligible=false,dispatchAllowed=false,synthetic=allowSynthetic});
        Status("ANALYZING");
        try
        {
            var manifests=new[]{"control","shifted"}.Select(name=>StandardTrackPairCli.Read(Path.Combine(source,name),offline:true)).ToArray();
            if(manifests[0].Condition!="control"||manifests[1].Condition!="shifted"||manifests.Any(m=>m.SyntheticSource!=allowSynthetic)
                ||manifests[0].PairId!=manifests[1].PairId||manifests[0].AppSha256!=manifests[1].AppSha256||manifests[0].CoreSha256!=manifests[1].CoreSha256
                ||manifests[0].Context.Id!=manifests[1].Context.Id||manifests[0].CapturePolicy!=manifests[1].CapturePolicy)
                throw new InvalidDataException("Complete matching same-build control and shifted conditions are required.");
            foreach(var m in manifests)StandardTrackPairCli.ValidateCompleted(Path.Combine(source,m.Condition),m);
            using var a=JsonDocument.Parse(File.ReadAllText(Path.Combine(source,"control/live-context.json")));
            using var b=JsonDocument.Parse(File.ReadAllText(Path.Combine(source,"shifted/live-context.json")));
            if(a.RootElement.GetProperty("windowProcessId").GetInt32()!=b.RootElement.GetProperty("windowProcessId").GetInt32())
                throw new InvalidDataException("Rust was restarted between measured conditions.");
            var rows=new List<object>();var drafts=new List<object>();var models=new Dictionary<(string,int),StandardBandResponse>();int observed=0;
            int rawRows=0,rawComplete=0,rawMissing=0,rawDisconnected=0,rawClipped=0;
            foreach(var m in manifests)
            {
                var historical=StandardContourPilotCli.Read(Path.Combine(source,"source"),offline:true);
                foreach(var c in m.Cases)
                {
                    // The live-context "speed" is the legacy default. The
                    // per-case speed is what actually drove the traced emitter.
                    var context=new StandardBandResponseContext(m.Context,m.AppSha256,m.CoreSha256,StandardContourMotion.Revision,c.Sample.Speed,m.CapturePolicy);
                    var design=historical.Design.Single(d=>d.SourceSample==c.SourceSample);var target=design.Command;
                    string stage=Path.Combine(source,m.Condition,$"sample-{c.Sample.Index:D2}","black-band");
                    PixelImage Frame(string name)=>StandardStraightPilotCli.Frame(Path.Combine(stage,name),m.Context.Canvas,m.SyntheticSource);
                    var baseline=Frame("before.png");
                    var model=new StandardBandResponse(context,target,c.Sample.Reservation,baseline);
                    var requested=c.Sample.Stages.Single(s=>s.Name=="black-band").Colour;
                    var colorMasks=new List<StandardPaintMaskResult>();
                    for(int i=0;i<c.Paths.Length;i++)
                    {
                        var before=Frame($"stroke-{i:D3}-before.png");var after=Frame($"stroke-{i:D3}-after.png");
                        model.Add($"{m.ProtocolId}/{c.Sample.Index}/{i}",c.Paths[i],before,after);
                        var mask=StandardPaintMask.Measure(baseline,after,m.Context.Canvas,c.Sample.Reservation,target,requested);
                        colorMasks.Add(mask);rawRows+=mask.Rows.Count;rawComplete+=mask.CompleteRows;
                        rawMissing+=mask.MissingRows;rawDisconnected+=mask.DisconnectedRows;rawClipped+=mask.ClippedRows;
                    }
                    models.Add((m.Condition,c.SourceSample),model);
                    var scores=model.States.Select(s=>new{stateId=s.Id,s.PrefixLength,score=s.Score(target,14)}).ToArray();
                    var choice=model.SelectObserved(14);observed+=model.States.Count;
                    rows.Add(new{condition=m.Condition,c.SourceSample,model.Id,model.ImplementationSha256,model.Context,model.Target,model.Reservation,
                        model.States,scores,requestedColorMasks=colorMasks,establishedSolidPixels=0,productionEligible=false});
                    drafts.Add(new{condition=m.Condition,c.SourceSample,selection=StandardBandResponse.SelectionRevision,targetWidth=14,
                        choice,coverageCredit=0,capStatus="observed full Possible preserved; not physically certified",
                        execution="no conversion to PaintBatch; no dispatch API; unknown positions/history unavailable"});
                }
            }
            var final=Files(source);
            if(original.Count!=final.Count||original.Any(p=>!final.TryGetValue(p.Key,out var hash)||hash!=p.Value))
                throw new IOException("Source evidence changed during band-response analysis.");
            Write("source-files.json",original);Write("band-responses.json",rows);Write("observed-route-drafts.json",drafts);
            if(inverse)
            {
                var candidates=new List<object>();
                foreach(int sample in manifests[0].Cases.Select(c=>c.SourceSample))
                {
                    var control=models[("control",sample)];var shifted=models[("shifted",sample)];
                    var candidate=StandardBandInverse.Build(control,shifted,14);
                    candidates.Add(new{sourceSample=sample,status=candidate is null?"UNAVAILABLE_INSUFFICIENT_CALIBRATION":"CORRECTED_PATH_HYPOTHESIS_REQUIRES_PHYSICAL_VALIDATION",
                        candidate,controlScore=control.States[^1].Score(control.Target,14),shiftedScore=shifted.States[^1].Score(shifted.Target,14),
                        caps="observed source caps retained in band-responses.json; corrected caps are not predicted or certified",coverageCredit=0});
                }
                Write("inverse-candidates.json",candidates);
                Write("inverse-policy.json",new{revision=StandardBandInverse.Revision,policy=StandardBandInverse.Policy,
                    targetWidth=14,source="existing observed full prefix at two absolute placements",newCorrectedPathsGenerated=true,
                    assumptions="row-local linear cross interpolation; curvature may change longitudinal response; endpoints/overlap require new physical validation",
                    thresholds="RGB12/contrast80 unchanged; local50% diagnostic edges are not requested-RGB certification",
                    coordinates="physical screen; no global mapping correction; no transform of UI/palette/HEX",
                    independentValidation=false,ordinaryStartChanged=false,dispatchAllowed=false,physicalQualityCertified=false,nativeInputByReviewer=false});
            }
            Write("summary.json",new{revision=StandardBandResponse.Revision,selectionRevision=StandardBandResponse.SelectionRevision,
                pairId=manifests[0].PairId,status="MEASURED_RESPONSE_MODEL_NOT_QUALIFIED",conditions=2,bands=rows.Count,observedStates=observed,
                validatedNativeFrames=allowSynthetic?0:144,validatedTraces=60,sourceFiles=original.Count,originalSourceUnchanged=true,
                diagnosticEdgeObservable="full reservation; mandatory every commanded along coordinate; local 50% edge radius32; meanRGB contrast>80",
                requestedColorMask=new{revision=StandardPaintMask.Revision,expectedRows=rawRows,completeRows=rawComplete,
                    missingRows=rawMissing,disconnectedRows=rawDisconnected,clippedRows=rawClipped,
                    rule="maxRGB(actual,requested)<=12 AND maxRGB(actual,baseline)>80; no inferred replacement RGB; not certified Solid",
                    edgeConvention="half-open screen pixel indices; physical pixel boundaries at index-.5"},
                model="exact absolute commands and ordered observed history; each state uses clean-baseline-to-after, never union of per-track deltas",
                selection="exploratory measured-prefix ranking only: missing body rows, max two-edge error, mean squared two-edge target error, max adjacent edge step, width range, prefix length; no minimum-width percentage gate",
                independentValidation=false,establishedSolidPixels=0,physicalQualityCertified=false,productionEligible=false,dispatchAllowed=false,
                nativeInputByReviewer=false,synthetic=allowSynthetic});
            Status("MEASURED_RESPONSE_MODEL_NOT_QUALIFIED");
        }
        catch(Exception error){Status("INCOMPLETE",error.Message);throw;}
    }
}
