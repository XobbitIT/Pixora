using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

internal sealed partial class Painter
{
    public PhysicalCoverageResult? FinalCoverage { get; private set; }
    internal string? FinalAuditDirectory=>finalAuditDirectory;
    private PixelImage? finalAuditBefore;
    private int[]? finalAuditLabels;
    private string? finalAuditDirectory,finalAuditProblem;
    private int skippedPaletteOperations;

    private PixelImage CaptureForFinalAudit(ScreenRect canvas)
    {
        for(;;)try{return StableShot(canvas);}catch(InputInterrupted){WaitReady();}
    }

    private void BeginFinalAudit(PaintPlan plan,bool resume)
    {
        var canvas=settings.Calibration.Rect("canvas");
        finalAuditDirectory=Path.Combine(Path.GetDirectoryName(logPath)!,"coverage-audit");
        try
        {
            finalAuditLabels=FinalCoverageAudit.Labels(plan,canvas);
            Directory.CreateDirectory(finalAuditDirectory);
            // Fresh START invalidates the prior run before writing a checkpoint.
            // RESUME may reuse only the baseline left by that current run.
            string baseline=FinalAuditBaseline.PathFor(logPath,plan.Identity);
            if(resume)
            {
                if(!File.Exists(baseline)){finalAuditProblem="Initial Canvas snapshot for this plan is unavailable.";return;}
                finalAuditBefore=Images.Load(baseline);
                if(finalAuditBefore.Width!=canvas.Width||finalAuditBefore.Height!=canvas.Height)
                {finalAuditBefore=null;finalAuditProblem="Initial Canvas dimensions do not match.";}
            }
            else
            {
                finalAuditBefore=CaptureForFinalAudit(canvas);Images.Save(finalAuditBefore,baseline);
            }
        }
        catch(Exception e) when(e is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        {finalAuditProblem=e.Message;Log("final_audit_baseline_unavailable",new{reason=finalAuditProblem});}
    }

    private void CompleteFinalAudit(PaintPlan plan)
    {
        var canvas=settings.Calibration.Rect("canvas");PixelImage? after=null;
        try
        {
            if(finalAuditLabels is not null)after=CaptureForFinalAudit(canvas);
            if(finalAuditBefore is not null&&after is not null&&finalAuditLabels is not null)
                FinalCoverage=FinalCoverageAudit.Read(finalAuditBefore,after,finalAuditLabels,token);
        }
        catch(Exception e) when(e is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        {finalAuditProblem=e.Message;}
        if(FinalCoverage is null)
        {
            string reason=finalAuditProblem??"Stable initial and final Canvas snapshots are required.";
            if(finalAuditLabels is not null)FinalCoverage=FinalCoverageAudit.Unavailable(finalAuditLabels,reason);
            else
            {
                int target=(int)Math.Round(plan.Indices.Count(i=>i>=0)*(double)canvas.Width*canvas.Height/Math.Max(1,plan.Indices.Length));
                FinalCoverage=new(target,0,0,target,[],[],reason);
            }
        }
        Log("final_coverage_audit",new{FinalCoverage.Target,FinalCoverage.Confirmed,FinalCoverage.Missing,FinalCoverage.Unknown,
            FinalCoverage.ConfirmedPercent,FinalCoverage.MissingPercent,FinalCoverage.UnknownPercent,FinalCoverage.Passed,
            FinalCoverage.UnavailableReason,skippedPaletteOperations,criteria="CoverageAudit.Read unchanged; local 128px tiles",paintedRgbVerified=false});
        try
        {
            Directory.CreateDirectory(finalAuditDirectory!);
            if(finalAuditBefore is not null)Images.Save(finalAuditBefore,Path.Combine(finalAuditDirectory!,"group--1-before.png"));
            if(after is not null)
            {
                Images.Save(after,Path.Combine(finalAuditDirectory!,"group--1-after.png"));
                if(FinalCoverage.MissingMask.Length==after.Width*after.Height)
                {
                    Images.Save(CoverageAudit.GapOverlay(after,FinalCoverage.MissingMask),Path.Combine(finalAuditDirectory!,"group--1-gaps.png"));
                    Images.Save(FinalCoverageAudit.UnknownOverlay(after,FinalCoverage.UnknownMask),Path.Combine(finalAuditDirectory!,"group--1-unknown.png"));
                }
            }
            File.WriteAllText(Path.Combine(finalAuditDirectory!,"final-summary.json"),JsonSerializer.Serialize(new{
                plan.Identity,FinalCoverage.Target,FinalCoverage.Confirmed,FinalCoverage.Missing,FinalCoverage.Unknown,
                FinalCoverage.Passed,FinalCoverage.UnavailableReason,skippedPaletteOperations}));
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException)
        {Log("final_audit_save_failed",new{reason=e.Message});}
    }
}
