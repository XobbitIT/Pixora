using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

// Offline only: no Native, Painter, Settings.Save, checkpoint or input lease.
internal static class StandardBoundaryReview
{
    internal static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    internal static PixelImage ReadNative(string path,ScreenRect canvas)
    {
        using var document=JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(path,"json")));
        var m=document.RootElement;var image=Images.LoadNative(path);
        if(m.GetProperty("canvas").Deserialize<ScreenRect>()!=canvas||image.Width!=canvas.Width||image.Height!=canvas.Height
            ||!m.GetProperty("nativeInput").GetBoolean()||m.GetProperty("synthetic").GetBoolean()
            ||!string.Equals(Convert.ToHexString(SHA256.HashData(image.Rgba)),m.GetProperty("rgbaSha256").GetString(),StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Native evidence hash, geometry or provenance differs.");
        return image;
    }
    private static void Write(string path,object value)=>File.WriteAllText(path,JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));
    internal static StandardGeometryContext Context(JsonElement live,Settings settings,SpeedProfile speed)
    {
        var result=new StandardGeometryContext(settings.Calibration.Rect("canvas"),live.GetProperty("origin").Deserialize<ScreenPoint>(),
            live.GetProperty("client").Deserialize<ScreenSize>(),live.GetProperty("windowDpi").GetInt32(),live.GetProperty("shape").GetInt32(),
            live.GetProperty("size").GetDouble(),speed,StrokeTiming.Frame(settings),StrokeTiming.EndHold(settings,speed),StrokeTiming.Release(settings),settings.Text("color_mode"),
            live.GetProperty("emitter").GetString()!,live.GetProperty("mapping").GetString()=="identity"?"identity-v1":"unsupported");
        result.Validate();return result;
    }
    private static BrushSpan[] Difference(PixelImage before,PixelImage after)
    {
        var points=new List<ScreenPoint>();
        for(int y=0;y<before.Height;y++)for(int x=0;x<before.Width;x++)
            if(RustSlider.Delta(before.Color(y*before.Width+x),after.Color(y*before.Width+x))>12)points.Add(new(x,y));
        return BrushFootprints.Spans(points);
    }
    internal sealed record TraceResult(int Readings,int Mismatches,int Points,int Packets);
    internal static TraceResult ReadTrace(string path,EvidenceSeriesCase sample,EvidenceColourStage stage,int lineIndex,bool nativeRequired=true)
    {
        using var file=File.OpenRead(path);using var zip=new GZipStream(file,CompressionMode.Decompress);using var document=JsonDocument.Parse(zip);
        var root=document.RootElement;var line=stage.Lines[lineIndex];bool reverse=sample.Kind=="colour-overdraw"?lineIndex%2==1:sample.Reverse;
        if(root.GetProperty("sample").GetInt32()!=sample.Index||root.GetProperty("line").Deserialize<ScreenLine>()!=line
            ||root.GetProperty("reverse").GetBoolean()!=reverse||root.GetProperty("requested").GetString()!=stage.Colour.Hex
            ||root.GetProperty("problem").ValueKind!=JsonValueKind.Null||root.GetProperty("pauseReason").ValueKind!=JsonValueKind.Null
            ||root.GetProperty("speed").Deserialize<SpeedProfile>()!=sample.Speed||root.GetProperty("nativeInput").GetBoolean()!=nativeRequired
            ||root.GetProperty("emitter").GetString()!=StandardStrokeMotion.DenseRevision||root.GetProperty("qpcFrequency").GetInt64()<=0)
            throw new InvalidDataException("Trace context or result differs from frozen protocol.");
        var a=new ScreenPoint(reverse?line.X2:line.X1,reverse?line.Y2:line.Y1);var b=new ScreenPoint(reverse?line.X1:line.X2,reverse?line.Y1:line.Y2);
        int length=TransferSchedule.Length(line),dx=Math.Sign(b.X-a.X),dy=Math.Sign(b.Y-a.Y),point=0,packets=0,reads=0,mismatches=0,held=0,released=0,endpoints=0;
        long previous=0;var command=(ScreenPoint?)null;
        foreach(var row in root.GetProperty("Samples").EnumerateArray())
        {
            long started=row.GetProperty("StartedQpc").GetInt64(),finished=row.GetProperty("FinishedQpc").GetInt64();
            double requestedWait=row.GetProperty("RequestedWait").GetDouble();
            if(started<previous||finished<started||!double.IsFinite(requestedWait)||requestedWait<0||!row.GetProperty("CursorReadSucceeded").GetBoolean())throw new InvalidDataException("Trace clock, wait or cursor reading failed.");
            previous=finished;string phase=row.GetProperty("Phase").GetString()!;
            var issued=row.GetProperty("IssuedPoints");
            if(issued.ValueKind==JsonValueKind.Array)
            {
                if(phase!="move-issued"||issued.GetArrayLength()<1||issued.GetArrayLength()>sample.Speed.Pitch*sample.Speed.BatchSize)
                    throw new InvalidDataException("Trace packet shape changed.");
                if(point>0&&(held!=1||released!=0))throw new InvalidDataException("Movement outside held stroke.");
                packets++;
                foreach(var p in issued.EnumerateArray())
                {
                    var actual=p.Deserialize<ScreenPoint>();var expected=new ScreenPoint(a.X+point*dx,a.Y+point*dy);
                    if(point>length||actual!=expected)throw new InvalidDataException("Trace command path changed.");command=actual;point++;
                }
            }
            if(command is null||row.GetProperty("Command").Deserialize<ScreenPoint>()!=command)throw new InvalidDataException("Trace command acknowledgement changed.");
            bool equal=row.GetProperty("Cursor").Deserialize<ScreenPoint>()==command;reads++;if(!equal)mismatches++;
            if(phase=="held"){if(held!=0||released!=0||endpoints!=1||point!=1)throw new InvalidDataException("Invalid paint-down order.");held++;}
            else if(phase=="released"){if(held!=1||released!=0||point!=length+1)throw new InvalidDataException("Invalid paint-up order.");released++;}
            else if(phase=="endpoint-confirmed")
            {if(!equal||!(endpoints==0&&held==0&&point==1||endpoints==1&&released==1&&point==length+1))throw new InvalidDataException("Endpoint was not acknowledged in order.");endpoints++;}
            else if(phase is not ("move-issued" or "wait-finished"))throw new InvalidDataException("Unknown trace phase.");
        }
        if(point!=length+1||held!=1||released!=1||endpoints!=2)throw new InvalidDataException("Incomplete trace path or button lifecycle.");
        return new(reads,mismatches,point,packets);
    }
    internal static void Run(string sourceDirectory,string outputDirectory)
    {
        string source=Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar),output=Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar);
        bool Contains(string a,string b)=>string.Equals(a,b,StringComparison.OrdinalIgnoreCase)||b.StartsWith(a+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        if(Contains(source,output)||Contains(output,source)||Directory.Exists(output)||File.Exists(output))
            throw new IOException("Use a new output directory outside the source evidence.");
        string manifestPath=Path.Combine(source,"protocol.json");
        if(Hash(manifestPath)!=File.ReadAllText(manifestPath+".sha256").Trim().ToLowerInvariant())throw new InvalidDataException("Frozen protocol hash differs.");
        var manifest=JsonSerializer.Deserialize<StandardEvidenceSeriesCli.Manifest>(File.ReadAllText(manifestPath))??throw new InvalidDataException("Missing protocol.");
        if(manifest.Revision!=StandardEvidenceSeries.Revision||manifest.SettingsSha256!=Hash(Path.Combine(source,"settings.json"))||manifest.ProtocolId!=manifest.Protocol.Identity())
            throw new InvalidDataException("Frozen evidence context differs.");
        manifest.Protocol.Validate();using var status=JsonDocument.Parse(File.ReadAllText(Path.Combine(source,"series-status.json")));
        if(!status.RootElement.TryGetProperty("result",out var result))throw new InvalidDataException("Evidence series is incomplete.");
        if(result.GetProperty("Status").GetString()!="DATA_COLLECTED"||result.GetProperty("CompletedCases").GetInt32()!=42
            ||result.GetProperty("CompletedStages").GetInt32()!=44||result.GetProperty("CompletedStrokes").GetInt32()!=216)
            throw new InvalidDataException("Evidence series is incomplete.");
        using var live=JsonDocument.Parse(File.ReadAllText(Path.Combine(source,"live-context.json")));
        var settings=Settings.Load(Path.Combine(source,"settings.json"));var rect=manifest.Protocol.Canvas;
        if(settings.Calibration.Rect("canvas")!=rect)throw new InvalidDataException("Settings Canvas differs from protocol.");
        Directory.CreateDirectory(output);
        void State(string state,string? reason)=>Write(Path.Combine(output,"review-status.json"),new{status=state,reason,nativeInput=false,productChanged=false,productionEligible=false,dispatchAllowed=false});
        State("ANALYZING",null);
        try
        {
            int nativeFiles=0;PixelImage Native(string path){var image=ReadNative(path,rect);nativeFiles++;return image;}
            var baseline=Native(Path.Combine(source,"baseline.png"));var confirm=Native(Path.Combine(source,"baseline-confirm.png"));
            string seriesId=Hash(Path.Combine(source,"started.json"));
            if(!EvidenceRegionCheck.Same(baseline,confirm,rect,rect))throw new InvalidDataException("Baseline noise exceeds RGB12.");
            var observations=new List<StandardPaintObservation>();var traces=new List<TraceResult>();var overlaps=new List<object>();var drafts=new List<object>();
            foreach(var sample in manifest.Protocol.Cases)
            {
                string folder=Path.Combine(source,$"sample-{sample.Index:D2}");var current=Native(Path.Combine(folder,"before.png"));
                if(!EvidenceRegionCheck.Same(baseline,current,rect,sample.Reservation))throw new InvalidDataException("Reservation was not clean.");
                var layers=new List<(int Color,IReadOnlyList<BrushSpan> Possible)>();
                foreach(var stage in sample.Stages)
                {
                    string stageFolder=Path.Combine(folder,stage.Name);var before=Native(Path.Combine(stageFolder,"before.png"));var after=Native(Path.Combine(stageFolder,"after.png"));
                    if(!EvidenceRegionCheck.Same(current,before,rect,rect)||!EvidenceRegionCheck.ChangesContained(before,after,rect,sample.Reservation))
                        throw new InvalidDataException("Color selection or scene changed outside reservation.");
                    for(int i=0;i<stage.Lines.Length;i++)traces.Add(ReadTrace(Path.Combine(stageFolder,$"stroke-{i:D3}.json.gz"),sample,stage,i));
                    if(sample.Kind!="colour-overdraw")
                    {
                        var command=sample.Reverse?TransferSchedule.Reverse(stage.Lines[0]):stage.Lines[0];
                        observations.Add(StandardPaintObservation.Measure($"{seriesId}/sample-{sample.Index:D2}/{stage.Name}",Context(live.RootElement,settings,sample.Speed),command,before,after,sample.Reservation));
                    }
                    else layers.Add((stage.Colour.Key,Difference(before,after)));
                    current=after;
                }
                if(layers.Count>0)
                {
                    var replay=StandardBoundaryLayers.Replay(rect.Width,rect.Height,layers);overlaps.Add(new{sample=sample.Index,sample.Vertical,replay.Layers,replay.PhysicalQualityCertified});
                    var first=BrushFootprints.Points(layers[0].Possible.ToArray()).ToHashSet();var overlay=new PixelImage(rect.Width,rect.Height);
                    foreach(var p in BrushFootprints.Points(layers[1].Possible.ToArray()))overlay.Set(p.Y*rect.Width+p.X,first.Contains(p)?new(255,0,180):new(70,170,255));
                    Images.Save(overlay,Path.Combine(output,$"sample-{sample.Index:D2}-boundary-overwrite.png"));
                }
            }
            var candidates=observations.GroupBy(o=>(context:o.Context.Id,o.Vertical,o.Reverse,o.Length))
                .Select(g=>StandardKernelCandidate.Freeze(g.Select(StandardKernelObservation.Observational).ToArray())).ToArray();
            foreach(var sample in manifest.Protocol.Cases.Where(c=>c.Kind=="colour-overdraw"))
            {
                var target=new int[rect.Width*rect.Height];Array.Fill(target,-1);
                foreach(var stage in sample.Stages)foreach(var line in stage.Lines)
                    for(int i=0;i<=TransferSchedule.Length(line);i++)target[(line.Y1-rect.Top+(line.X1==line.X2?i:0))*rect.Width+line.X1-rect.Left+(line.Y1==line.Y2?i:0)]=stage.Colour.Key;
                var draft=StandardBoundaryPlan.Build(target,Context(live.RootElement,settings,sample.Speed),candidates);
                drafts.Add(new{sample=sample.Index,draft.Status,draft.Reason,draft.TargetPixels,draft.CoveredSolidPixels,uncovered=draft.Uncovered.Count(x=>x),commands=draft.Commands.Count,
                    draft.DispatchAllowed,draft.PhysicalQualityCertified,draft.OverlapPolicy});
            }
            Write(Path.Combine(output,"observed-strokes.json"),observations);Write(Path.Combine(output,"frozen-candidates.json"),candidates);
            Write(Path.Combine(output,"boundary-drafts.json"),drafts);Write(Path.Combine(output,"overlap-layers.json"),overlaps);
            Write(Path.Combine(output,"summary.json"),new{revision="standard-boundary-review-v1",sourceBuild=live.RootElement.GetProperty("build").GetString(),analyzerBuild=BuildInfo.Full,
                protocolId=manifest.ProtocolId,sourceAppSha256=manifest.AppSha256,sourceCoreSha256=manifest.CoreSha256,
                nativeFiles,traceFiles=traces.Count,cursorReadings=traces.Sum(t=>t.Readings),cursorMismatches=traces.Sum(t=>t.Mismatches),issuedPoints=traces.Sum(t=>t.Points),packets=traces.Sum(t=>t.Packets),
                observations=observations.Count,candidates=candidates.Length,establishedSolidPixels=candidates.Sum(c=>c.Solid.Sum(r=>r.Right-r.Left)),
                reason="single-pass series does not measure repeat-stable Solid; no untouched held-out; no production dispatch",
                nativeInput=false,productChanged=false,productionEligible=false,physicalQualityCertified=false});
            State("DATA_ANALYZED_NOT_QUALIFIED",null);
        }
        catch(Exception error){State("INCOMPLETE",error.Message);throw;}
    }
}
