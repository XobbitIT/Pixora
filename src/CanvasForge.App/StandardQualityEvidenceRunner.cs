using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

internal static class StandardQualityEvidenceRunner
{
    internal sealed record Hooks(Action Prepare,Func<PixelImage> Shot,Action<ScreenLine,int> Draw,Action Release);
    internal sealed record Result(string Status,string? Reason,int CompletedSegments,QualityLineMetrics? Metrics);
    internal static Result Run(StandardQualityExperiment protocol,QualityLineCase sample,string directory,PixelImage? baseline,Hooks hooks)
    {
        protocol.Validate();
        if(!protocol.Cases.Contains(sample))throw new InvalidDataException("Unregistered evidence sample.");
        return RunCase(StandardQualityExperiment.Revision,protocol.Canvas,sample,directory,baseline,hooks);
    }
    internal static Result Run(StandardDirectionExperiment protocol,QualityLineCase sample,string directory,PixelImage? baseline,Hooks hooks)
    {
        protocol.Validate();
        if(!protocol.Cases.Contains(sample))throw new InvalidDataException("Unregistered evidence sample.");
        return RunCase(StandardDirectionExperiment.Revision,protocol.Canvas,sample,directory,baseline,hooks);
    }
    private static Result RunCase(string revision,ScreenRect canvas,QualityLineCase sample,string directory,PixelImage? baseline,Hooks hooks)
    {
        Directory.CreateDirectory(directory);
        if(Directory.EnumerateFileSystemEntries(directory).Any())throw new IOException("Evidence sample destination must be new.");
        var result=new Result("incomplete","not_started",0,null);var startedUtc=DateTimeOffset.UtcNow;
        void Receipt()=>File.WriteAllText(Path.Combine(directory,"receipt.json"),JsonSerializer.Serialize(new
            {revision,startedUtc,updatedUtc=DateTimeOffset.UtcNow,sample,result,physicalQualityCertified=false,automaticRetry=false}));
        Receipt();
        try
        {
            hooks.Prepare();var background=hooks.Shot();Images.Save(background,Path.Combine(directory,"background.png"));
            var before=hooks.Shot();Images.Save(before,Path.Combine(directory,"before.png"));
            if(BrushFootprints.Contrast(background,before).PeakDelta>12||baseline is not null&&BrushFootprints.Contrast(baseline,before).PeakDelta>12)
                throw new InvalidDataException("Evidence baseline is not clean or capture noise exceeds RGB12.");
            foreach(var line in sample.Segments)
            {
                int index=result.CompletedSegments;
                Images.Save(hooks.Shot(),Path.Combine(directory,$"segment-{index:D2}-before.png"));
                hooks.Draw(line,result.CompletedSegments);result=result with{CompletedSegments=result.CompletedSegments+1};
                hooks.Release();Images.Save(hooks.Shot(),Path.Combine(directory,$"segment-{index:D2}-after.png"));Receipt();
            }
            var after=hooks.Shot();Images.Save(after,Path.Combine(directory,"after.png"));
            result=result with{Status="captured",Reason=null,Metrics=QualityLineAnalysis.Measure(sample,canvas,before,after)};
        }
        catch(Exception e){result=result with{Status="incomplete",Reason=e.GetType().Name+": "+e.Message};}
        finally{try{hooks.Release();}finally{Receipt();}}
        return result;
    }
}
