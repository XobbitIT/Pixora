namespace CanvasForge.Core;
public static class Planner
{
    public const string PaletteRevision="palette-dominant-color-reservation-v2";
    public static PaintPlan Build(PixelImage source, Settings settings, IProgress<string>? progress = null, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var recipe = ImageRecipe.Capture(settings);
        if(ReleaseProfile.Standard(settings))settings=DrawingWorkflow.Effective(settings);
        settings.Validate();
        var target = TargetPreparation.Build(source, recipe, progress, token);
        var w = target.Width; var h = target.Height;
        var indices = target.Indices;
        // Color locations are bound only after the image has been prepared.
        var palette = target.Mode == ColorMode.RustPalette ? settings.Palette().ToArray() : target.Palette;
        if (target.Mode == ColorMode.RustPalette && palette.Any(x => x.ClickPoint is null))
            throw new InvalidOperationException("Палітра містить колір без координат кліку.");
        int? background = null;
        var strokeGrid = (int[])indices.Clone();
        // A base fill is safe only when every canvas cell will be painted.
        if (settings.Bool("background_fill") && settings.Text("background_mode") == "auto" && !indices.Contains(-1))
        {
            var border = new List<int>();
            for (var x = 0; x < w; x++)
            {
                border.Add(indices[x]);
                border.Add(indices[(h - 1) * w + x]);
            }

            for (var y = 0; y < h; y++)
            {
                border.Add(indices[y * w]);
                border.Add(indices[y * w + w - 1]);
            }

            background = border.GroupBy(x => x).OrderByDescending(x => x.Count()).First().Key;
            for (var i = 0; i < strokeGrid.Length; i++)
                if (strokeGrid[i] == background)
                    strokeGrid[i] = -1;
        }

        var strokes = Group(strokeGrid, w, h, settings.Bool("rustangelo_mode", true), token);
        return new()
        {
            Width = w,
            Height = h,
            Mode = settings.Mode,
            Palette = palette,
            Indices = indices,
            Preview = target.Preview,
            Recipe = recipe,
            Strokes = strokes,
            Counts = target.Counts,
            BackgroundColor = background,
            Error = target.Error,
            Identity = PlanIdentity.Compute(source, settings, palette),
            TargetIdentity = target.Identity,
            BeforeEdgeMerge = target.BeforeEdgeMerge,
            EdgeMerge = target.EdgeMerge
        };
    }

    private static IEnumerable<int> Neighbors(int i, int w, int h)
    {
        var x = i % w;
        var y = i / w;
        if (x > 0)
            yield return i - 1;
        if (x + 1 < w)
            yield return i + 1;
        if (y > 0)
            yield return i - w;
        if (y + 1 < h)
            yield return i + w;
    }

    public static void Cleanup(int[] grid, int w, int h, int passes, int minRegion, CancellationToken token)
        => TargetPreparation.Cleanup(grid, w, h, passes, minRegion, token);

    public static Dictionary<int, List<Stroke>> Group(int[] grid, int w, int h, bool hybrid, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var result = new Dictionary<int, List<Stroke>>();
        var seen = new bool[grid.Length];
        for (var seed = 0; seed < grid.Length; seed++)
        {
            if (seen[seed] || grid[seed] < 0)
                continue;
            token.ThrowIfCancellationRequested();
            var color = grid[seed];
            var cells = new List<int>();
            var q = new Queue<int>();
            q.Enqueue(seed);
            seen[seed] = true;
            while (q.Count > 0)
            {
                if((cells.Count&1023)==0)token.ThrowIfCancellationRequested();
                var i = q.Dequeue();
                cells.Add(i);
                foreach (var j in Neighbors(i, w, h))
                    if (!seen[j] && grid[j] == color)
                    {
                        seen[j] = true;
                        q.Enqueue(j);
                    }
            }

            List<Stroke> Scan(bool vertical)
            {
                var strokes = new List<Stroke>();
                foreach (var row in cells.GroupBy(i => vertical ? i % w : i / w).OrderBy(g => g.Key))
                {
                    token.ThrowIfCancellationRequested();
                    var coords = row.Select(i => vertical ? i / w : i % w).OrderBy(x => x).ToArray();
                    var a = coords[0];
                    var b = a;
                    void Add() => strokes.Add(vertical ? new(color, row.Key, a, row.Key, b) : new(color, a, row.Key, b, row.Key));
                    foreach (var x in coords.Skip(1))
                    {
                        if (x == b + 1)
                            b = x;
                        else
                        {
                            Add();
                            a = b = x;
                        }
                    }

                    Add();
                }

                return strokes;
            }

            var hor = Scan(false);
            var vert = hybrid ? Scan(true) : hor;
            var best = vert.Count < hor.Count ? vert : hor;
            if (!result.TryGetValue(color, out var list))
                result[color] = list = new();
            list.AddRange(best);
        }

        return result;
    }
}

public static class Coverage
{
    public const string StandardRevision="standard-dense-rectangular-runs-v3";
    public static bool ShiftLine(Settings s,double size,ScreenLine line)
        => SpeedCalibration.Resolve(s,size,line)?.Method==StrokeMethod.Shift;
    public static int[] Partition(int start, int end, int cells) => Enumerable.Range(0, cells + 1).Select(i => (int)Math.Round(start + (double)(end - start) * i / cells)).ToArray();
    public static List<ScreenLine> Expand(IEnumerable<Stroke> strokes, ScreenRect canvas, int w, int h, int pitch)
    {
        var xb = Partition(canvas.Left, canvas.Right, w);
        var yb = Partition(canvas.Top, canvas.Bottom, h);
        var lines = new List<ScreenLine>();
        pitch = Math.Max(1, pitch);
        foreach (var st in strokes)
        {
            if (st.Y1 == st.Y2)
            {
                var x1 = xb[st.X1];
                var x2 = xb[st.X2 + 1] - 1;
                var y1 = yb[st.Y1];
                var y2 = yb[st.Y1 + 1] - 1;
                if (x2 < x1 || y2 < y1)
                    continue;
                for (var y = y1; y <= y2; y += pitch)
                    lines.Add(new(x1, y, x2, y));
            }
            else
            {
                var y1 = yb[st.Y1];
                var y2 = yb[st.Y2 + 1] - 1;
                var x1 = xb[st.X1];
                var x2 = xb[st.X1 + 1] - 1;
                if (x2 < x1 || y2 < y1)
                    continue;
                for (var x = x1; x <= x2; x += pitch)
                    lines.Add(new(x, y1, x, y2));
            }
        }

        return Merge(lines);
    }

    public static List<ScreenLine> Merge(IEnumerable<ScreenLine> lines)
    {
        var list = lines.ToList();
        var output = new List<ScreenLine>();
        foreach (var vertical in new[]
        {
            false,
            true
        }

        )
            foreach (var row in list.Where(l => vertical ? l.X1 == l.X2 && l.Y1 != l.Y2 : l.Y1 == l.Y2).GroupBy(l => vertical ? l.X1 : l.Y1).OrderBy(g => g.Key))
            {
                var spans = row.Select(l => vertical ? (A: Math.Min(l.Y1, l.Y2), B: Math.Max(l.Y1, l.Y2)) : (A: Math.Min(l.X1, l.X2), B: Math.Max(l.X1, l.X2))).OrderBy(p => p.A).ToArray();
                var a = spans[0].A;
                var b = spans[0].B;
                void Add() => output.Add(vertical ? new(row.Key, a, row.Key, b) : new(a, row.Key, b, row.Key));
                foreach (var s in spans.Skip(1))
                {
                    if (s.A <= b + 1)
                        b = Math.Max(b, s.B);
                    else
                    {
                        Add();
                        a = s.A;
                        b = s.B;
                    }
                }

                Add();
            }

        return output;
    }

    public static Dictionary<int, List<ScreenLine>> Build(PaintPlan plan, Settings settings) => Build(plan, settings, null);

    // standardPitch is for offline what-if estimates only (SpeedReport); the
    // product always passes null, so Standard keeps its 1 px pitch.
    internal static Dictionary<int, List<ScreenLine>> Build(PaintPlan plan, Settings settings, int? standardPitch)
    {
        var r = settings.Calibration.Rect("canvas");
        if (!r.Valid)
            r = new(0, 0, 512, 512);
        var speed = SpeedProfile.Get(settings.Text("speed_profile", "Rapid"));
        var precision = settings.Text("coverage_mode", "Precision") == "Precision";
        // Movement presets must not silently remove coverage rows. Ordinary
        // Size3 has no measured proof authorizing 2/3-pixel row spacing.
        var pitch = ReleaseProfile.Standard(settings)?standardPitch??1:precision ? speed.Pitch : Math.Max(1, settings.Int("coverage_pitch", 2));
        var groups = plan.Strokes.ToDictionary(p => p.Key, p => Expand(p.Value, r, plan.Width, plan.Height, pitch));
        if (plan.BackgroundColor is int bg)
        {
            var baseLines = new List<ScreenLine>();
            for (var y = r.Top; y < r.Bottom; y += pitch)
                baseLines.Add(new(r.Left, y, r.Right - 1, y));
            if (groups.TryGetValue(bg, out var extra))
                baseLines.AddRange(extra);
            groups[bg] = baseLines;
        }

        if(ReleaseProfile.Standard(settings))
            foreach(var color in groups.Keys.ToArray())groups[color]=StandardCoverageRuns.Optimize(groups[color]);
        return groups;
    }

    public static double EstimateSeconds(PaintPlan plan, Settings s, string? speedName = null,Dictionary<int,List<PaintBatch>>? preparedGroups=null)
    {
        var copy = s.Clone();
        if (speedName is not null)
            copy.Set("speed_profile", speedName);
        copy = ReleaseProfile.Standard(copy)?DrawingWorkflow.Effective(copy):BrushFootprints.Snapshot(copy);
        var groups = preparedGroups??TransferSchedule.Build(plan, copy);
        // Preview and execution share the same work, transport measurements,
        // first-palette proof and size-only setup/finish accounting.
        return Math.Max(0,copy.Int("start_delay",5))+
            PaintTimingPlan.Build(copy,groups,TransferSchedule.Order(plan,groups)).Sum(w=>w.PlannedSeconds);
    }
}

public static class ControlCurve
{
    public static bool IsMaximum(string kind, double value) => kind == "size" ? value >= 99.999 : value >= .99999;
    // Fractions refer to the interactive track, excluding the numeric field.
    public static readonly (double Value, double Fraction)[] Size = [(1, 0), (100, 1)];
    public static double Fraction(string kind, double value)
    {
        if (kind != "size")
            return Math.Clamp((value - .01) / .99, 0, 1);
        value = Math.Clamp(value, 1, 100);
        for (var i = 1; i < Size.Length; i++)
            if (value <= Size[i].Value)
            {
                var a = Size[i - 1];
                var b = Size[i];
                return a.Fraction + (b.Fraction - a.Fraction) * (value - a.Value) / (b.Value - a.Value);
            }

        return 1;
    }

    public static ScreenPoint? Point(Calibration cal, string kind, double value)
    {
        var a = cal.Point(kind + "_min");
        var b = cal.Point(kind + "_max");
        if (a is null || b is null)
            return null;
        var f = Fraction(kind, value);
        return new((int)Math.Round(a.Value.X + (b.Value.X - a.Value.X) * f), (int)Math.Round(a.Value.Y + (b.Value.Y - a.Value.Y) * f));
    }
}
