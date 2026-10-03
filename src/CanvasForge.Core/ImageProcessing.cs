namespace CanvasForge.Core;
public readonly record struct Lab(double L, double A, double B)
{
    public double Distance(Lab other) => (L - other.L) * (L - other.L) + (A - other.A) * (A - other.A) + (B - other.B) * (B - other.B);
    public static Lab From(Rgb c)
    {
        static double Linear(double x) => x > .04045 ? Math.Pow((x + .055) / 1.055, 2.4) : x / 12.92;
        static double F(double x) => x > .008856 ? Math.Cbrt(x) : 7.787 * x + 16.0 / 116;
        var r = Linear(c.R / 255.0);
        var g = Linear(c.G / 255.0);
        var b = Linear(c.B / 255.0);
        var x = F((r * .4124564 + g * .3575761 + b * .1804375) / .95047);
        var y = F(r * .2126729 + g * .7151522 + b * .072175);
        var z = F((r * .0193339 + g * .119192 + b * .9503041) / 1.08883);
        return new(116 * y - 16, 500 * (x - y), 200 * (y - z));
    }
}

public static class ImageProcessing
{
    public static PixelImage Crop(PixelImage image, int l, int t, int r, int b)
    {
        var result = new PixelImage(r - l, b - t);
        for (var y = t; y < b; y++)
            Buffer.BlockCopy(image.Rgba, (y * image.Width + l) * 4, result.Rgba, ((y - t) * result.Width) * 4, result.Width * 4);
        return result;
    }

    public static PixelImage RemoveBackground(PixelImage source, CancellationToken token)
    {
        var im = source.Clone();
        var w = im.Width;
        var h = im.Height;
        if (w < 4 || h < 4)
            return im;
        var patch = Math.Max(1, Math.Min(w, h) / 20);
        var corners = new List<Rgb>();
        foreach (var(xx, yy)in new[]
        {
            (0, 0),
            (w - patch, 0),
            (0, h - patch),
            (w - patch, h - patch)
        }

        )
        {
            var rr = new List<byte>();
            var gg = new List<byte>();
            var bb = new List<byte>();
            for (var y = yy; y < yy + patch; y++)
                for (var x = xx; x < xx + patch; x++)
                {
                    var c = im.Color(y * w + x);
                    rr.Add(c.R);
                    gg.Add(c.G);
                    bb.Add(c.B);
                }

            rr.Sort();
            gg.Sort();
            bb.Sort();
            corners.Add(new(rr[rr.Count / 2], gg[gg.Count / 2], bb[bb.Count / 2]));
        }

        var seen = new bool[w * h];
        var queue = new Queue<int>();
        void Add(int i)
        {
            if (seen[i])
                return;
            var c = im.Color(i);
            if (im.Alpha(i) <= 8 || corners.Any(s => Math.Max(Math.Abs(c.R - s.R), Math.Max(Math.Abs(c.G - s.G), Math.Abs(c.B - s.B))) <= 26))
            {
                seen[i] = true;
                queue.Enqueue(i);
            }
        }

        for (var x = 0; x < w; x++)
        {
            Add(x);
            Add((h - 1) * w + x);
        }

        for (var y = 0; y < h; y++)
        {
            Add(y * w);
            Add(y * w + w - 1);
        }

        while (queue.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var i = queue.Dequeue();
            var x = i % w;
            var y = i / w;
            if (x > 0)
                Add(i - 1);
            if (x + 1 < w)
                Add(i + 1);
            if (y > 0)
                Add(i - w);
            if (y + 1 < h)
                Add(i + w);
        }

        var removed = seen.Count(x => x);
        if (removed == 0 || removed >= w * h * .97)
            return im;
        for (var i = 0; i < seen.Length; i++)
            if (seen[i])
                im.Rgba[i * 4 + 3] = 0;
        return CropSubject(im);
    }

    public static PixelImage CropSubject(PixelImage im)
    {
        var l = im.Width;
        var t = im.Height;
        var r = 0;
        var b = 0;
        for (var y = 0; y < im.Height; y++)
            for (var x = 0; x < im.Width; x++)
                if (im.Alpha(y * im.Width + x) > 10)
                {
                    l = Math.Min(l, x);
                    t = Math.Min(t, y);
                    r = Math.Max(r, x + 1);
                    b = Math.Max(b, y + 1);
                }

        return r > l && b > t ? Crop(im, Math.Max(0, l - 2), Math.Max(0, t - 2), Math.Min(im.Width, r + 2), Math.Min(im.Height, b + 2)) : im;
    }

    // "Fill Canvas with subject" must also work for opaque JPG/PNG images.
    // Detect only border-connected pixels similar to the corner background,
    // crop to the remaining subject, but keep the original pixels/background intact.
    public static PixelImage CropOpaqueSubject(PixelImage im, CancellationToken token)
    {
        var w = im.Width;
        var h = im.Height;
        if (w < 4 || h < 4)
            return CropSubject(im);

        // Existing transparency already provides a reliable subject mask.
        if (Enumerable.Range(0, w * h).Any(i => im.Alpha(i) <= 10))
            return CropSubject(im);

        var patch = Math.Max(1, Math.Min(w, h) / 20);
        var corners = new List<Rgb>();
        foreach (var (xx, yy) in new[]
        {
            (0, 0),
            (w - patch, 0),
            (0, h - patch),
            (w - patch, h - patch)
        })
        {
            var rr = new List<byte>();
            var gg = new List<byte>();
            var bb = new List<byte>();
            for (var y = yy; y < yy + patch; y++)
                for (var x = xx; x < xx + patch; x++)
                {
                    var c = im.Color(y * w + x);
                    rr.Add(c.R);
                    gg.Add(c.G);
                    bb.Add(c.B);
                }

            rr.Sort();
            gg.Sort();
            bb.Sort();
            corners.Add(new(rr[rr.Count / 2], gg[gg.Count / 2], bb[bb.Count / 2]));
        }

        var background = new bool[w * h];
        var queue = new Queue<int>();
        void Add(int i)
        {
            if (background[i])
                return;
            var c = im.Color(i);
            if (corners.Any(s => Math.Max(Math.Abs(c.R - s.R), Math.Max(Math.Abs(c.G - s.G), Math.Abs(c.B - s.B))) <= 26))
            {
                background[i] = true;
                queue.Enqueue(i);
            }
        }

        for (var x = 0; x < w; x++)
        {
            Add(x);
            Add((h - 1) * w + x);
        }
        for (var y = 0; y < h; y++)
        {
            Add(y * w);
            Add(y * w + w - 1);
        }

        while (queue.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var i = queue.Dequeue();
            var x = i % w;
            var y = i / w;
            if (x > 0) Add(i - 1);
            if (x + 1 < w) Add(i + 1);
            if (y > 0) Add(i - w);
            if (y + 1 < h) Add(i + w);
        }

        var removed = background.Count(x => x);
        if (removed == 0 || removed >= w * h * .97)
            return im;

        var l = w;
        var t = h;
        var r = 0;
        var b = 0;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var i = y * w + x;
                if (!background[i] && im.Alpha(i) > 10)
                {
                    l = Math.Min(l, x);
                    t = Math.Min(t, y);
                    r = Math.Max(r, x + 1);
                    b = Math.Max(b, y + 1);
                }
            }

        return r > l && b > t
            ? Crop(im, Math.Max(0, l - 2), Math.Max(0, t - 2), Math.Min(w, r + 2), Math.Min(h, b + 2))
            : im;
    }

    // Premultiplied Lanczos resampling prevents dark fringes around transparent art.
    public static PixelImage Prepare(PixelImage source, int width, int height, Settings settings, CancellationToken token)
    {
        var removeBackground = settings.Bool("remove_bg");
        var im = removeBackground ? RemoveBackground(source, token) : source;
        if (settings.Bool("fill_subject"))
            im = removeBackground ? CropSubject(im) : CropOpaqueSubject(im, token);
        var mode = settings.Text("fit_mode", "fit square");
        if (mode == "smart")
            mode = settings.Bool("fill_subject") ? "crop" : "fit whole";
        var boxW = mode == "fit square" ? Math.Min(width, height) : width;
        var boxH = mode == "fit square" ? Math.Min(width, height) : height;
        var scale = mode == "crop" ? Math.Max((double)width / im.Width, (double)height / im.Height) : Math.Min((double)boxW / im.Width, (double)boxH / im.Height);
        var rw = Math.Max(1, (int)Math.Round(im.Width * scale));
        var rh = Math.Max(1, (int)Math.Round(im.Height * scale));
        var offsetX = (width - rw) / 2;
        var offsetY = (height - rh) / 2;
        var result = new PixelImage(width, height);
        static double Kernel(double v)
        {
            v = Math.Abs(v);
            if (v < 1e-9)
                return 1;
            if (v >= 3)
                return 0;
            return Math.Sin(Math.PI * v) * Math.Sin(Math.PI * v / 3) / (Math.PI * Math.PI * v * v / 3);
        }

        var sxScale = (double)im.Width / rw;
        var syScale = (double)im.Height / rh;
        var fx = Math.Max(1, sxScale);
        var fy = Math.Max(1, syScale);
        Parallel.For(0, height, new ParallelOptions { CancellationToken = token }, y =>
        {
            for (var x = 0; x < width; x++)
            {
                if (x < offsetX || y < offsetY || x >= offsetX + rw || y >= offsetY + rh)
                {
                    result.Set(y * width + x, Rgb.White, 0);
                    continue;
                }

                var sx = (x - offsetX + .5) * sxScale - .5;
                var sy = (y - offsetY + .5) * syScale - .5;
                double rs = 0, gs = 0, bs = 0, aa = 0, ww = 0;
                for (var yy = Math.Max(0, (int)Math.Ceiling(sy - 3 * fy)); yy <= Math.Min(im.Height - 1, (int)Math.Floor(sy + 3 * fy)); yy++)
                    for (var xx = Math.Max(0, (int)Math.Ceiling(sx - 3 * fx)); xx <= Math.Min(im.Width - 1, (int)Math.Floor(sx + 3 * fx)); xx++)
                    {
                        var weight = Kernel((xx - sx) / fx) * Kernel((yy - sy) / fy);
                        var i = yy * im.Width + xx;
                        var a = im.Alpha(i) / 255.0;
                        var c = im.Color(i);
                        rs += c.R * a * weight;
                        gs += c.G * a * weight;
                        bs += c.B * a * weight;
                        aa += a * weight;
                        ww += weight;
                    }

                var alpha = ww == 0 ? 0 : Math.Clamp(aa / ww, 0, 1);
                var divisor = Math.Abs(aa) < 1e-9 ? 1 : aa;
                static byte Byte(double v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);
                // Keep straight RGB and alpha separate. A white matte here is
                // irreversible once the planner turns accepted cells opaque.
                result.Set(y * width + x, alpha <= 0 ? new(0, 0, 0)
                    : new(Byte(rs / divisor), Byte(gs / divisor), Byte(bs / divisor)), Byte(alpha * 255));
            }
        });
        return result;
    }

    public static PixelImage Filter(PixelImage image, Settings s, CancellationToken token)
    {
        var im = image.Clone();
        if (s.Bool("median_cleanup"))
        {
            var src = im;
            var dest = im.Clone();
            Parallel.For(0, im.Height, new ParallelOptions { CancellationToken = token }, y =>
            {
                var vals = new byte[9];
                for (var x = 0; x < src.Width; x++)
                    for (var ch = 0; ch < 3; ch++)
                    {
                        if (src.Alpha(y * src.Width + x) == 0) continue;
                        var n = 0;
                        for (var dy = -1; dy <= 1; dy++)
                            for (var dx = -1; dx <= 1; dx++)
                            {
                                var index = Math.Clamp(y + dy, 0, src.Height - 1) * src.Width + Math.Clamp(x + dx, 0, src.Width - 1);
                                if (src.Alpha(index) > 0) vals[n++] = src.Rgba[index * 4 + ch];
                            }
                        Array.Sort(vals, 0, n);
                        dest.Rgba[(y * src.Width + x) * 4 + ch] = vals[n / 2];
                    }
            });
            im = dest;
        }

        var radius = s.Number("preblur", .02);
        if (radius > .01)
            im = Gaussian(im, radius, token);
        if (s.Bool("edge_preserve", true))
        {
            var blurred = Gaussian(im, .85, token);
            for (var i = 0; i < im.Rgba.Length; i++)
                if (i % 4 != 3 && im.Alpha(i / 4) > 0)
                {
                    var delta = im.Rgba[i] - blurred.Rgba[i];
                    if (Math.Abs(delta) > 3)
                        im.Rgba[i] = (byte)Math.Clamp((int)Math.Round(im.Rgba[i] + 1.45 * delta), 0, 255);
                }
        }

        return im;
    }

    private static PixelImage Gaussian(PixelImage im, double radius, CancellationToken token)
    {
        var r = Math.Clamp((int)Math.Ceiling(radius * 3), 1, 12);
        var weights = Enumerable.Range(-r, 2 * r + 1).Select(x => Math.Exp(-x * x / (2 * radius * radius))).ToArray();
        var sum = weights.Sum();
        for (var i = 0; i < weights.Length; i++)
            weights[i] /= sum;
        var tmp = im.Clone();
        var result = im.Clone();
        for (var pass = 0; pass < 2; pass++)
        {
            var input = pass == 0 ? im : tmp;
            var output = pass == 0 ? tmp : result;
            Parallel.For(0, im.Height, new ParallelOptions { CancellationToken = token }, y =>
            {
                for (var x = 0; x < im.Width; x++)
                    for (var ch = 0; ch < 3; ch++)
                    {
                        if (input.Alpha(y * im.Width + x) == 0) continue;
                        double v = 0, total = 0;
                        for (var k = -r; k <= r; k++)
                        {
                            var xx = pass == 0 ? Math.Clamp(x + k, 0, im.Width - 1) : x;
                            var yy = pass == 1 ? Math.Clamp(y + k, 0, im.Height - 1) : y;
                            var index = yy * im.Width + xx;
                            var weight = weights[k + r] * input.Alpha(index) / 255.0;
                            v += input.Rgba[index * 4 + ch] * weight;
                            total += weight;
                        }

                        output.Rgba[(y * im.Width + x) * 4 + ch] = (byte)Math.Clamp((int)Math.Round(v / Math.Max(1e-12, total)), 0, 255);
                    }
            });
        }

        return result;
    }
}

public static class Quantization
{
    public static int Nearest(Lab color, Lab[] candidates, int[]? allowed = null)
    {
        var best = -1;
        var distance = double.MaxValue;
        foreach (var i in allowed ?? Enumerable.Range(0, candidates.Length).ToArray())
        {
            var d = color.Distance(candidates[i]);
            if (d < distance)
            {
                best = i;
                distance = d;
            }
        }

        return best;
    }

    public static int[] Map(PixelImage image, Rgb[] colors, int alphaThreshold, bool dither, CancellationToken token, int[]? allowed = null)
    {
        var labs = colors.Select(Lab.From).ToArray();
        allowed ??= Enumerable.Range(0, colors.Length).ToArray();
        var result = new int[image.Width * image.Height];
        if (!dither)
        {
            Parallel.For(0, image.Height, new ParallelOptions { CancellationToken = token }, y =>
            {
                for (var x = 0; x < image.Width; x++)
                {
                    var i = y * image.Width + x;
                    result[i] = image.Alpha(i) < alphaThreshold ? -1 : Nearest(Lab.From(image.Color(i)), labs, allowed);
                }
            });
            return result;
        }

        var errors = new double[image.Width * image.Height * 3];
        for (var y = 0; y < image.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < image.Width; x++)
            {
                var i = y * image.Width + x;
                if (image.Alpha(i) < alphaThreshold)
                {
                    result[i] = -1;
                    continue;
                }

                var c = image.Color(i);
                var rr = Math.Clamp(c.R + errors[i * 3], 0, 255);
                var gg = Math.Clamp(c.G + errors[i * 3 + 1], 0, 255);
                var bb = Math.Clamp(c.B + errors[i * 3 + 2], 0, 255);
                var k = Nearest(Lab.From(new((byte)rr, (byte)gg, (byte)bb)), labs, allowed);
                result[i] = k;
                var q = colors[k];
                foreach (var(dx, dy, f)in new[]
                {
                    (1, 0, 7.0 / 16),
                    (-1, 1, 3.0 / 16),
                    (0, 1, 5.0 / 16),
                    (1, 1, 1.0 / 16)
                }

                )
                {
                    var xx = x + dx;
                    var yy = y + dy;
                    if (xx < 0 || xx >= image.Width || yy >= image.Height)
                        continue;
                    var j = yy * image.Width + xx;
                    if (image.Alpha(j) < alphaThreshold)
                        continue;
                    errors[j * 3] += (rr - q.R) * f;
                    errors[j * 3 + 1] += (gg - q.G) * f;
                    errors[j * 3 + 2] += (bb - q.B) * f;
                }
            }
        }

        return result;
    }

    public static double Error(PixelImage image, int[] idx, Rgb[] colors)
    {
        double sum = 0;
        var count = 0;
        for (var i = 0; i < idx.Length; i++)
            if (idx[i] >= 0)
            {
                sum += Lab.From(image.Color(i)).Distance(Lab.From(colors[idx[i]]));
                count++;
            }

        return count == 0 ? 0 : Math.Sqrt(sum / count);
    }

    private sealed record Sample(Rgb Color, int Count);
    public static Rgb[] HexPalette(PixelImage image, int limit, int alphaThreshold, CancellationToken token)
    {
        // Histogram excludes transparent cells and retains original RGB values.
        var hist = new Dictionary<int, int>();
        for (var i = 0; i < image.Width * image.Height; i++)
            if (image.Alpha(i) >= alphaThreshold)
            {
                var k = image.Color(i).Key;
                hist[k] = hist.GetValueOrDefault(k) + 1;
            }

        if (hist.Count == 0)
            return[Rgb.White];
        if (hist.Count <= limit)
            return hist.OrderByDescending(x => x.Value).ThenBy(x => x.Key).Select(x => Rgb.FromKey(x.Key)).ToArray();
        var samples = hist.OrderBy(x => x.Key).Select(x => new Sample(Rgb.FromKey(x.Key), x.Value)).ToList();
        var boxes = new List<List<Sample>>
        {
            samples
        };
        static int Channel(Rgb c, int axis) => axis == 0 ? c.R : axis == 1 ? c.G : c.B;
        static (int Axis, double Score) Info(List<Sample> box)
        {
            var ranges = Enumerable.Range(0, 3).Select(a => box.Max(x => Channel(x.Color, a)) - box.Min(x => Channel(x.Color, a))).ToArray();
            var axis = Array.IndexOf(ranges, ranges.Max());
            return (axis, ranges[axis] * Math.Sqrt(box.Sum(x => (double)x.Count)));
        }

        while (boxes.Count < limit)
        {
            token.ThrowIfCancellationRequested();
            var candidate = boxes.Select((b, i) => (b, i, info: Info(b))).Where(x => x.b.Count > 1).OrderByDescending(x => x.info.Score).FirstOrDefault();
            if (candidate.b is null)
                break;
            var box = candidate.b.OrderBy(x => Channel(x.Color, candidate.info.Axis)).ThenBy(x => x.Color.Key).ToList();
            var half = box.Sum(x => (long)x.Count) / 2;
            long total = 0;
            var split = 1;
            for (var i = 0; i < box.Count - 1; i++)
            {
                total += box[i].Count;
                split = i + 1;
                if (total >= half)
                    break;
            }

            boxes[candidate.i] = box.GetRange(0, split);
            boxes.Add(box.GetRange(split, box.Count - split));
        }

        static Rgb Mean(IEnumerable<Sample> box)
        {
            double count = 0, r = 0, g = 0, b = 0;
            foreach (var x in box)
            {
                count += x.Count;
                r += x.Color.R * x.Count;
                g += x.Color.G * x.Count;
                b += x.Color.B * x.Count;
            }

            return new((byte)Math.Round(r / count), (byte)Math.Round(g / count), (byte)Math.Round(b / count));
        }

        var colors = boxes.Select(Mean).ToArray();
        // Weighted Lloyd refinement in perceptual Lab space; deterministic output.
        for (var pass = 0; pass < 3; pass++)
        {
            token.ThrowIfCancellationRequested();
            var labs = colors.Select(Lab.From).ToArray();
            var sums = new double[colors.Length, 4];
            var ids = Enumerable.Range(0, colors.Length).ToArray();
            foreach (var s in samples)
            {
                var k = Nearest(Lab.From(s.Color), labs, ids);
                sums[k, 0] += s.Count;
                sums[k, 1] += s.Color.R * s.Count;
                sums[k, 2] += s.Color.G * s.Count;
                sums[k, 3] += s.Color.B * s.Count;
            }

            for (var k = 0; k < colors.Length; k++)
                if (sums[k, 0] > 0)
                    colors[k] = new((byte)Math.Round(sums[k, 1] / sums[k, 0]), (byte)Math.Round(sums[k, 2] / sums[k, 0]), (byte)Math.Round(sums[k, 3] / sums[k, 0]));
        }

        return colors.Distinct().ToArray();
    }
}
