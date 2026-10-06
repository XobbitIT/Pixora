namespace CanvasForge.Core;

// Remove the obsolete beta.32 mode switch without rewriting painting preferences.
public static class DrawingWorkflow
{
    public static void Normalize(Settings s)
    {
        s.Data.Remove("drawing_mode");
        s.Data.Remove("manual_brush_controls");
    }
    public static Settings Effective(Settings source)
    {
        var s = source.Clone();
        Normalize(s);
        return s;
    }
}
