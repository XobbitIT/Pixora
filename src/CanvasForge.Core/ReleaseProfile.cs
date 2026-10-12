namespace CanvasForge.Core;

// Standard deliberately uses the existing ordinary stroke route. It does not
// authorize a measured footprint, accelerated route or exact-coverage claim.
public static class ReleaseProfile
{
    public const string Revision="standard3-experimental-v1";
    public static bool Standard(Settings s)=>s.Text("release_profile","Standard")=="Standard";
    public static Settings Experimental(Settings source)
    {var s=source.Clone();s.Set("release_profile","Experimental");return s;}
    public static void ApplyStandard(Settings s)
    {
        s.Set("release_profile","Standard");
        foreach(var key in new[]{"adaptive_brush","adaptive_auto_shape","calibrated_strokes","fast_transfer","line_mode","coverage_audit","audit_repair","edge_merge","background_fill","auto_brush_size"})s.Set(key,false);
        s.Set("brush_shape_slot",3);s.Set("brush_shape","Round");s.Set("precision_brush_size","3");s.Set("brush_size_value",3);
        s.Set("coverage_mode","Precision");s.Set("force_precision_controls",true);s.Set("use_fixed_opacity",true);s.Set("paint_opacity_value",1);
        s.Set("interval_value",.01);s.Set("input_engine","Stable");s.Set("control_confirmation","Visual");
        s.Set("input_frame_delay_ms",Math.Max(20,s.Number("input_frame_delay_ms",20)));
        // These archived proofs are irrelevant to Standard plan identity. Keep
        // the saved originals so Experimental can still display their evidence.
        foreach(var key in s.Data.Select(p=>p.Key).Where(k=>k.StartsWith("brush_calibration_")||k.StartsWith("probe_")||k.StartsWith("speed_calibration")||k.StartsWith("brush_signal")).ToArray())s.Data.Remove(key);
        foreach(var key in new[]{"brush_footprints","visual_brush_check_pending","preparation_history","shape_speed_profiles","shape_spatial_profiles","speed_probe_profile","probe_spatial_models","probe_spatial_profile","input_experimental_delay_ms","adaptive_max_size","edge_merge_thickness","fast_path_batch_points","audit_repair_passes"})s.Data.Remove(key);
    }
}

public class PreparationInterruptedException(string message="Preparation paused. Return to Rust and continue; incomplete measurements are discarded.") : Exception(message);
