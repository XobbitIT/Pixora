namespace CanvasForge.Core;

public static class PaletteCursorParking
{
    // A mouse-up move outside captured controls, Canvas and evidence patches.
    // The caller must still check foreground/window ownership of the destination.
    public static ScreenPoint? Find(Settings settings,ScreenRect window,ScreenPoint origin,int dpi,ScreenPoint current,EditorReference? captured=null)
    {
        var c=settings.PaintCalibration();
        var regions=new[]{"canvas","palette","quick","brush_shapes","tool_row","topbar","top_bar",
            "save_cancel","clan_logo","hex","swatch","palette_swatch","size_track","size_value_field"}
            .Select(c.Rect).Where(r=>r.Valid).ToList();
        foreach(string key in new[]{"brush_tool","hard_brush","square_brush"})
            if(c.Point(key) is {} p)regions.Add(new(p.X-4,p.Y-4,p.X+5,p.Y+5));
        if(c.HexPoint is {} hex)regions.Add(new(hex.X-4,hex.Y-4,hex.X+5,hex.Y+5));
        var reference=captured??EditorEvidence.Reference(settings);
        ScreenRect Screen(ScreenRect r)=>new(r.Left+origin.X,r.Top+origin.Y,r.Right+origin.X,r.Bottom+origin.Y);
        if(reference?.Structure is {} structure)
        {
            regions.AddRange(structure.Patches.Select(p=>Screen(p.Rect)));
            regions.AddRange(structure.Exclusions.Select(Screen));
        }
        if(reference?.Tool is {} tool)regions.Add(Screen(tool.Row));
        if(reference is not null)regions.AddRange(reference.Pixels.Select(p=>new ScreenRect(p.X+origin.X,p.Y+origin.Y,p.X+origin.X+1,p.Y+origin.Y+1)));
        return Find(window,regions,CaptureCursor.Clearance(0,dpi),current);
    }
    public static ScreenPoint? Find(ScreenRect window,IReadOnlyList<ScreenRect> excluded,int clearance,ScreenPoint current)
    {
        if(!window.Valid||clearance<1)throw new ArgumentException("Invalid parking geometry.");
        bool Clear(ScreenPoint p)=>p.X>=window.Left+8&&p.X<window.Right-8&&p.Y>=window.Top+8&&p.Y<window.Bottom-8
            &&excluded.All(r=>!r.Valid||p.X<r.Left-clearance||p.X>=r.Right+clearance||p.Y<r.Top-clearance||p.Y>=r.Bottom+clearance);
        if(Clear(current))return current;
        var candidates=new List<ScreenPoint>();
        for(int y=window.Top+16;y<window.Bottom-8;y+=32)
            for(int x=window.Left+16;x<window.Right-8;x+=32)
                if(Clear(new(x,y)))candidates.Add(new(x,y));
        return candidates.OrderBy(p=>Math.Abs((long)p.X-current.X)+Math.Abs((long)p.Y-current.Y))
            .Select(p=>(ScreenPoint?)p).FirstOrDefault();
    }
}
