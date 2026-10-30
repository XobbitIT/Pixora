namespace CanvasForge.Core;

public enum InputIntent { Paint, UserInterface, Parking }

// Validate the whole packet before emitting anything. Reach never authorizes
// a mouse-down outside Canvas; footprint containment belongs to the planner.
public sealed class InputCommandGuard(ScreenRect canvas,ScreenRect window,
    IReadOnlyList<ScreenRect> controls,IReadOnlyList<ScreenRect>? occlusions=null)
{
    private static bool Inside(ScreenPoint p,ScreenRect r)=>r.Valid&&p.X>=r.Left&&p.X<r.Right&&p.Y>=r.Top&&p.Y<r.Bottom;
    public bool Allows(ScreenPoint point,InputIntent intent=InputIntent.Paint,bool down=false)
    {
        if(!Inside(point,window))return false;
        return intent switch
        {
            InputIntent.Paint=>Inside(point,canvas)&&!controls.Any(r=>Inside(point,r))
                &&!(occlusions?.Any(r=>Inside(point,r))??false),
            InputIntent.UserInterface=>controls.Any(r=>Inside(point,r)),
            InputIntent.Parking=>!down,
            _=>false
        };
    }
    public bool Allows(IReadOnlyList<ScreenPoint> points,InputIntent intent=InputIntent.Paint)
        =>points.Count>0&&points.All(p=>Allows(p,intent));
    public bool Allows(ScreenLine line,InputIntent intent=InputIntent.Paint)
    {
        if(line.X1!=line.X2&&line.Y1!=line.Y2)return false;
        int dx=Math.Sign(line.X2-line.X1),dy=Math.Sign(line.Y2-line.Y1);
        for(int k=0;k<=TransferSchedule.Length(line);k++)
            if(!Allows(new ScreenPoint(line.X1+k*dx,line.Y1+k*dy),intent))return false;
        return true;
    }
    public static InputCommandGuard From(Settings settings,ScreenRect window)
    {
        var c=settings.PaintCalibration();
        var regions=new[]{"palette","quick","brush_shapes","hex","size_track","size_value_field",
            "tool_row","topbar","top_bar","save_cancel","clan_logo"}
            .Select(c.Rect).Where(r=>r.Valid).ToList();
        foreach(string key in new[]{"brush_tool","hard_brush","square_brush"})
            if(c.Point(key) is {} p)regions.Add(new(p.X-4,p.Y-4,p.X+5,p.Y+5));
        // HEX can be stored as a point rather than a rectangle.
        if(c.HexPoint is {} hex)regions.Add(new(hex.X-4,hex.Y-4,hex.X+5,hex.Y+5));
        return new(c.Rect("canvas"),window,regions);
    }
}
