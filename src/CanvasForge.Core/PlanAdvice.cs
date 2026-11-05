namespace CanvasForge.Core;

// Antialiased edges of flat images (logos, emblems) quantize into many small
// in-between colors. Each becomes a separate color group of short strokes that
// is painted after the large fills and overlaps their borders with Size 3.
public static class PlanAdvice
{
    public const double ThinShare = 0.015;
    public const int MinimumThinColors = 3;

    // Colors that cover less than ThinShare of the painted cells.
    public static (int Count, double Share) ThinColors(PaintPlan plan)
    {
        long total = plan.Counts.Values.Sum(x => (long)x);
        if (total == 0) return (0, 0);
        var thin = plan.Counts.Where(p => p.Value < total * ThinShare).ToArray();
        return (thin.Length, thin.Sum(p => (double)p.Value) / total);
    }

    public static bool SuggestEdgeMerge(PaintPlan plan, Settings settings)
        => plan.ExperimentalTexel is null && !settings.Bool("edge_merge") && ThinColors(plan).Count >= MinimumThinColors;
}
