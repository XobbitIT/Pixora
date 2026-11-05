using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

// Native lossless observations, not a physical-quality verdict. No input here.
internal static class PaintRunEvidence
{
    internal static void Save(string directory,string phase,PixelImage canvas,ScreenRect rect,
        PixelImage editor,ScreenRect screen,object context)
    {
        if(phase is not ("before" or "after")||!rect.Valid||canvas.Width!=rect.Width||canvas.Height!=rect.Height
            ||editor.Width!=screen.Width||editor.Height!=screen.Height)throw new ArgumentException("Invalid native evidence geometry.");
        Directory.CreateDirectory(directory);
        Images.Save(canvas,Path.Combine(directory,$"native-{phase}.png"));
        Images.Save(editor,Path.Combine(directory,$"editor-{phase}.png"));
        File.WriteAllText(Path.Combine(directory,$"native-{phase}.json"),JsonSerializer.Serialize(new
        {
            revision=1,phase,rect,screen,coordinateSpace="screen physical pixels; native capture 1:1",
            canvasRgbaSha256=Convert.ToHexString(SHA256.HashData(canvas.Rgba)).ToLowerInvariant(),
            editorRgbaSha256=Convert.ToHexString(SHA256.HashData(editor.Rgba)).ToLowerInvariant(),
            context,physicalQualityCertified=false,paintedRgbVerified=false
        }));
    }
    internal static void SavePlan(string directory,PaintPlan plan,Settings settings,
        Dictionary<int,List<PaintBatch>> groups,IReadOnlyList<int> order,CancellationToken token=default)
    {
        bool joining=TransferSchedule.StandardJoin(settings);
        if(ReleaseProfile.Standard(settings)&&!joining&&groups.Values.SelectMany(g=>g).Any(op=>op.Segments.Count!=1))
            throw new InvalidDataException("Standard command evidence requires exactly one segment per operation.");
        Directory.CreateDirectory(directory);
        using var file=File.Create(Path.Combine(directory,"command-plan.json.gz"));
        using var zip=new GZipStream(file,CompressionLevel.Fastest);
        JsonSerializer.Serialize(zip,new
        {
            revision=1,plan.Identity,plan.TargetIdentity,imageRecipe=plan.Recipe?.ToJson(),canvas=settings.Calibration.Rect("canvas"),
            emitter=ReleaseProfile.Standard(settings)?StandardStrokeMotion.Emitter(settings):StrokeMotion.Revision,mapping="identity",rounding="to-even",
            groups=order.Select((color,group)=>new
            {
                group,color=plan.Palette[color].Color.Hex,
                operations=groups[color].Select((op,line)=>
                {
                    token.ThrowIfCancellationRequested();
                    return new{line,op.Size,op.ShapeSlot,op.ProfileId,op.StrokeProfileId,
                        // A joined press runs its segments exactly as recorded (no parity reversal).
                        op.SourceStrokes,op.Segments,reverse=ReleaseProfile.Standard(settings)?(bool?)(op.Segments.Count>1?false:line%2==1):null,
                        route=ReleaseProfile.Standard(settings)?(op.Segments.Count>1?"standard-join-experimental":StandardLineMotion.Applies(settings,op.Segments[0])?"native-line-prototype":"standard"):op.StrokeProfileId is not null?"measured-stroke":"experimental; see runtime route log"};
                })
            }),
            physicalResultVerified=false,plannedNotObserved=true
        });
    }
}

internal sealed partial class Painter
{
    private int strokeTraces;
    private bool nativeSegmentResumed;
    internal Action<string,bool>? StandardEvidenceCapture {get;set;}
    private void SaveStrokeTrace(StrokeTraceInput trace,ScreenLine line,SpeedProfile speed,bool reverse)
    {
        int index=++strokeTraces;if(runSnapshotDirectory is null)return;
        try
        {
            using var file=File.Create(Path.Combine(runSnapshotDirectory,$"stroke-{index:D3}.json.gz"));
            using var zip=new GZipStream(file,CompressionLevel.Fastest);
            JsonSerializer.Serialize(zip,new{revision=1,line,reverse,speed,size=DesiredControls(),shape=activeShape,
                emitter=StandardStrokeMotion.Emitter(settings),mapping="identity",index,maxStrokes=128,maxSamplesPerStroke=256,
                trace.Truncated,trace.Samples,diagnosticOnly=true,performanceQualified=false,
                cursorPhasesOnly=true,completeContinuousCursorPathProven=false});
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){Log("stroke_trace_unavailable",new{index,reason=e.Message});}
    }
    private void SaveExecutionEvidence(PaintPlan plan,Dictionary<int,List<PaintBatch>> groups,IReadOnlyList<int> order)
    {
        if(runSnapshotDirectory is null)return;
        try{PaintRunEvidence.SavePlan(runSnapshotDirectory,plan,settings,groups,order,token);}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException)
        {Log("command_snapshot_unavailable",new{reason=e.Message});}
    }
    private void CaptureStandardEvidence(string phase,PaintPlan plan,bool resumed=false)
    {
        if(StandardEvidenceCapture is {} capture){capture(phase,resumed);return;}
        if(runSnapshotDirectory is null)return;
        try
        {
            // Existing capture and editor guards remain in force. Parking never
            // presses a painting button. An interrupted capture waits for proof.
            for(;;)try
            {
                WaitReady();CheckBoundary();Native.ReleaseChecked();
                var started=DateTimeOffset.UtcNow;
                var canvas=settings.Calibration.Rect("canvas");
                var frame=StableShot(canvas);CheckBoundary();
                var proof=ReadEditor();CheckBoundary();
                if(proof.State!=EditorEvidenceState.Present){EditorUnavailable(proof);continue;}
                var editor=lastEditorFrame!;Native.ReleaseChecked();
                PaintRunEvidence.Save(runSnapshotDirectory,phase,frame,canvas,editor,windowRect,new
                {
                    started,finished=DateTimeOffset.UtcNow,version=BuildInfo.Full,plan.Identity,plan.TargetIdentity,
                    resumed,done=timingDone,total=timingTotal,activeSeconds=ActiveSeconds,
                    origin=Native.ClientOrigin(window),dpi=windowDpi,client=Native.ClientSize(window),pid=windowProcessId,
                    mappingRevision="identity",transformHash="identity",proof,capture=lastEditorCapture,
                    canvasConfirmation=EditorEvidence.Reference(settings)?.Structure?.Bounds,
                    separateCaptures=true,baselineScope=resumed?"resume segment":"new start"
                });
                Log("native_run_evidence",new{phase,directory=runSnapshotDirectory,canvas,physicalQualityCertified=false});
                break;
            }
            catch(InputInterrupted){WaitReady();}
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {Log("native_run_evidence_unavailable",new{phase,reason=e.Message,physicalQualityCertified=false});}
    }
}
