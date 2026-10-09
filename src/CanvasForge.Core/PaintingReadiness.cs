namespace CanvasForge.Core;

public enum PreparationState { Ready, NeedsAction, Error, Stale }
public sealed record PreparationStep(string Key, PreparationState State, string? Problem = null);
public sealed record PaintingPreparation(IReadOnlyList<PreparationStep> Steps)
{
    public bool CanPaint => Steps.All(s => s.State == PreparationState.Ready);
    public PreparationStep? FirstIncomplete => Steps.FirstOrDefault(s => s.State != PreparationState.Ready);
}

// All required indicators and START share this model. Speed and coverage are
// separate optional capabilities; neither substitutes for a physical imprint.
public static class PaintingReadiness
{
    public static string? BrushProblem(Settings s)
    {
        if (CalibrationReliability.PaintingProblem(s) is { } problem) return problem;
        if (!BrushFootprints.Read(s, s.Bool("adaptive_brush") && s.Bool("adaptive_auto_shape"))
            .Any(p => p.Size == PaintTimingPlan.DefaultSize(s) && p.SolidCore.Valid))
            return "Пензель не підтверджено. Натисни «Підготувати до малювання» або повтори вимірювання пензля.";
        return null;
    }

    public static PaintingPreparation Read(Settings s, bool hasImage)
    {
        bool layoutReady=s.Mode!=ColorMode.HexDirect||s.HexControlsReady;
        var c = layoutReady?s.PaintCalibration():s.Calibration;
        bool canvas = c.Rect("canvas").Valid;
        bool colors = s.Mode == ColorMode.HexDirect ? c.HexReady && s.HexControlsReady
            : !s.Bool("palette_target_failed") && c.Rect("palette").Valid && s.Palette().Count > 0 && s.Palette().All(p => p.ClickPoint is not null);
        bool controls = layoutReady && !s.Bool("controls_validation_failed") && new[] { "size", "interval", "opacity" }.All(k => c.Rect(k + "_track").Valid && c.Rect(k + "_value_field").Valid)
            && (s.Mode == ColorMode.HexDirect || c.Point("brush_tool") is not null)
            && (c.Rect("brush_shapes").Valid || c.Point(s.Text("brush_shape") == "Square" ? "square_brush" : "hard_brush") is not null);
        double size = PaintTimingPlan.DefaultSize(s);
        string? brush = BrushProblem(s);
        var signals = layoutReady?BrushSignalDiagnostics.Read(s).Where(p => p.Size == size && p.Context == BrushFootprints.Context(s, p.ShapeSlot)):[];
        var brushState = brush is null ? PreparationState.Ready
            : signals.Any(p => p.State != BrushSignalState.Verified) ? PreparationState.Error
            : s.Data.ContainsKey("brush_footprints") ? PreparationState.Stale : PreparationState.NeedsAction;
        return new(new[]
        {
            new PreparationStep("image", hasImage ? PreparationState.Ready : PreparationState.NeedsAction, hasImage ? null : "Відкрий зображення."),
            new PreparationStep("capture", canvas ? PreparationState.Ready : PreparationState.NeedsAction, canvas ? null : "Захопи полотно."),
            new PreparationStep("colors", colors ? PreparationState.Ready : PreparationState.NeedsAction, colors ? null : "Захопи палітру або перевір HEX."),
            new PreparationStep("controls", controls ? PreparationState.Ready : PreparationState.NeedsAction, controls ? null : "Захопи пензель і три числові поля Rust."),
            new PreparationStep("brush", brushState, brush)
        });
    }
}
