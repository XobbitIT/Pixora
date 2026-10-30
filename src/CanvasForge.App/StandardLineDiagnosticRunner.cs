using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

// A diagnostic attempt never restarts itself, resumes on a painted baseline,
// writes a checkpoint, installs a footprint or enables a production route.
internal static class StandardLineDiagnosticRunner
{
    internal sealed record Hooks(Action Prepare,Func<PixelImage> Shot,Action<int> Draw,Action Release);
    internal sealed record Result(string Status,string? Reason,int CompletedLines,StandardLineDiagnosticMetrics? Metrics,
        bool PhysicalResultVerified=false,bool ProductionEnabled=false);
    internal static Result Run(StandardLineDiagnosticPlan plan,string directory,Hooks hooks)
    {
        Directory.CreateDirectory(directory);
        if(Directory.EnumerateFileSystemEntries(directory).Any())throw new IOException("Diagnostic destination must be new.");
        Result result=new("incomplete","not_started",0,null);
        var options=new JsonSerializerOptions{WriteIndented=true};
        void Receipt()=>File.WriteAllText(Path.Combine(directory,"attempt.json"),JsonSerializer.Serialize(new
            {revision=StandardLineDiagnosticPlan.Revision,build=BuildInfo.Full,createdUtc=DateTimeOffset.UtcNow,plan,result},options));
        Receipt();
        try
        {
            hooks.Prepare();
            var background=hooks.Shot();Images.Save(background,Path.Combine(directory,"background.png"));
            var before=hooks.Shot();Images.Save(before,Path.Combine(directory,"before.png"));
            if(BrushFootprints.Contrast(background,before).PeakDelta>12)
                throw new InvalidDataException("diagnostic_background_unstable");
            // Disk failures before a line must abort before any paint is sent.
            result=result with{Reason="drawing_first_line"};Receipt();
            hooks.Draw(0);result=result with{CompletedLines=1,Reason="capturing_first_line"};
            var first=hooks.Shot();Images.Save(first,Path.Combine(directory,"after-first.png"));Receipt();
            hooks.Draw(1);result=result with{CompletedLines=2,Reason="capturing_second_line"};
            var second=hooks.Shot();Images.Save(second,Path.Combine(directory,"after-second.png"));
            var metrics=StandardLineDiagnosticAnalysis.Inspect(plan,background,before,first,second);
            result=new("captured",null,2,metrics);
        }
        catch(Exception error)
        {
            // Including interruptions: return INCOMPLETE to the UI, so the
            // generic preparation retry cannot silently redraw this attempt.
            result=result with{Status="incomplete",Reason=error.GetType().Name+": "+error.Message};
        }
        finally
        {
            // Always release before disk IO, even if a capture or receipt fails.
            try{hooks.Release();}
            finally{Receipt();}
        }
        return result;
    }
}
