using System.Text.Json.Nodes;

namespace CanvasForge.Core;
public readonly record struct BrushStroke(ScreenLine Line, double Size, int OuterRadius = 0, int FillRadius = 0, int ShapeSlot = 0, string? ProfileId = null);

public static class AdaptiveBrush
{
    public static string Context(Settings s)
    {
        var r = s.Calibration.Rect("canvas");
        var track = s.PaintCalibration().Rect("size_track");
        return $"v2:{r.Width}:{r.Height}:{s.Text("color_mode")}:{s.Int("brush_shape_slot", 3)}:{s.Text("brush_shape")}:{track.Width}:{track.Height}";
    }

    public static string? SetupProblem(Settings s)
    {
        var r = s.Calibration.Rect("canvas");
        if (!r.Valid || r.Width < 240 || r.Height < 240) return "Захопи Canvas від 240×240 px.";
        if (s.Mode == ColorMode.HexDirect && !s.HexControlsReady) return "Захопи пензель і повзунки HEX.";
        var cal = s.PaintCalibration();
        if (cal.SessionClient is null || cal.SessionSize is null) return "Повтори налаштування Rust для поточного вікна.";
        if (new[] { "size_track", "interval_track", "opacity_track" }.Any(k => !cal.Rect(k).Valid))
            return "Захопи Size, Interval та Opacity разом із числами справа.";
        if (cal.Point("brush_tool") is null) return "Захопи інструмент пензля через налаштування Rust.";
        if (!cal.Rect("brush_shapes").Valid && cal.Point(s.Text("brush_shape", "Round") == "Square" ? "square_brush" : "hard_brush") is null)
            return "Захопи ряд форм пензля через налаштування Rust.";
        if (s.Mode == ColorMode.RustPalette && s.Palette().Count == 0) return "Захопи палітру Rust.";
        if (s.Mode == ColorMode.HexDirect && !s.Calibration.HexReady) return "Захопи й перевір поле HEX.";
        if (s.Int("brush_shape_slot",3) is <1 or >7) return "Вибери форму пензля від 1 до 7.";
        if(s.Int("brush_shape_slot",3) is not (3 or 4)&&!cal.Rect("brush_shapes").Valid) return "Захопи ряд із семи форм пензля.";
        return null;
    }

    public static bool CalibrationCurrent(Settings s) => (s.Mode != ColorMode.HexDirect || s.HexControlsReady)
        && (s.Data["brush_footprints"] is not null?BrushFootprints.Read(s).Any(p=>p.Size==1&&p.SolidCore.Valid)
            :s.Int("brush_shape_slot",3) is 3 or 4&&s.Text("brush_calibration_context") == Context(s) && Samples(s).Length >= 2);

    public static void Prepare(Settings s)
    {
        s.Set("coverage_mode", "Precision"); s.Set("force_precision_controls", true);
        s.Set("use_fixed_opacity", true); s.Set("paint_opacity_value", 1);
        s.Set("line_mode", false); s.Set("background_fill", false);
    }

    public static Settings CalibrationSettings(Settings source, double size)
    {
        if (!BrushFootprints.Sizes.Contains(size)) throw new ArgumentOutOfRangeException(nameof(size));
        var s = source.Clone();
        Prepare(s);
        s.Set("adaptive_brush", false); s.Set("input_engine", "Stable");
        s.Set("fast_transfer", false);
        s.Set("coverage_mode", "Fast"); s.Set("force_precision_controls", false);
        s.Set("auto_brush_size", false); s.Set("brush_size_value", size); s.Set("interval_value", .01);
        return s;
    }

    public static void UpgradeCalibrationContext(Settings s)
    {
        if (!s.Text("brush_calibration_context").StartsWith("v1:", StringComparison.Ordinal)) return;
        if (s.Mode == ColorMode.HexDirect && !s.HexControlsReady) return;
        var r = s.Calibration.Rect("canvas");
        var legacy = $"v1:{r.Width}:{r.Height}:{s.Text("color_mode")}:{s.Int("brush_shape_slot", 3)}:{s.Text("brush_shape")}:" + s.PaintCalibration().Rect("size_track");
        // Upgrade only a matching legacy calibration; stale measurements stay stale.
        if (s.Text("brush_calibration_context") == legacy)
            s.Set("brush_calibration_context", Context(s));
    }

    public static void Validate(Settings s)
    {
        if (!s.Bool("adaptive_brush")) return;
        if (s.Text("coverage_mode", "Precision") != "Precision" || !s.Bool("force_precision_controls", true)
            || s.Bool("line_mode")&&!SpeedCalibration.Use(s) || s.Bool("background_fill") || (s.Bool("use_fixed_opacity", true) && s.Number("paint_opacity_value", 1) < .999))
            throw new InvalidOperationException("Адаптивний пензель потребує Precision, точних controls, Opacity 1 і вимкнених Shift-line та заповнення фону.");
        if (s.Int("brush_shape_slot", 3) is not (3 or 4)&&!BrushFootprints.Read(s).Any(p=>p.SolidCore.Valid))
            throw new InvalidOperationException("Адаптивний пензель потребує суцільного круглого пензля (3) або квадратного (4).");
        if (!CalibrationCurrent(s))
            throw new InvalidOperationException("Спочатку калібруй пензель для поточного Canvas, режиму кольорів і форми пензля в розділі «Адаптивний режим».");
        var r = s.Calibration.Rect("canvas");
        if (!r.Valid || (long)r.Width * r.Height > 16_000_000)
            throw new InvalidOperationException("Адаптивний пензель підтримує Canvas до 16 мільйонів пікселів.");
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

    public static Dictionary<int, List<BrushStroke>> Build(PaintPlan plan, Settings s, CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();
        s=BrushFootprints.Snapshot(s);
        var basic = Coverage.Build(plan, s);
        var output = basic.ToDictionary(x => x.Key, x => x.Value.Select(l => new BrushStroke(l, 0)).ToList());
        if (!s.Bool("adaptive_brush")) return output;
        Validate(s);
        if(BrushFootprints.Read(s).Count>0)return MeasuredAdaptive.Build(plan,s,basic,token);
        var rect = s.Calibration.Rect("canvas");
        int w = rect.Width, h = rect.Height;
        var labels = new int[checked(w * h)];
        Array.Fill(labels, -1);
        var xb = Coverage.Partition(rect.Left, rect.Right, plan.Width);
        var yb = Coverage.Partition(rect.Top, rect.Bottom, plan.Height);
        for (int y = 0; y < plan.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (int x = 0; x < plan.Width; x++)
                for (int py = yb[y] - rect.Top; py < yb[y + 1] - rect.Top; py++)
                    Array.Fill(labels, plan.Indices[y * plan.Width + x], py * w + xb[x] - rect.Left, xb[x + 1] - xb[x]);
        }
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
        foreach (var brush in Samples(s).Where(x => x.Inner >= 2 && x.Size <= s.Int("adaptive_max_size", 20)))
            for (int y = brush.Outer + 1; y < h - brush.Outer - 1; y += 2 * brush.Inner + 1)
            {
                token.ThrowIfCancellationRequested();
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
                cost += TransferSchedule.EstimateBatch(s,speed,new(op.Size,new[]{op.Line},1));
            }
            return cost;
        }
        foreach (var (color, lines) in basic)
        {
            token.ThrowIfCancellationRequested();
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
