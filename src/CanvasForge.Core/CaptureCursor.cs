namespace CanvasForge.Core;

// Keep the brush preview outside the captured region, then return to the action
// that preceded the snapshot. All moves are performed after releasing input.
public static class CaptureCursor
{
    // The rendered pointer/halo can be larger than a tiny brush. Sizes are
    // physical capture pixels; scale only the pointer clearance with window DPI.
    public static int Clearance(int brushRadius,int dpi=96)
    {
        if(brushRadius is <0 or >512||dpi is <48 or >768)throw new ArgumentOutOfRangeException();
        return Math.Max((int)Math.Ceiling(96*dpi/96d),brushRadius+(int)Math.Ceiling(12*dpi/96d));
    }

    public static ScreenPoint? ParkingPoint(ScreenRect region,ScreenRect window,ScreenPoint current,int margin)
    {
        if(!region.Valid||!window.Valid||margin<1||margin>1024)throw new ArgumentException("Invalid cursor capture region.");
        bool Inside(ScreenPoint p)=>p.X>=window.Left&&p.X<window.Right&&p.Y>=window.Top&&p.Y<window.Bottom;
        bool Clear(ScreenPoint p)=>p.X<=region.Left-margin||p.X>=region.Right+margin
            ||p.Y<=region.Top-margin||p.Y>=region.Bottom+margin;
        if(Inside(current)&&Clear(current))return current;
        int x=Math.Clamp(current.X,window.Left,window.Right-1),y=Math.Clamp(current.Y,window.Top,window.Bottom-1);
        ScreenPoint[] candidates=[new(region.Left-margin,y),new(region.Right+margin,y),
            new(x,region.Top-margin),new(x,region.Bottom+margin)];
        return candidates.Where(p=>Inside(p)&&Clear(p))
            .OrderBy(p=>Math.Pow((double)p.X-current.X,2)+Math.Pow((double)p.Y-current.Y,2))
            .Select(p=>(ScreenPoint?)p).FirstOrDefault();
    }

    public static T Snapshot<T>(ScreenPoint original,ScreenPoint parked,Action release,
        Action<ScreenPoint> move,Func<T> capture,Func<bool> mayReturn,Action<Exception>? restoreFailed=null,bool returnToOriginal=true)
    {
        if(original==parked)return capture();
        try
        {
            release();move(parked);
            return capture();
        }
        finally {if(returnToOriginal)Return(original,release,move,mayReturn,restoreFailed);}
    }

    // Returning a cursor must never mask a capture failure or steal it after an
    // interruption. The owner supplies its focus, cancellation and geometry checks.
    public static bool Return(ScreenPoint original,Action release,Action<ScreenPoint> move,
        Func<bool> mayReturn,Action<Exception>? restoreFailed=null)
    {
        try
        {
            if(!mayReturn())return false;
            release();move(original);return true;
        }
        catch(Exception error)
        {
            try{restoreFailed?.Invoke(error);}catch{}
            return false;
        }
    }
}
