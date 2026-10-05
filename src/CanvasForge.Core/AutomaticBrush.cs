using System.Globalization;
using System.Text.Json.Nodes;

namespace CanvasForge.Core;

public static class AutomaticBrush
{
    public const string Revision = "outer-diameter-calibration-v2";

    public static double Value(Settings settings, double pixels)
    {
        if (!double.IsFinite(pixels) || pixels <= 0) throw new ArgumentOutOfRangeException(nameof(pixels));
        double fallback = Math.Clamp(pixels, 1, 100);
        var context = settings.Text("brush_calibration_context");
        if (context.Length > 0 && context != AdaptiveBrush.Context(settings)) return fallback;
        if (settings.Data["brush_calibration_points"] is not JsonArray points) return fallback;
        var samples = new List<(double Size, double Pixels)>();
        foreach (var row in points.OfType<JsonArray>())
        {
            if (row.Count is not (2 or 3)) continue;
            bool Number(int index, out double value) => double.TryParse(row[index]?.ToString(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
            if (!Number(0, out var size) || !Number(1, out var outer) || size is < 1 or > 100 || outer is < 1 or > 512) continue;
            if (row.Count == 3 && (!Number(2, out var inner) || inner < 1 || inner > outer)) continue;
            // Outer diameter controls boundary spill. Inner diameter describes solid fill,
            // and must not replace it when choosing a non-adaptive brush size.
            samples.Add((size, outer));
        }
        var ordered = samples.GroupBy(x => x.Pixels).Select(g => g.OrderBy(x => x.Size).First())
            .OrderBy(x => x.Pixels).ToArray();
        if (ordered.Length < 2) return fallback;
        if (ordered.Zip(ordered.Skip(1)).Any(pair => pair.Second.Size < pair.First.Size)) return fallback;
        if (pixels <= ordered[0].Pixels) return ordered[0].Size;
        for (int i = 1; i < ordered.Length; i++)
            if (pixels <= ordered[i].Pixels)
                return ordered[i - 1].Size + (ordered[i].Size - ordered[i - 1].Size)
                    * (pixels - ordered[i - 1].Pixels) / (ordered[i].Pixels - ordered[i - 1].Pixels);
        return ordered[^1].Size;
    }
}
