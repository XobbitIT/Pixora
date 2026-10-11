using CanvasForge.Core;

namespace CanvasForge.App;
internal sealed partial class Painter
{
    private void ParkForControlCapture(Calibration calibration)
    {
        CheckBoundary();
        var original=Native.Cursor();
        var park=ControlCapture.ParkingPoint(calibration,windowRect,original,windowDpi)
            ??throw new InvalidOperationException("Немає місця для знімка без курсора. Повтори захоплення полотна.");
        if(park==original)return;
        Native.ReleaseChecked();MoveCursor(park);Delay(StrokeTiming.CursorPark(settings));
        Log("control_cursor_park",new{original,park,region=ControlCapture.Region(calibration,windowRect),
            dpi=windowDpi,transport="guarded SendInput",allNumericControls=true});
    }
}
