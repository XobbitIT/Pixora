namespace CanvasForge.Core;

public sealed record BrushToolReference(ScreenRect Row,ScreenPoint Brush);
public sealed record BrushToolResult(bool Safe,string Reason);

public static class BrushToolState
{
    private static ScreenRect Local(ScreenRect r,ScreenPoint o)=>new(r.Left-o.X,r.Top-o.Y,r.Right-o.X,r.Bottom-o.Y);
    public static BrushToolReference Capture(PixelImage frame,ScreenRect screen,ScreenRect shapes,ScreenPoint origin)
    {
        var tool=BrushToolLocator.Find(frame,screen,shapes);
        if(tool is null)throw new InvalidDataException("Не вдалося підтвердити ряд інструментів Rust. Відкрий панель без накладок і повтори захоплення.");
        if(tool.SelectedSlot!=0)throw new InvalidDataException("Вибери інструмент пензля в Rust перед захопленням полотна.");
        return new(Local(tool.Row,origin),new(tool.Brush.X-origin.X,tool.Brush.Y-origin.Y));
    }
    public static ScreenRect CaptureRegion(BrushToolReference reference,ScreenRect shapes,ScreenPoint origin)
        =>new(shapes.Left,reference.Row.Top+origin.Y-8,shapes.Right,reference.Row.Bottom+origin.Y+8);
    public static BrushToolResult Read(BrushToolReference? reference,PixelImage frame,ScreenRect screen,ScreenRect shapes,ScreenPoint origin)
    {
        if(reference is null)return new(false,"tool_reference_missing");
        var found=BrushToolLocator.Find(frame,screen,shapes);
        if(found is null)return new(false,"tool_state_unavailable");
        if(found.SelectedSlot!=0)return new(false,"tool_state_unsafe");
        if(Local(found.Row,origin)!=reference.Row||new ScreenPoint(found.Brush.X-origin.X,found.Brush.Y-origin.Y)!=reference.Brush)
            return new(false,"tool_geometry_changed");
        return new(true,"tool_brush_confirmed");
    }
}
