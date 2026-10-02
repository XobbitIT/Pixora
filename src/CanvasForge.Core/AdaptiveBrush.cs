using System.Text.Json.Nodes;

namespace CanvasForge.Core;
public readonly record struct BrushStroke(ScreenLine Line, double Size, int OuterRadius = 0, int FillRadius = 0);

public static class AdaptiveBrush
{
    public static string Context(Settings s)
    {
        var r = s.Calibration.Rect("canvas");
        return $"v1:{r.Width}:{r.Height}:{s.Text("color_mode")}:{s.Int("brush_shape_slot", 3)}:{s.Text("brush_shape")}:" + s.PaintCalibration().Rect("size_track").ToString();
    }

    public static void Validate(Settings s)
    {
        if (!s.Bool("adaptive_brush")) return;
        if (s.Text("coverage_mode", "Precision") != "Precision" || !s.Bool("force_precision_controls", true)
            || s.Bool("line_mode") || s.Bool("background_fill") || (s.Bool("use_fixed_opacity", true) && s.Number("paint_opacity_value", 1) < .999))
            throw new InvalidOperationException("Adaptive brush requires Precision, precision controls, opacity 1, and Shift-line/background fill disabled.");
        if (s.Int("brush_shape_slot", 3) is not (3 or 4))
            throw new InvalidOperationException("Adaptive brush requires solid round (slot 3) or square (slot 4).");
        if (s.Text("brush_calibration_context") != Context(s) || Samples(s).Length < 2)
            throw new InvalidOperationException("Calibrate the brush for the current Canvas, color mode and brush shape before enabling adaptive painting.");
        var r = s.Calibration.Rect("canvas");
        if (!r.Valid || (long)r.Width * r.Height > 16_000_000)
            throw new InvalidOperationException("Adaptive brush supports Canvas areas up to 16 million pixels.");
    }

    private static (double Size, int Outer, int Inner)[] Samples(Settings s)
    {
        if (s.Data["brush_calibration_points"] is not JsonArray a) return [];
        var result = new List<(double Size, int Outer, int Inner)>();
        foreach (var row in a.OfType<JsonArray>())
        {
            if (row.Count < 3) continue;
            var size = row[0]!.GetValue<double>();
            var outer = row[1]!.GetValue<double>();
            var inner = row[2]!.GetValue<double>();
            if (!double.IsFinite(size) || !double.IsFinite(outer) || !double.IsFinite(inner)
                || size < 1 || size > 100 || outer < 1 || outer > 512 || inner < 1 || inner > outer) continue;
            result.Add((size, (int)Math.Ceiling(outer / 2) + 2, Math.Max(0, (int)Math.Floor((inner - 1) / 2) - 1)));
        }
        return result.OrderByDescending(x => x.Item2).ThenByDescending(x => x.Item1).ToArray();
    }

    public static Dictionary<int, List<BrushStroke>> Build(PaintPlan plan, Settings s)
    {
        var basic = Coverage.Build(plan, s);
        var output = basic.ToDictionary(x => x.Key, x => x.Value.Select(l => new BrushStroke(l, 0)).ToList());
        if (!s.Bool("adaptive_brush")) return output;
        Validate(s);
        var rect = s.Calibration.Rect("canvas");
        int w = rect.Width, h = rect.Height;
        var labels = new int[checked(w * h)];
        Array.Fill(labels, -1);
        var xb = Coverage.Partition(rect.Left, rect.Right, plan.Width);
        var yb = Coverage.Partition(rect.Top, rect.Bottom, plan.Height);
        for (int y = 0; y < plan.Height; y++)
            for (int x = 0; x < plan.Width; x++)
                for (int py = yb[y] - rect.Top; py < yb[y + 1] - rect.Top; py++)
                    Array.Fill(labels, plan.Indices[y * plan.Width + x], py * w + xb[x] - rect.Left, xb[x + 1] - xb[x]);
        // Chebyshev distance to a different color, transparency or Canvas edge.
        var dist = new int[labels.Length];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            int i = y * w + x;
            bool boundary = x == 0 || y == 0 || x == w - 1 || y == h - 1 || labels[i] < 0;
            if (!boundary)
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    boundary |= labels[i + dy * w + dx] != labels[i];
            dist[i] = boundary ? 0 : Math.Min(w, h);
        }
        for (int y = 1; y < h - 1; y++) for (int x = 1; x < w - 1; x++)
        { int i = y * w + x; dist[i] = Math.Min(dist[i], 1 + Math.Min(dist[i - 1], Math.Min(dist[i - w], Math.Min(dist[i - w - 1], dist[i - w + 1])))); }
        for (int y = h - 2; y > 0; y--) for (int x = w - 2; x > 0; x--)
        { int i = y * w + x; dist[i] = Math.Min(dist[i], 1 + Math.Min(dist[i + 1], Math.Min(dist[i + w], Math.Min(dist[i + w - 1], dist[i + w + 1])))); }
        var covered = new bool[labels.Length];
        var large = basic.ToDictionary(x => x.Key, _ => new List<BrushStroke>());
        foreach (var brush in Samples(s).Where(x => x.Inner >= 2))
            for (int y = brush.Outer + 1; y < h - brush.Outer - 1; y += 2 * brush.Inner + 1)
            {
                int x = brush.Outer + 1;
                while (x < w - brush.Outer - 1)
                {
                    int start = x, color = labels[y * w + x];
                    if (color < 0 || !large.ContainsKey(color) || dist[y * w + x] <= brush.Outer || covered[y * w + x]) { x++; continue; }
                    while (x + 1 < w - brush.Outer - 1 && labels[y * w + x + 1] == color && dist[y * w + x + 1] > brush.Outer && !covered[y * w + x + 1]) x++;
                    int end = x++;
                    if (end - start < 2 * brush.Outer) continue;
                    large[color].Add(new(new(rect.Left + start, rect.Top + y, rect.Left + end, rect.Top + y), brush.Size, brush.Outer, brush.Inner));
                    for (int py = y - brush.Inner; py <= y + brush.Inner; py++)
                        Array.Fill(covered, true, py * w + start - brush.Inner, end - start + 2 * brush.Inner + 1);
                }
            }
        var speed = SpeedProfile.Get(s.Text("speed_profile", "Rapid"));
        double Cost(IEnumerable<BrushStroke> strokes)
        {
            double cost = 0, previous = 0;
            foreach (var op in strokes)
            {
                if (op.Size != previous) cost += StrokeTiming.SliderChangeEstimate(s); // measured control-change estimate
                previous = op.Size;
                cost += StrokeTiming.Estimate(s, speed, Math.Max(Math.Abs(op.Line.X2 - op.Line.X1), Math.Abs(op.Line.Y2 - op.Line.Y1)), false);
            }
            return cost;
        }
        foreach (var (color, lines) in basic)
        {
            if (large[color].Count == 0) continue;
            var fine = new List<ScreenLine>();
            foreach (var l in lines)
            {
                int dx = Math.Sign(l.X2 - l.X1), dy = Math.Sign(l.Y2 - l.Y1);
                int length = Math.Max(Math.Abs(l.X2 - l.X1), Math.Abs(l.Y2 - l.Y1)), begin = -1;
                for (int k = 0; k <= length + 1; k++)
                {
                    bool keep = k <= length && !covered[(l.Y1 + k * dy - rect.Top) * w + l.X1 + k * dx - rect.Left];
                    if (keep && begin < 0) begin = k;
                    if (!keep && begin >= 0)
                    { fine.Add(new(l.X1 + begin * dx, l.Y1 + begin * dy, l.X1 + (k - 1) * dx, l.Y1 + (k - 1) * dy)); begin = -1; }
                }
            }
            // Regroup the residual center pixels into long horizontal/vertical runs.
            // This preserves their exact set, including sparse precision pitches.
            var pending = new bool[labels.Length];
            foreach (var l in fine)
            {
                int length = Math.Max(Math.Abs(l.X2-l.X1), Math.Abs(l.Y2-l.Y1));
                for (int k=0;k<=length;k++) pending[(l.Y1 + k*Math.Sign(l.Y2-l.Y1)-rect.Top)*w+l.X1+k*Math.Sign(l.X2-l.X1)-rect.Left] = true;
            }
            var regrouped = new List<ScreenLine>();
            for (int y=0;y<h;y++) for (int x=0;x<w;x++)
            {
                if (!pending[y*w+x]) continue;
                int right=x, bottom=y;
                while (right+1<w && pending[y*w+right+1]) right++;
                while (bottom+1<h && pending[(bottom+1)*w+x]) bottom++;
                if (bottom-y > right-x)
                {
                    regrouped.Add(new(rect.Left+x,rect.Top+y,rect.Left+x,rect.Top+bottom));
                    for(int yy=y;yy<=bottom;yy++) pending[yy*w+x]=false;
                }
                else
                {
                    regrouped.Add(new(rect.Left+x,rect.Top+y,rect.Left+right,rect.Top+y));
                    Array.Fill(pending,false,y*w+x,right-x+1);
                }
            }
            var proposed = large[color].Concat(regrouped.Select(l => new BrushStroke(l, 0))).ToList();
            if (Cost(proposed) < Cost(output[color])) output[color] = proposed;
        }
        return output;
    }
}
