namespace CanvasForge.Core;

public readonly record struct SliderObservation(ScreenRect Track, double Fraction)
{
    public ScreenRect ValueField { get; init; }
    // Subpixel rasterisation error, rather than a 12% tolerance
    // that silently accepts a different brush size or opacity.
    public bool Matches(double desired) => Math.Abs(Fraction - desired) <= .75 / Math.Max(1, Track.Width);
    public ScreenPoint Point(double fraction) => new(
        Track.Left + (int)Math.Round(Math.Clamp(fraction, 0, 1) * (Track.Width - 1)), Track.Center.Y);
}

public sealed record SliderRowRejection(int Row,int Seed,string Reason,ScreenRect? Band=null,double? Observed=null);
public sealed record SliderReadDiagnosis(ScreenRect Area,IReadOnlyList<SliderObservation> Found,
    IReadOnlyList<SliderRowRejection> Rejections,string Reason);

public static class RustSlider
{
    public static SliderObservation Capture(PixelImage image, ScreenRect selection)
    {
        var found = Find(image, selection);
        if (found.Count == 0)
            throw new InvalidOperationException("Не знайдено повзунок. Обведи всю зелену смугу та числове поле справа із запасом.");
        if (found.Count > 1)
            throw new InvalidOperationException("У рамці кілька повзунків. Обведи один повзунок із запасом.");
        return found[0];
    }

    // Scan the whole selection: its centre may be a label or blank padding.
    // Require complete bars inside the selection, and ignore clipped neighbours.
    public static IReadOnlyList<SliderObservation> Find(PixelImage image, ScreenRect selection)
        =>Find(image,selection,null);
    private static IReadOnlyList<SliderObservation> Find(PixelImage image, ScreenRect selection,Action<SliderRowRejection>? reject)
    {
        if (!selection.Valid || selection.Left < 0 || selection.Top < 0
            || selection.Right > image.Width || selection.Bottom > image.Height) return [];
        var found = new List<SliderObservation>();
        for (var y = selection.Top + 2; y < selection.Bottom - 2; y += 2)
        {
            var x = selection.Left;
            while (x < selection.Right)
            {
                if (!Green(image.Color(y * image.Width + x))) { x++; continue; }
                var left = x;
                while (x < selection.Right && Green(image.Color(y * image.Width + x))) x++;
                if (x - left < 80) continue;
                if (found.Any(old => y >= old.Track.Top && y < old.Track.Bottom
                    && Math.Abs(left - old.Track.Left) <= 3)) continue;
                var seed = (left + x) / 2;
                var read = ReadRow(image, selection, y, seed,reject);
                if (read is not { } observation) continue;
                // Read a consistent central row after locating the bar. The
                // user's padding must not change sampling at compressed edges.
                var centred = ReadRow(image, selection, observation.Track.Center.Y, seed,reject);
                if (centred is { } stable) observation = stable;
                if (found.Any(old => Math.Abs(old.Track.Left - observation.Track.Left) <= 3
                    && Math.Abs(old.Track.Top - observation.Track.Top) <= 3)) continue;
                found.Add(observation);
            }
        }
        return found.OrderBy(x => x.Track.Top).ThenBy(x => x.Track.Left).ToArray();
    }

    // Rust places an editable numeric field after the interactive track. Read
    // its darker background separately; green digits must never count as fill.
    // The hint is relative to image, and may be a legacy partial capture.
    public static SliderObservation? Read(PixelImage image, ScreenRect hint)
    {
        if (!hint.Valid) return null;
        // Runtime/legacy hints may contain only the interactive track.
        var area = ReadArea(image,hint);
        var found = Find(image, area);
        return found.Count == 1 ? found[0] : null;
    }

    private static ScreenRect ReadArea(PixelImage image,ScreenRect hint)=>new(
        Math.Max(0,hint.Left-48),Math.Max(0,hint.Top-12),
        Math.Min(image.Width,hint.Right+120),Math.Min(image.Height,hint.Bottom+12));

    // Diagnostics run against the same screenshot and predicates as Read.
    // They explain rejection without broadening which bars can be accepted.
    public static SliderReadDiagnosis Diagnose(PixelImage image,ScreenRect hint)
    {
        var area=ReadArea(image,hint);
        if(!hint.Valid||!area.Valid)return new(area,[],[],"invalid_hint");
        var rejected=new List<SliderRowRejection>();
        var found=Find(image,area,r=>
        {
            if(rejected.Count<32&&!rejected.Any(old=>old.Reason==r.Reason&&old.Band==r.Band))rejected.Add(r);
        });
        string reason=found.Count>1?"ambiguous_bands":found.Count==1?"accepted":
            rejected.Count==0?"no_green_band":rejected[0].Reason;
        return new(area,found,rejected,reason);
    }

    private static SliderObservation? ReadRow(PixelImage image, ScreenRect area, int cy, int seed,Action<SliderRowRejection>? reject=null)
    {
        SliderObservation? Reject(string reason,ScreenRect? band=null,double? observed=null)
        {reject?.Invoke(new(cy,seed,reason,band,observed));return null;}
        var columns = new Rgb[image.Width];
        for (var x = area.Left; x < area.Right; x++)
            columns[x] = Median(Enumerable.Range(cy - 2, 5).Select(y => image.Color(y * image.Width + x)));
        if (!Green(columns[seed])) return Reject("seed_not_green");
        var left = seed;
        while (left > area.Left && Green(columns[left - 1])) left--;
        var right = seed + 1;
        while (right < area.Right && Green(columns[right])) right++;
        var top = cy;
        while (top > 0 && Green(image.Color((top - 1) * image.Width + seed))) top--;
        var bottom = cy + 1;
        while (bottom < image.Height && Green(image.Color(bottom * image.Width + seed))) bottom++;
        // A clipped capture cannot establish endpoints safely.
        var bounds=new ScreenRect(left,top,right,bottom);
        if (left <= area.Left || right >= area.Right || top <= area.Top || bottom >= area.Bottom)
            return Reject("clipped_endpoints",bounds);
        if(right-left<80)return Reject("band_too_narrow",bounds,right-left);
        if(bottom-top<8||bottom-top>96)return Reject("band_height",bounds,bottom-top);
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
        if (fieldFraction is < .10 or > .40)return Reject("numeric_field_fraction",bounds,fieldFraction);
        if(end-left<40)return Reject("track_too_short",bounds,end-left);
        var filled = 0;
        for (var x = 0; x < end - left; x++)
        {
            if (colors[x].G <= field.G * 1.45) break;
            filled++;
        }
        // A second bright run is not a valid left-to-right slider fill.
        if (colors.Skip(filled + 3).Take(end - left - filled - 3).Any(c => c.G > field.G * 1.45))
            return Reject("noncontiguous_fill",bounds,filled);
        return new(new(left, top, end, bottom), filled / (double)(end - left))
            { ValueField = new(end, top, right, bottom) };
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
