namespace CanvasForge.Core;

// Keep historical measurements readable, but never select Size 1 for painting.
public static class DrawingWorkflow
{
    public const double MinimumSize=2;
    public const string Revision="minimum-painting-size-2-v1";
    public static IReadOnlyList<BrushFootprint> PaintingProfiles(Settings s,bool allShapes=false)
        =>BrushFootprints.Read(s,allShapes).Where(p=>p.Size>=MinimumSize).ToArray();
    public static IReadOnlyList<BrushFootprint> RepairProfiles(Settings s)
        =>PaintingProfiles(s,s.Bool("adaptive_auto_shape"))
            .Where(p=>p.SolidCore.Valid&&p.Possible.Sum(r=>r.Right-r.Left)<=4096)
            .GroupBy(p=>p.ShapeSlot).Select(g=>g.OrderBy(p=>p.Size).First()).ToArray();
    public static void Normalize(Settings s)
    {
        s.Data.Remove("drawing_mode");
        s.Data.Remove("manual_brush_controls");
        foreach(var key in new[]{"precision_brush_size","probe_size","adaptive_max_size"})
            if(s.Number(key,3)==1)s.Set(key,key=="precision_brush_size"?"2":(object)MinimumSize);
        if(s.Number("brush_size_value",3) is >0 and <MinimumSize)s.Set("brush_size_value",MinimumSize);
        if(s.Number("cell_px",3)==1)s.Set("cell_px",2);
        foreach(var key in new[]{"brush_calibration_size","probe_sizes"})
        {
            var fields=s.Text(key).Split('/');
            bool Removed(string field)=>double.TryParse(field,System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,out var size)&&size==1;
            if(fields.Any(Removed))s.Set(key,string.Join('/',fields.Where(x=>!Removed(x)).DefaultIfEmpty("2")));
        }
        if(!s.Bool("adaptive_brush"))
        {
            if(s.Text("precision_brush_size","3")=="Profile"||s.Number("precision_brush_size",3)<MinimumSize)s.Set("precision_brush_size","3");
            if(s.Number("brush_size_value",3)<MinimumSize)s.Set("brush_size_value",3);
        }
    }
    public static Settings Effective(Settings source)
    {
        var s = source.Clone();
        Normalize(s);
        if(ReleaseProfile.Standard(s)){ReleaseProfile.ApplyStandard(s);return s;}
        if(!s.Bool("adaptive_brush")&&!(s.Text("coverage_mode","Precision")=="Precision"&&s.Bool("force_precision_controls",true))&&s.Bool("auto_brush_size",true))
        {
            double size=Math.Max(MinimumSize,AutomaticBrush.Value(s,Math.Max(2,s.Int("cell_px",3))));
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
