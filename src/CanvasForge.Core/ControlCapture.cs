namespace CanvasForge.Core;

// A cursor-free numeric read must exclude every neighbouring control too.
// Parking above Opacity alone can put the rendered cursor over Interval.
public static class ControlCapture
{
    public static ScreenRect Bounds(ScreenRect track,ScreenRect window)
        =>new(Math.Max(window.Left,track.Left-Math.Max(48,track.Width/4)),
            Math.Max(window.Top,track.Top-12),
            Math.Min(window.Right,track.Right+Math.Max(120,track.Width/2)),
            Math.Min(window.Bottom,track.Bottom+12));

    public static ScreenRect Region(Calibration calibration,ScreenRect window)
    {
        var regions=new[]{"size","interval","opacity"}.Select(k=>calibration.Rect(k+"_track"))
            .Where(r=>r.Valid).Select(r=>Bounds(r,window)).Where(r=>r.Valid).ToArray();
        return regions.Length==0?default:new(regions.Min(r=>r.Left),regions.Min(r=>r.Top),
            regions.Max(r=>r.Right),regions.Max(r=>r.Bottom));
    }

    public static ScreenPoint? ParkingPoint(Calibration calibration,ScreenRect window,ScreenPoint current,int dpi)
    {
        var region=Region(calibration,window);
        return region.Valid?CaptureCursor.ParkingPoint(region,window,current,CaptureCursor.Clearance(0,dpi)):current;
    }
}
