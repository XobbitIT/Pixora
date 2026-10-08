namespace CanvasForge.Core;

// Normal defaults remain Size 3. Explicit Size 1 is available again; painting
// must have its own current solid proof before using it.
public static class DrawingWorkflow
{
    public static void Normalize(Settings s)
    {
        s.Data.Remove("drawing_mode");
        s.Data.Remove("manual_brush_controls");
        if(!s.Bool("adaptive_brush"))
        {
            if(s.Text("precision_brush_size","3")=="Profile"||s.Number("precision_brush_size",3)<1)s.Set("precision_brush_size","3");
            if(s.Number("brush_size_value",3)<1)s.Set("brush_size_value",3);
        }
    }
    public static Settings Effective(Settings source)
    {
        var s = source.Clone();
        Normalize(s);
        if(!s.Bool("adaptive_brush")&&!(s.Text("coverage_mode","Precision")=="Precision"&&s.Bool("force_precision_controls",true))&&s.Bool("auto_brush_size",true))
        {
            double size=Math.Max(3,AutomaticBrush.Value(s,Math.Max(1,s.Int("cell_px",3))));
            s.Set("auto_brush_size",false);s.Set("brush_size_value",size);
        }
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
