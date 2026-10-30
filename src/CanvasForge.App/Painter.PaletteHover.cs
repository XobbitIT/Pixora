using CanvasForge.Core;

namespace CanvasForge.App;

internal sealed partial class Painter
{
    private PaletteHoverTransition? paletteHover;
    private PaletteHoverTransition PaletteHover=>paletteHover??=new(()=>System.Diagnostics.Stopwatch.GetTimestamp()/(double)System.Diagnostics.Stopwatch.Frequency);
    private void SelectPaletteTarget(ScreenPoint point,int attempt=0)
    {
        CheckBoundary();
        var origin=Native.ClientOrigin(window);var reference=EditorEvidence.Reference(settings);
        var stored=reference?.QuietParkingPoint is {} local?new ScreenPoint(local.X+origin.X,local.Y+origin.Y):(ScreenPoint?)null;
        var parked=PaletteCursorParking.Find(settings,windowRect,origin,windowDpi,stored??ReadCursor())
            ??throw new InvalidOperationException("Немає місця для знімка без курсора. Повтори захоплення полотна.");
        if(stored is {} expected&&parked!=expected)
            throw new InvalidOperationException("Немає місця для знімка без курсора. Повтори захоплення полотна.");
        EditorEvidenceResult? last=null;int captures=0;
        try{PaletteHover.Select(RequireEditorProof,()=>Click(point,waitForReady:false),()=>MoveCursor(parked),Delay,
            ()=>{CheckBoundary();last=ReadEditor(false);CheckBoundary();captures++;return last.State==EditorEvidenceState.Present;},
            ()=>EditorUnavailable(last??new(EditorEvidenceState.Missing,"editor_reference_missing",[])),Native.ReleaseChecked);}
        catch(PaletteHoverTransition.Expired)
        {
            Native.Release();Paused=true;pauseReason="palette_hover_timeout";editorValidationEnabled=false;
            Log("input_pause",new{reason=pauseReason,point,parked,captures,referenceChanged=false});throw new InputInterrupted();
        }
        editorValidationEnabled=true;nextEditorCheck=Environment.TickCount64+400;
        // The configured color wait runs with the normal heartbeat restored.
        Delay(Math.Max(0,ColorDelay()+attempt*.10-captures*PaletteHoverTransition.SettleSeconds));
        Log("palette_hover_cleared",new{point,parked,captures,storedPoint=stored.HasValue,editorProved=true,referenceChanged=false});
    }
}
