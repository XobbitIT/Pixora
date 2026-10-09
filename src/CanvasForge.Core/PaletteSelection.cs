namespace CanvasForge.Core;

public sealed record PaletteTargetEvidence(ScreenRect Area, Rgb Expected, Rgb First, Rgb Second)
{
    public bool Passed => RustSlider.Delta(Expected, First) <= 12 && RustSlider.Delta(Expected, Second) <= 12
        && RustSlider.Delta(First, Second) <= 12;
}
public sealed class PaletteTargetException(PaletteTargetEvidence evidence) : InvalidOperationException(
    $"Колір палітри {evidence.Expected.Hex} змінився або координати застаріли. Повтори захоплення палітри.")
{
    public PaletteTargetEvidence Evidence {get;}=evidence;
}
public sealed class PaletteLayoutException() : InvalidOperationException("Палітра змістилася. Повтори підготовку або захоплення палітри.");

public static class PaletteSelection
{
    public static ScreenPoint ClickPoint(PaletteEntry entry, CoordinateRebase rebase, bool alreadyAligned=false)
    {
        var point=entry.ClickPoint??throw new InvalidOperationException("Capture the palette again.");
        if(alreadyAligned||rebase.IsIdentity)return point;
        var (x,y)=rebase.Map(point.X,point.Y);return new(x,y);
    }
    // A cell read proves the captured target is still under the click. It does
    // not prove the final painted RGB; an active swatch remains a separate check.
    public static ScreenRect SampleArea(Calibration c, ScreenPoint point, string source)
    {
        string key = source == "quick" ? "quick" : "palette";
        int cols = key == "quick" ? 1 : 4, rows = key == "quick" ? c.Get("quick_rows", 10) : 16;
        var rect = c.Rect(key);
        var centers = c.GridCenters(key, cols, rows);
        if (!rect.Valid || !centers.Contains(point))
            throw new PaletteLayoutException();
        int hw = Math.Max(1, (int)(rect.Width / (double)cols * .21));
        int hh = Math.Max(1, (int)(rect.Height / (double)rows * .21));
        return new(point.X - hw, point.Y - hh, point.X + hw + 1, point.Y + hh + 1);
    }

    public static PaletteTargetEvidence Verify(Calibration c, PaletteEntry entry, ScreenPoint point,
        Func<ScreenRect, Rgb> read, Action settle)
    {
        var area = SampleArea(c, point, entry.Source);
        var first = read(area); settle(); var second = read(area);
        var evidence = new PaletteTargetEvidence(area, entry.Color, first, second);
        if (!evidence.Passed)
            throw new PaletteTargetException(evidence);
        return evidence;
    }
}
