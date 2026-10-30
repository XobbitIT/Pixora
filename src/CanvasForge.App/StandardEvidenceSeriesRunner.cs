using System.Security.Cryptography;
using System.Diagnostics;
using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

internal static class StandardEvidenceSeriesRunner
{
    internal sealed record Hooks(Action Check,Func<PixelImage> Shot,Action<Rgb> SelectColour,
        Action<EvidenceSeriesCase,ScreenLine,bool,string> Draw,Action Release,bool NativeInput=false,Action<PixelImage>? ValidateInitialBaseline=null,Action? ValidateFinalArtifacts=null);
    internal sealed record Result(string Status,string? Reason,int CompletedCases,int CompletedStages,int CompletedStrokes);
    internal static Result Run(StandardEvidenceSeries protocol,string directory,Hooks hooks)
    {
        protocol.Validate();return RunCases(protocol.Canvas,protocol.Cases,StandardEvidenceSeries.Revision,protocol.Identity(),directory,hooks);
    }
    internal static Result Run(StandardRepeatSeries protocol,string directory,Hooks hooks)
    {
        protocol.Validate();return RunCases(protocol.Canvas,protocol.Cases,StandardRepeatSeries.Revision,protocol.Identity(),directory,hooks,finalCapture:true);
    }
    internal const string TrackCaptureRevision="standard-track-increment-v1";
    internal static Result RunCases(ScreenRect canvas,EvidenceSeriesCase[] cases,string revision,string protocolId,string directory,Hooks hooks,bool finalCapture=false,bool captureEachStroke=false)
    {
        Directory.CreateDirectory(directory);
        using(var marker=new FileStream(Path.Combine(directory,"started.json"),FileMode.CreateNew,FileAccess.Write))
            JsonSerializer.Serialize(marker,new{startedUtc=DateTimeOffset.UtcNow,revision,protocolId,automaticRetry=false});
        var result=new Result("INCOMPLETE","not_started",0,0,0);int sequence=0,emittedStrokes=0,completedStrokeCaptures=0;bool inputAttempted=false;
        void Write(string action,object? details=null)
        {
            var row=new{sequence=++sequence,timeUtc=DateTimeOffset.UtcNow,action,details,result};
            File.AppendAllText(Path.Combine(directory,"events.jsonl"),JsonSerializer.Serialize(row)+"\n");
            // Append-only progress is authoritative if the replace is interrupted.
            string path=Path.Combine(directory,"series-status.json"),tmp=path+".tmp";
            File.WriteAllText(tmp,JsonSerializer.Serialize(new{sequence,result,requiredCases=cases.Length,inputAttempted,nativeInputMayHaveOccurred=hooks.NativeInput,paintInputMayHaveOccurred=inputAttempted&&hooks.NativeInput,
                physicalQualityCertified=false,automaticRetry=false,automaticResume=false,emittedStrokes,completedStrokeCaptures,
                captureEachStroke,trackCaptureRevision=captureEachStroke?TrackCaptureRevision:null,
                statusPublicationPolicy="at released-input boundaries only; up to 5 metadata rename retries, 10ms apart; never replay paint"}));
            PublishReleasedStatus(tmp,path);
        }
        PixelImage Shot(string path)
        {
            hooks.Release();hooks.Check();long started=Stopwatch.GetTimestamp();var shot=hooks.Shot();long finished=Stopwatch.GetTimestamp();
            if(shot.Width!=canvas.Width||shot.Height!=canvas.Height)throw new InvalidDataException("Native evidence geometry changed.");
            Images.Save(shot,path);
            File.WriteAllText(Path.ChangeExtension(path,"json"),JsonSerializer.Serialize(new{path=Path.GetFileName(path),createdUtc=DateTimeOffset.UtcNow,
                canvas,captureWindow=new{startedQpc=started,finishedQpc=finished,qpcFrequency=Stopwatch.Frequency},rgbaSha256=Convert.ToHexString(SHA256.HashData(shot.Rgba)).ToLowerInvariant(),
                coordinateSpace="screen physical pixels; native 1:1",nativeInput=hooks.NativeInput,synthetic=!hooks.NativeInput,physicalQualityCertified=false}));
            return shot;
        }
        Write("series_started");
        try
        {
            hooks.Check();var baseline=Shot(Path.Combine(directory,"baseline.png"));
            var stable=Shot(Path.Combine(directory,"baseline-confirm.png"));
            if(!EvidenceRegionCheck.Same(baseline,stable,canvas,canvas))throw new InvalidDataException("Evidence baseline noise exceeds RGB12.");
            hooks.ValidateInitialBaseline?.Invoke(baseline);
            foreach(var sample in cases)
            {
                hooks.Check();string folder=Path.Combine(directory,$"sample-{sample.Index:D2}");Directory.CreateDirectory(folder);
                if(Directory.EnumerateFileSystemEntries(folder).Any())throw new IOException("Evidence sample destination is not empty.");
                File.WriteAllText(Path.Combine(folder,"sample.json"),JsonSerializer.Serialize(sample));Write("sample_started",new{sample.Index,sample.Kind,sample.Reservation});
                var current=Shot(Path.Combine(folder,"before.png"));
                if(!EvidenceRegionCheck.Same(baseline,current,canvas,sample.Reservation))throw new InvalidDataException("Reserved sample area is not clean relative to initial baseline.");
                foreach(var stage in sample.Stages)
                {
                    hooks.Check();hooks.SelectColour(stage.Colour);hooks.Check();
                    string stageFolder=Path.Combine(folder,stage.Name);Directory.CreateDirectory(stageFolder);
                    var before=Shot(Path.Combine(stageFolder,"before.png"));
                    if(!EvidenceRegionCheck.Same(current,before,canvas,canvas))throw new InvalidDataException("Canvas changed during colour selection.");
                    Write("colour_started",new{sample.Index,stage.Name,requested=stage.Colour.Hex,strokes=stage.Lines.Length});
                    for(int i=0;i<stage.Lines.Length;i++)
                    {
                        hooks.Check();bool reverse=sample.Kind=="colour-overdraw"?i%2==1:sample.Reverse;
                        string trace=Path.Combine(stageFolder,$"stroke-{i:D3}.json.gz");
                        string stem=Path.Combine(stageFolder,$"stroke-{i:D3}");PixelImage? trackBefore=null;
                        if(captureEachStroke)
                        {
                            trackBefore=Shot(stem+"-before.png");
                            if(!EvidenceRegionCheck.Same(current,trackBefore,canvas,canvas))throw new InvalidDataException("Canvas changed between recorded tracks.");
                        }
                        Write("stroke_started",new{sample.Index,stage.Name,line=i,command=stage.Lines[i],reverse,trace=Path.GetFileName(trace)});
                        inputAttempted=true;
                        hooks.Draw(sample,stage.Lines[i],reverse,trace);hooks.Release();emittedStrokes++;
                        if(captureEachStroke)
                        {
                            Write("stroke_emitted",new{sample.Index,stage.Name,line=i});
                            var trackAfter=Shot(stem+"-after.png");
                            if(!EvidenceRegionCheck.ChangesContained(trackBefore!,trackAfter,canvas,sample.Reservation))
                                throw new InvalidDataException("Track changes exceeded the reserved band.");
                            using(var file=new FileStream(stem+"-pair.json",FileMode.CreateNew,FileAccess.Write))
                                JsonSerializer.Serialize(file,new{revision=TrackCaptureRevision,protocolId,sample=sample.Index,stage=stage.Name,track=i,
                                    trace=Path.GetFileName(trace),traceSha256=StandardBoundaryReview.Hash(trace),
                                    before=Path.GetFileName(stem+"-before.png"),beforeRgbaSha256=Convert.ToHexString(SHA256.HashData(trackBefore!.Rgba)).ToLowerInvariant(),
                                    after=Path.GetFileName(stem+"-after.png"),afterRgbaSha256=Convert.ToHexString(SHA256.HashData(trackAfter.Rgba)).ToLowerInvariant(),
                                    nativeInput=hooks.NativeInput,synthetic=!hooks.NativeInput,physicalQualityCertified=false});
                            current=trackAfter;completedStrokeCaptures++;
                        }
                        result=result with{CompletedStrokes=result.CompletedStrokes+1};
                        Write("stroke_captured",new{sample.Index,stage.Name,line=i});
                    }
                    var after=Shot(Path.Combine(stageFolder,"after.png"));
                    if(captureEachStroke&&!EvidenceRegionCheck.Same(current,after,canvas,canvas))throw new InvalidDataException("Canvas changed after the final recorded track.");
                    if(!EvidenceRegionCheck.ChangesContained(before,after,canvas,sample.Reservation))throw new InvalidDataException("Paint or scene changes exceeded the reserved sample area.");
                    current=after;result=result with{CompletedStages=result.CompletedStages+1};Write("colour_captured",new{sample.Index,stage.Name});
                }
                // Singles have before/after for their one stroke. Compound band
                // cases retain after each colour plus every stroke trace.
                Images.Save(current,Path.Combine(folder,"after.png"));
                result=result with{CompletedCases=result.CompletedCases+1};Write("sample_captured",new{sample.Index});
            }
            if(finalCapture)Shot(Path.Combine(directory,"final.png"));
            hooks.ValidateFinalArtifacts?.Invoke();
            result=result with{Status="DATA_COLLECTED",Reason=null};Write("series_captured");
        }
        catch(Exception error)
        {
            result=result with{Reason=error.GetType().Name+": "+error.Message};Write("series_incomplete",new{reason=result.Reason,diagnosticException=error.ToString(),error.HResult});
        }
        finally
        {
            try{hooks.Release();}
            catch(Exception error){result=result with{Status="INCOMPLETE",Reason="release_failed: "+error.Message};}
            Write("series_finished");
        }
        return result;
    }
    // The journal row is already durable. Every caller is between strokes,
    // with no held paint input. Windows readers can transiently deny rename;
    // retry only this publication, never colour selection or a paint command.
    internal static void PublishReleasedStatus(string temporary,string path)
    {
        for(int attempt=0;;attempt++)
        {
            try{File.Move(temporary,path,true);return;}
            catch(UnauthorizedAccessException) when(attempt<5){Thread.Sleep(10);}
            catch(IOException error) when(attempt<5&&(error.HResult&0xffff) is 32 or 33){Thread.Sleep(10);}
        }
    }
}
