namespace CanvasForge.Core;

// Simple painting uses the brush the user selected in Rust. Measured automation
// remains an explicit advanced workflow, with its original acceptance criteria.
public static class DrawingWorkflow
{
    public static bool Simple(Settings s) => s.Text("drawing_mode", "Simple") != "Advanced";

    public static Settings Effective(Settings source)
    {
        var s = source.Clone();
        s.Data.Remove("manual_brush_controls");
        if (!Simple(source)) return s;
        s.Set("manual_brush_controls", true);
        foreach (string key in new[] { "adaptive_brush", "adaptive_auto_shape", "calibrated_strokes", "coverage_audit", "audit_repair",
            "fast_transfer", "line_mode", "background_fill", "force_precision_controls", "auto_brush_size", "auto_tools", "auto_insert_preview" })
            s.Set(key, false);
        s.Set("coverage_mode", "Precision"); s.Set("coverage_pitch", 1);
        s.Set("input_engine", "Stable"); s.Set("input_frame_delay_ms", 25);
        s.Set("speed_profile", "Rapid"); s.Set("brush_shape", "Square"); s.Set("brush_shape_slot", 4);
        s.Set("brush_size_value", 1); s.Set("interval_value", .01);
        s.Set("paint_opacity_value", 1); s.Set("use_fixed_opacity", true);
        s.Set("hex_verify", true); s.Set("hex_readback_every", 1);
        s.Set("fit_mode", "fit whole");
        return s;
    }

    public static string? SimpleSetupProblem(Settings s)
    {
        var cal = s.Calibration;
        if (!cal.Rect("canvas").Valid) return "Захопи полотно.";
        if (cal.SessionClient is null || cal.SessionSize is null) return "Повтори налаштування Rust для поточного вікна.";
        if (s.Mode == ColorMode.HexDirect)
            return cal.HexPoint is null ? "Захопи поле HEX." : null;
        if (!cal.Rect("palette").Valid || s.Palette().Count == 0) return "Захопи палітру Rust 4×16.";
        if (s.Palette().Any(e => e.ClickPoint is not { } p || !(Inside(cal.Rect("palette"), p) || Inside(cal.Rect("quick"), p))))
            return "Повтори захоплення палітри Rust 4×16.";
        return null;
    }

    private static bool Inside(ScreenRect r, ScreenPoint p) => p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;
}
