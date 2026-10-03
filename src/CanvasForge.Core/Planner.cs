namespace CanvasForge.Core;
public static class Planner
{
    public static PaintPlan Build(PixelImage source, Settings settings, IProgress<string>? progress = null, CancellationToken token = default)
    {
        settings.Validate();
        var canvas = settings.Calibration.Rect("canvas");
        var cw = canvas.Valid ? canvas.Width : 512;
        var ch = canvas.Valid ? canvas.Height : 512;
        var cell = settings.Int("cell_px", 3);
        var w = Math.Max(2, (int)Math.Round((double)cw / cell));
        var h = Math.Max(2, (int)Math.Round((double)ch / cell));
        if ((long)w * h > 8_000_000)
            throw new InvalidOperationException("Plan exceeds 8 million cells. Increase detail size.");
        progress?.Report("Розміщення зображення…");
        var reference = ImageProcessing.Prepare(source, w, h, settings, token);
        var image = ImageProcessing.Filter(reference, settings, token);
        var alpha = settings.Int("alpha_threshold", 16);
        var dither = settings.Bool("dither");
        PaletteEntry[] palette;
        int[] indices;
        progress?.Report("Підбір кольорів…");
        if (settings.Mode == ColorMode.HexDirect)
        {
            var capText = settings.Text("hex_max_colors", "128");
            var cap = capText == "Auto" ? 64 : Math.Clamp(int.Parse(capText), 1, 256);
            var rgb = Quantization.HexPalette(image, cap, alpha, token);
            indices = Quantization.Map(image, rgb, alpha, false, token);
            if (capText == "Auto")
            {
                var err = Quantization.Error(image, indices, rgb);
                foreach (var limit in new[]
                {
                    96,
                    128,
                    192,
                    256
                }

                )
                {
                    var next = Quantization.HexPalette(image, limit, alpha, token);
                    var map = Quantization.Map(image, next, alpha, false, token);
                    var e = Quantization.Error(image, map, next);
                    var improvement = err <= 1e-6 ? 0 : (err - e) / err * 100;
                    if (improvement < settings.Number("adaptive_threshold", 1))
                        break;
                    rgb = next;
                    indices = map;
                    err = e;
                    if (err < 1)
                        break;
                }
            }

            if (dither)
                indices = Quantization.Map(image, rgb, alpha, true, token);
            palette = rgb.Select(c => new PaletteEntry(c, null, "hex")).ToArray();
        }
        else
        {
            palette = settings.Palette().ToArray();
            if (palette.Length == 0)
                throw new InvalidOperationException("Захопи палітру Rust 4×16 перед побудовою плану.");
            if (palette.Any(x => x.ClickPoint is null))
                throw new InvalidOperationException("Палітра містить колір без координат кліку.");
            var rgb = palette.Select(x => x.Color).ToArray();
            var full = Quantization.Map(image, rgb, alpha, false, token);
            var ranked = full.Where(x => x >= 0).GroupBy(x => x).OrderByDescending(x => x.Count()).ThenBy(x => x.Key).Select(x => x.Key).ToList();
            var text = settings.Text("max_colors", "Auto");
            var cap = text == "Auto" ? palette.Length : Math.Min(int.Parse(text), palette.Length);
            var forced = settings.Bool("skin_assist", true) ? Enumerable.Range(0, rgb.Length).Where(i => IsSkin(rgb[i])).Take(Math.Max(1, cap / 4)).ToArray() : [];
            var allowed = forced.Concat(ranked).Distinct().Take(cap).ToArray();
            if (allowed.Length == 0)
                allowed = [0];
            if (text == "Auto")
            {
                var previous = double.MaxValue;
                int[]? chosen = null;
                foreach (var limit in new[]
                {
                    16,
                    32,
                    64,
                    palette.Length
                }.Distinct().OrderBy(x => x))
                {
                    var ids = forced.Concat(ranked).Distinct().Take(Math.Min(limit, palette.Length)).ToArray();
                    if (ids.Length == 0)
                        ids = [0];
                    var map = Quantization.Map(image, rgb, alpha, false, token, ids);
                    var err = Quantization.Error(image, map, rgb);
                    if (chosen is not null && previous > 0 && (previous - err) / previous * 100 < settings.Number("adaptive_threshold", 1))
                        break;
                    chosen = ids;
                    previous = err;
                }

                allowed = chosen ?? allowed;
            }

            indices = Quantization.Map(image, rgb, alpha, dither, token, allowed);
            if (settings.Bool("skin_assist", true))
            {
                var skins = allowed.Where(i => IsSkin(rgb[i])).ToArray();
                var labs = rgb.Select(Lab.From).ToArray();
                if (skins.Length > 0)
                    for (var i = 0; i < indices.Length; i++)
                        if (indices[i] >= 0 && IsSkin(image.Color(i)) && !IsSkin(rgb[indices[i]]))
                        {
                            var lab = Lab.From(image.Color(i));
                            var skin = Quantization.Nearest(lab, labs, skins);
                            if (lab.Distance(labs[skin]) <= lab.Distance(labs[indices[i]]) * 1.25 + 12)
                                indices[i] = skin;
                        }
            }
        }

        progress?.Report("Оптимізація областей…");
        var original = (int[])indices.Clone();
        Cleanup(indices, w, h, settings.Int("smooth_passes"), settings.Int("min_region", 1), token);
        if (settings.Bool("edge_preserve", true))
        {
            for (var y = 1; y < h - 1; y++)
                for (var x = 1; x < w - 1; x++)
                {
                    var i = y * w + x;
                    if (original[i] < 0)
                        continue;
                    var c = image.Color(i);
                    var grad = new[]
                    {
                        i - 1,
                        i + 1,
                        i - w,
                        i + w
                    }.Max(j => ColorDelta(c, image.Color(j)));
                    if (grad >= 24)
                        indices[i] = original[i];
                }
        }

        var counts = indices.Where(i => i >= 0).GroupBy(i => i).ToDictionary(g => g.Key, g => g.Count());
        var preview = new PixelImage(w, h);
        for (var i = 0; i < indices.Length; i++)
            preview.Set(i, indices[i] < 0 ? Rgb.White : palette[indices[i]].Color, indices[i] < 0 ? (byte)0 : (byte)255);
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
            Preview = preview,
            Strokes = strokes,
            Counts = counts,
            BackgroundColor = background,
            Error = Quantization.Error(reference, indices, palette.Select(x => x.Color).ToArray()),
            Identity = PlanIdentity.Compute(source, settings, palette)
        };
    }

    private static bool IsSkin(Rgb c) => c.R > 70 && c.G > 35 && c.B > 20 && c.R > c.G && c.G >= c.B && c.R - c.B > 12 && c.R - c.G < 95;
    private static int ColorDelta(Rgb a, Rgb b) => Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
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
    {
        for (var pass = 0; pass < passes; pass++)
        {
            var source = (int[])grid.Clone();
            for (var y = 1; y < h - 1; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 1; x < w - 1; x++)
                {
                    var i = y * w + x;
                    if (source[i] < 0)
                        continue;
                    var neighbors = new List<int>();
                    for (var dy = -1; dy <= 1; dy++)
                        for (var dx = -1; dx <= 1; dx++)
                            if (dx != 0 || dy != 0)
                                neighbors.Add(source[(y + dy) * w + x + dx]);
                    var winner = neighbors.Where(k => k >= 0).GroupBy(k => k).OrderByDescending(g => g.Count()).FirstOrDefault();
                    if (winner is not null && winner.Count() >= 5)
                        grid[i] = winner.Key;
                }
            }
        }

        if (minRegion <= 1)
            return;
        var seen = new bool[grid.Length];
        var snapshot = (int[])grid.Clone();
        for (var seed = 0; seed < grid.Length; seed++)
        {
            if (seen[seed] || snapshot[seed] < 0)
                continue;
            token.ThrowIfCancellationRequested();
            var cells = new List<int>();
            var q = new Queue<int>();
            q.Enqueue(seed);
            seen[seed] = true;
            var border = new List<int>();
            while (q.Count > 0)
            {
                var i = q.Dequeue();
                cells.Add(i);
                foreach (var j in Neighbors(i, w, h))
                    if (snapshot[j] == snapshot[seed])
                    {
                        if (!seen[j])
                        {
                            seen[j] = true;
                            q.Enqueue(j);
                        }
                    }
                    else if (snapshot[j] >= 0)
                        border.Add(snapshot[j]);
            }

            if (cells.Count < minRegion && border.Count > 0)
            {
                var target = border.GroupBy(k => k).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
                foreach (var i in cells)
                    grid[i] = target;
            }
        }
    }

    public static Dictionary<int, List<Stroke>> Group(int[] grid, int w, int h, bool hybrid, CancellationToken token = default)
    {
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

    public static Dictionary<int, List<ScreenLine>> Build(PaintPlan plan, Settings settings)
    {
        var r = settings.Calibration.Rect("canvas");
        if (!r.Valid)
            r = new(0, 0, 512, 512);
        var speed = SpeedProfile.Get(settings.Text("speed_profile", "Rapid"));
        var precision = settings.Text("coverage_mode", "Precision") == "Precision";
        var pitch = precision ? speed.Pitch : Math.Max(1, settings.Int("coverage_pitch", 2));
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

        return groups;
    }

    public static double EstimateSeconds(PaintPlan plan, Settings s, string? speedName = null)
    {
        var copy = s.Clone();
        if (speedName is not null)
            copy.Set("speed_profile", speedName);
        var speed = SpeedProfile.Get(copy.Text("speed_profile"));
        var groups = TransferSchedule.Build(plan, copy);
        double seconds = copy.Int("start_delay", 5)+3*StrokeTiming.SliderChangeEstimate(copy)+StrokeTiming.ClickEstimate(copy);
        if(!StrokeTiming.Fast(copy) || copy.Bool("use_fixed_opacity",true)&&copy.Number("paint_opacity_value",1)!=1)
            seconds+=StrokeTiming.SliderChangeEstimate(copy); // Final restore to Opacity 1.
        double previousSize=speed.BrushSize;
        var order=groups.Keys.OrderByDescending(i=>plan.Counts.GetValueOrDefault(i)).ToList();
        if(plan.BackgroundColor is int bg){order.Remove(bg);order.Insert(0,bg);}
        foreach (var color in order)
        {
            seconds += copy.Mode == ColorMode.HexDirect ? StrokeTiming.HexChangeEstimate(copy)+StrokeTiming.ColorDelay(copy) : StrokeTiming.ColorDelay(copy) + StrokeTiming.ClickEstimate(copy);
            foreach (var op in groups[color])
            {
                double size=op.Size>0?op.Size:speed.BrushSize;
                if(copy.Bool("adaptive_brush")&&size!=previousSize)seconds+=StrokeTiming.SliderChangeEstimate(copy);
                previousSize=size;
                seconds += TransferSchedule.EstimateBatch(copy,speed,op);
            }
        }

        return seconds;
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
