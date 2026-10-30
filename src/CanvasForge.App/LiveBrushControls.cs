using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;
internal sealed partial class MainWindow
{
    private void RefreshLiveBrushControls(nint target)
    {
        SetupToken.ThrowIfCancellationRequested();
        if(Native.GetForegroundWindow()!=target)throw new InvalidOperationException("Rust lost focus.");
        var origin=Native.ClientOrigin(target);var size=Native.ClientSize(target);
        int dpi=Native.DpiOf(target);uint process=Native.ProcessIdOf(target);
        var window=new ScreenRect(origin.X,origin.Y,origin.X+size.Width,origin.Y+size.Height);
        var old=settings.PaintCalibration();
        var cursor=Native.Cursor();
        var region=BrushControlsLocator.CaptureRegion(window,old);
        var park=(region.Valid?CaptureCursor.ParkingPoint(region,window,cursor,CaptureCursor.Clearance(0,dpi))
            :ControlCapture.ParkingPoint(old,window,cursor,dpi))
            ??throw new InvalidOperationException("Немає місця для знімка без курсора. Повтори захоплення полотна.");
        void CheckCapture()
        {
            SetupToken.ThrowIfCancellationRequested();
            if(Native.GetForegroundWindow()!=target||Native.ProcessIdOf(target)!=process||!Native.IsRust(target))
                throw new InvalidOperationException("Rust lost focus.");
            if(Native.DpiOf(target)!=dpi)throw new InvalidOperationException(CalibrationSession.DpiChangedMessage);
            if(Native.ClientOrigin(target)!=origin||Native.ClientSize(target)!=size)
                throw new InvalidOperationException("Вікно Rust змінило розмір або положення. Повтори захоплення.");
        }
        CheckCapture();
        var initial=EditorEvidence.Read(settings,EditorEvidence.Reference(settings),Native.Screenshot(window),window,origin,dpi,size);
        if(initial.State!=EditorEvidenceState.Present)
            throw new PreparationInterruptedException("Редактор Rust не підтверджено. Повтори захоплення областей.");
        if(park!=cursor)
        {
            CheckCapture();Native.ReleaseChecked();
            GuardedNativeInput.Move(InputCommandGuard.From(settings,window),[park],InputIntent.Parking,CheckCapture,()=>throw new PreparationInterruptedException(),Native.MovePath);Thread.Sleep((int)Math.Ceiling(StrokeTiming.CursorPark(settings)*1000));
        }
        CheckCapture();
        var image=Native.Screenshot(window);
        var editor=EditorEvidence.Read(settings,EditorEvidence.Reference(settings),image,window,origin,dpi,size);
        if(editor.State!=EditorEvidenceState.Present)throw new PreparationInterruptedException("Редактор Rust не підтверджено. Повтори захоплення областей.");
        var found=BrushControlsLocator.Find(image,window,old);
        // Existing valid captures remain supported, including custom UI skins.
        // Their usual strict layout check still refuses missing/ambiguous fields.
        if(found is null)return;
        bool Same(string key)=>old.Rect(key)==found.Rect(key);
        bool stable=new[]{"size_track"}.All(Same)
            &&Math.Abs(old.Rect("brush_shapes").Center.Y-found.Rect("brush_shapes").Center.Y)<=2
            &&old.Point("brush_tool")==found.Point("brush_tool");
        if(stable)return;
        CheckCapture();
        var relocated=EditorEvidence.RelocateControls(settings,found,image,window,origin,dpi,size);
        if(relocated.State!=EditorEvidenceState.Present)
            throw new PreparationInterruptedException("Редактор Rust не підтверджено. Повтори захоплення областей.");
        Dirty();Save();
        SessionLog.AppendJson(LogPath,JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,action="controls_relocated",details=new{
            mode=settings.Mode.ToString(),oldSize=old.Rect("size_track"),newSize=found.Rect("size_track"),
            oldShapes=old.Rect("brush_shapes"),newShapes=found.Rect("brush_shapes"),dpi=Native.DpiOf(target),
            oldFootprintsRetainedAsStale=true,editorReferenceRefreshed=true}})+Environment.NewLine);
    }
}
