using CanvasForge.Core;
using System.Diagnostics;
using System.Text.Json;

namespace CanvasForge.App;

internal sealed partial class Painter
{
    private readonly StrokeCursorGuard strokeCursor;
    private readonly CursorReadMetrics cursorReadMetrics=new();
    private readonly GuardedNativeInput.Environment inputEnvironment;
    private ScreenPoint ReadGuardCursor()=>cursorReadMetrics.Read(Native.Cursor);
    private void CheckStrokeCursor(string phase,bool requireEndpoint=false)
    {
        try{strokeCursor.Check(phase,requireEndpoint);}
        catch(StrokeCursorMismatchException error)
        {CursorCommandMismatch(error.Expected,error.Actual,error.Phase);}
        catch(CursorReadException error)
        {
            Log("input_cursor_unavailable",new{reason="cursor_read_failed",error.Win32Error,phase,sampleStatus="invalid",duringStroke=true});
            throw;
        }
    }
    private ScreenPoint ReadCursor()
    {
        try{return Native.Cursor();}
        catch(CursorReadException error)
        {Log("input_cursor_unavailable",new{reason="cursor_read_failed",error.Win32Error,sampleStatus="invalid",paintDown=false});throw;}
    }
    private void CursorChangedBeforeDown(ScreenPoint before,ScreenPoint after)
    {
        Native.Release();Paused=true;pauseReason="cursor_moved";editorValidationEnabled=false;
        Log("input_pause",new{reason=pauseReason,before,after,paintDown=false});
        report(new(0,0,0,0,RecoveryMessages.CursorMoved));throw new InputInterrupted();
    }
    private void CursorCommandMismatch(ScreenPoint expected,ScreenPoint actual,string phase="endpoint")
    {
        Native.Release();Paused=true;pauseReason="cursor_position_mismatch";editorValidationEnabled=false;
        Log("input_pause",new{reason=pauseReason,expected,actual,phase,sampleStatus="invalid"});
        report(new(0,0,0,0,RecoveryMessages.CursorPosition));throw new InputInterrupted();
    }
    private void CheckPaintTool()
    {
        long started=Stopwatch.GetTimestamp();toolProofChecks++;
        try{CheckPaintToolCore();}
        finally{toolProofSeconds+=Stopwatch.GetElapsedTime(started).TotalSeconds;}
    }
    private void CheckPaintToolCore()
    {
        var reference=EditorEvidence.Reference(settings);
        if(reference?.Tool is not {} tool)
        {EditorUnavailable(new(EditorEvidenceState.Missing,"tool_reference_missing",["tool-state"]));return;}
        var origin=Native.ClientOrigin(window);
        var area=BrushToolState.CaptureRegion(tool,settings.PaintCalibration().Rect("brush_shapes"),origin);
        if(!area.Valid||area.Left<windowRect.Left||area.Top<windowRect.Top||area.Right>windowRect.Right||area.Bottom>windowRect.Bottom)
        {EditorUnavailable(new(EditorEvidenceState.Missing,"tool_geometry_changed",["tool-state"]));return;}
        var frame=Native.Screenshot(area);
        var result=BrushToolState.Read(tool,frame,area,settings.PaintCalibration().Rect("brush_shapes"),origin);
        if(!result.Safe)
        {
            Native.Release();
            try
            {
                string folder=Path.Combine(Path.GetDirectoryName(logPath)!,"editor-diagnostics",$"tool-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}");
                Directory.CreateDirectory(folder);Images.Save(frame,Path.Combine(folder,"tool-row.png"));
                File.WriteAllText(Path.Combine(folder,"tool-state.json"),JsonSerializer.Serialize(new
                    {result,reference=tool,screen=area,origin,shapes=settings.PaintCalibration().Rect("brush_shapes"),paintDown=false},new JsonSerializerOptions{WriteIndented=true}));
                Log("tool_state_failed",new{result,folder,paintDown=false});
            }
            catch(Exception error) when(error is IOException or UnauthorizedAccessException)
            {Log("tool_diagnostic_failed",new{message=error.Message});}
            EditorUnavailable(new(EditorEvidenceState.Missing,result.Reason,["tool-state"]));
        }
    }
    private bool editorValidationEnabled,provingEditor;
    private long nextEditorCheck;
    private long nextEditorDiagnostic;
    private EditorEvidenceResult? lastEditorFailure;
    private PixelImage? lastEditorFrame;
    private EditorCaptureMetadata? lastEditorCapture;
    private string? lastEditorDirectory;
    private EditorEvidenceResult ReadEditor(bool saveFailure=true)
    {
        long started=Stopwatch.GetTimestamp();editorProofChecks++;
        try{return ReadEditorCore(saveFailure);}
        finally{editorProofSeconds+=Stopwatch.GetElapsedTime(started).TotalSeconds;}
    }
    private EditorEvidenceResult ReadEditorCore(bool saveFailure)
    {
        if(saveFailure)lastEditorDirectory=null;
        var cursorBefore=ReadCursor();long started=Stopwatch.GetTimestamp();var frame=Native.Screenshot(windowRect);long ended=Stopwatch.GetTimestamp();
        lastEditorFrame=frame;lastEditorCapture=new(started,ended,Stopwatch.Frequency,cursorBefore,ReadCursor());
        var reference=EditorEvidence.Reference(settings);
        var origin=Native.ClientOrigin(window);int dpi=Native.DpiOf(window);var client=Native.ClientSize(window);
        var result=EditorEvidence.Read(settings,reference,frame,windowRect,origin,dpi,client);
        if(result.State!=EditorEvidenceState.Present)
        {
            Native.Release(); // No painting button may remain held during disk IO.
            if(saveFailure&&reference is not null&&Environment.TickCount64>=nextEditorDiagnostic)
            {
                nextEditorDiagnostic=Environment.TickCount64+5000;
                try
                {
                    string folder=EditorFailureDiagnostics.Save(Path.GetDirectoryName(logPath)!,reference,result,frame,
                        windowRect,origin,dpi,client,Native.Cursor(),lastEditorCapture);
                    lastEditorDirectory=folder;
                    Log("editor_evidence_unavailable",new{result,directory=folder,scopedCapture=true});
                }
                catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException)
                {Log("editor_diagnostic_failed",new{type=error.GetType().Name,message=error.Message});}
            }
        }
        return result;
    }
    private EditorRetryResult RetryEditorFrame(EditorEvidenceResult first)
    {
        if(!EditorEvidenceRetry.FrameOnly(first))return new(first,1,false);
        bool prior=provingEditor;provingEditor=true;editorValidationEnabled=false;
        string? folder=lastEditorDirectory;int index=0;var trace=new List<object>();
        trace.Add(new{index,result=first,capture=lastEditorCapture});
        try
        {
            var retry=EditorEvidenceRetry.Run(first,Native.Release,
                ()=>DelayTimer.Wait(EditorEvidenceRetry.IntervalSeconds,()=>{Check(true);if(Paused)throw new InputInterrupted();}),
                ()=>
                {
                    // Check before and after each capture. PID/DPI/rect/focus
                    // remain mandatory before a clean frame can authorize input.
                    Check(true);if(Paused)throw new InputInterrupted();
                    var result=ReadEditor(false);Check(true);if(Paused)throw new InputInterrupted();index++;
                    trace.Add(new{index,result,capture=lastEditorCapture});
                    if(folder is not null&&lastEditorFrame is {} frame&&EditorEvidence.Reference(settings) is {} reference)
                        try{EditorFailureDiagnostics.Save(Path.GetDirectoryName(logPath)!,reference,result,frame,windowRect,
                            Native.ClientOrigin(window),windowDpi,Native.ClientSize(window),Native.Cursor(),lastEditorCapture,folder,index);}
                        catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException)
                        {Log("editor_diagnostic_failed",new{message=e.Message});}
                    return result;
                });
            if(folder is not null)
                try{File.WriteAllText(Path.Combine(folder,"burst.json"),JsonSerializer.Serialize(new{retry,frames=trace},new JsonSerializerOptions{WriteIndented=true}));}
                catch(Exception e) when(e is IOException or UnauthorizedAccessException){Log("editor_diagnostic_failed",new{message=e.Message});}
            Log("editor_frame_recheck",new{retry,directory=folder,referenceChanged=false});
            return retry;
        }
        finally{provingEditor=prior;lastEditorFrame=null;}
    }
    private void RequireEditorProof()
    {
        // Both start and every manual resume prove the editor before invasive UI.
        provingEditor=true;
        try
        {
        CheckBoundary();var first=ReadEditor();
        if(first.State!=EditorEvidenceState.Present)
        {
            var retry=RetryEditorFrame(first);
            if(!retry.Recovered){EditorUnavailable(retry.Result.State==EditorEvidenceState.Present?first:retry.Result);return;}
            first=retry.Result;
        }
        Delay(.12);var second=ReadEditor();
        if(second.State!=EditorEvidenceState.Present){EditorUnavailable(second);return;}
        editorValidationEnabled=true;nextEditorCheck=Environment.TickCount64+400;
        Log("editor_proof",new{first,second,manualParameters="Interval/Opacity: manual precondition, not verified by Pixora"});
        }
        finally{provingEditor=false;}
    }
    private void EditorUnavailable(EditorEvidenceResult evidence)
    {
        lastEditorFailure=evidence;
        Native.Release();Paused=true;pauseReason=evidence.Reason;editorValidationEnabled=false;
        Log("input_pause",new{reason=pauseReason,evidence,autoResume=false});
        report(new(0,0,0,0,RecoveryMessages.Editor(evidence)));
        if(probeMode)throw new PreparationInterruptedException("Редактор Rust не підтверджено. Відкрий полотно та повтори захоплення.");
        throw new InputInterrupted();
    }
    private void CheckEditorHeartbeat()
    {
        if(paletteHover?.Active==true||!editorValidationEnabled||Paused||Environment.TickCount64<nextEditorCheck)return;
        nextEditorCheck=Environment.TickCount64+400;
        var evidence=ReadEditor();
        if(evidence.State!=EditorEvidenceState.Present)
        {
            Native.Release();editorValidationEnabled=false;
            var retry=RetryEditorFrame(evidence);
            if(retry.Recovered&&!probeMode)
            {
                editorValidationEnabled=true;nextEditorCheck=Environment.TickCount64+400;
                needsReprime=true;verifiedControls.Clear();
                Log("editor_transient_recovered",new{retry.Captures,retry.Result,referenceChanged=false,retryCurrentStroke=true});
                // Release invalidated the in-flight stroke. Return to its
                // transaction retry; never just continue moving with mouse up.
                throw new InputInterrupted();
            }
            Log("editor_liveness_confirmation",new{first=evidence,second=retry.Result,autoResume=false});
            EditorUnavailable(retry.Result.State==EditorEvidenceState.Present?evidence:retry.Result);
        }
    }
}
