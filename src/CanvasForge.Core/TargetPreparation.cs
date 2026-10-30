namespace CanvasForge.Core;

// Image-only stage: no ReleaseProfile, DrawingWorkflow, speed or input engine.
public static class TargetPreparation
{
    public static PreparedTarget Build(PixelImage source, ImageRecipe recipe, IProgress<string>? progress = null, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var settings = recipe.Parameters();
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
            var rgb = palette.Select(x => x.Color).ToArray();
            var full = Quantization.Map(image, rgb, alpha, false, token);
            var ranked = full.Where(x => x >= 0).GroupBy(x => x).OrderByDescending(x => x.Count()).ThenBy(x => x.Key).Select(x => x.Key).ToList();
            var text = settings.Text("max_colors", "Auto");
            var cap = text == "Auto" ? palette.Length : Math.Min(int.Parse(text), palette.Length);
            // Reserve skin shades only when they actually occur in the mapped
            // image. Small logo palettes must retain their dominant colors.
            var forced = settings.Bool("skin_assist", true) && cap >= 16
                ? ranked.Where(i => IsSkin(rgb[i])).Take(Math.Max(1, cap / 4)).ToArray() : [];
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

        int[]? beforeMerge=null;EdgeMergeResult? merge=null;
        if(settings.Bool("edge_merge",false))
        {
            beforeMerge=(int[])indices.Clone();
            merge=EdgeRegionMerge.Apply(indices,w,h,settings.Int("edge_merge_thickness",2),EdgeRegionMerge.MinimumArea(settings,w,h),token);
        }
        var counts = indices.Where(i => i >= 0).GroupBy(i => i).ToDictionary(g => g.Key, g => g.Count());
        var preview = new PixelImage(w, h);
        for (var i = 0; i < indices.Length; i++)
            preview.Set(i, indices[i] < 0 ? Rgb.White : palette[indices[i]].Color, indices[i] < 0 ? (byte)0 : (byte)255);
        return new()
        {
            Recipe = recipe, Width = w, Height = h, Mode = settings.Mode,
            Palette = palette, Indices = indices, Preview = preview, Counts = counts,
            Error = Quantization.Error(reference, indices, palette.Select(x => x.Color).ToArray()),
            Identity = TargetIdentity.Compute(preview, indices, palette),
            BeforeEdgeMerge = beforeMerge, EdgeMerge = merge
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
        token.ThrowIfCancellationRequested();
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
                if((cells.Count&1023)==0)token.ThrowIfCancellationRequested();
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

}
