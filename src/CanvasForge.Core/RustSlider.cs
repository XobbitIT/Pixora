namespace CanvasForge.Core;

public readonly record struct SliderObservation(ScreenRect Track, double Fraction)
{
    // Subpixel rasterisation error, rather than a 12% tolerance
    // that silently accepts a different brush size or opacity.
    public bool Matches(double desired) => Math.Abs(Fraction - desired) <= .75 / Math.Max(1, Track.Width);
    public ScreenPoint Point(double fraction) => new(
        Track.Left + (int)Math.Round(Math.Clamp(fraction, 0, 1) * (Track.Width - 1)), Track.Center.Y);
}

public static class RustSlider
{
    // Rust places an editable numeric field after the interactive track. Read
    // its darker background separately; green digits must never count as fill.
    // The hint is relative to image, and may be a legacy partial capture.
    public static SliderObservation? Read(PixelImage image, ScreenRect hint)
    {
        if (!hint.Valid || hint.Center.Y < 2 || hint.Center.Y >= image.Height - 2) return null;
        var cy = hint.Center.Y;
        var columns = new Rgb[image.Width];
        for (var x = 0; x < image.Width; x++)
            columns[x] = Median(Enumerable.Range(cy - 2, 5).Select(y => image.Color(y * image.Width + x)));
        var seed = -1;
        for (var x = Math.Min(image.Width - 1, hint.Center.X); x >= Math.Max(0, hint.Left); x--)
            if (Green(columns[x])) { seed = x; break; }
        if (seed < 0) return null;
        var left = seed;
        while (left > 0 && Green(columns[left - 1])) left--;
        var right = seed + 1;
        while (right < image.Width && Green(columns[right])) right++;
        var top = cy;
        while (top > 0 && Green(image.Color((top - 1) * image.Width + seed))) top--;
        var bottom = cy + 1;
        while (bottom < image.Height && Green(image.Color(bottom * image.Width + seed))) bottom++;
        // A clipped capture cannot establish endpoints safely.
        if (left == 0 || right == image.Width || top == 0 || bottom == image.Height
            || right - left < 80 || bottom - top < 8 || bottom - top > 96) return null;
        var band = Math.Max(3, (bottom - top) / 5);
        var rows = Enumerable.Range(top + 2, band - 2)
            .Concat(Enumerable.Range(bottom - band, band - 2)).ToArray();
        var colors = new Rgb[right - left];
        for (var x = left; x < right; x++)
            colors[x - left] = Median(rows.Select(y => image.Color(y * image.Width + x)));
        var field = Median(colors.Skip(colors.Length - 8));
        var edge = colors.Length - 9;
        var misses = 0;
        for (; edge > 0; edge--)
        {
            misses = Delta(colors[edge], field) > 5 ? misses + 1 : 0;
            if (misses >= 4) break;
        }
        var end = left + edge + 4;
        var fieldFraction = (right - end) / (double)(right - left);
        if (fieldFraction is < .10 or > .40 || end - left < 40) return null;
        var filled = 0;
        for (var x = 0; x < end - left; x++)
        {
            if (colors[x].G <= field.G * 1.45) break;
            filled++;
        }
        // A second bright run is not a valid left-to-right slider fill.
        if (colors.Skip(filled + 3).Take(end - left - filled - 3).Any(c => c.G > field.G * 1.45)) return null;
        return new(new(left, top, end, bottom), filled / (double)(end - left));
    }

    private static bool Green(Rgb c) => c.R >= 25 && c.G >= 35 && c.G - c.R >= 5 && c.G - c.B >= 10;
    public static int Delta(Rgb a, Rgb b) => Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
    private static Rgb Median(IEnumerable<Rgb> source)
    {
        var colors = source.ToArray();
        byte Component(Func<Rgb, byte> selector)
        {
            var sorted = colors.Select(selector).Order().ToArray();
            return (byte)((sorted[(sorted.Length - 1) / 2] + sorted[sorted.Length / 2]) / 2);
        }
        return new(Component(c => c.R), Component(c => c.G), Component(c => c.B));
    }
}
