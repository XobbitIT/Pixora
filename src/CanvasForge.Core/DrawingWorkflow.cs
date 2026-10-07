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
        if(s.Bool("calibrated_strokes")&&!SpeedCalibration.Use(s))
        {
            // Keep the preference/evidence in the saved configuration. A stale
            // speed proof disables acceleration, not ordinary controlled input.
            s.Set("calibrated_strokes",false);s.Set("fast_transfer",false);s.Set("line_mode",false);
            s.Set("input_engine","Stable");s.Set("input_frame_delay_ms",Math.Max(20,s.Number("input_frame_delay_ms",20)));
        }
        return s;
    }
}
