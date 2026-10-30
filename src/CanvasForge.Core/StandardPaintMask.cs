namespace CanvasForge.Core;

// Pixel indices are half-open. Pixel centers are integer screen coordinates;
// physical boundaries are FirstPixel-.5 and EndPixelExclusive-.5, not a fit.
public sealed record StandardPaintMaskRow(int Along, int Runs, int? FirstPixel, int? EndPixelExclusive, string? Reason)
{
    public StandardEdgeSection? Edges => Reason is null && FirstPixel is { } first && EndPixelExclusive is { } end
        ? new(Along,first-.5,end-.5) : null;
}

public sealed class StandardPaintMaskResult
{
    internal StandardPaintMaskResult(Rgb requested, int matches, int wrong, List<BrushSpan> spans, List<StandardPaintMaskRow> rows)
    { Requested = requested; MatchingPixels = matches; StrongNonMatchingPixels = wrong;
      Spans = Array.AsReadOnly(spans.ToArray()); Rows = Array.AsReadOnly(rows.ToArray()); }
    public string Revision => StandardPaintMask.Revision;
    public Rgb Requested { get; }
    public int MatchingPixels { get; }
    public int StrongNonMatchingPixels { get; }
    public IReadOnlyList<BrushSpan> Spans { get; }
    public IReadOnlyList<StandardPaintMaskRow> Rows { get; }
    public int MissingRows => Rows.Count(r=>r.Runs==0);
    public int DisconnectedRows => Rows.Count(r=>r.Runs>1);
    public int ClippedRows => Rows.Count(r=>r.Reason=="mask_reaches_search_boundary");
    public int CompleteRows => Rows.Count(r=>r.Edges is not null);
    public bool Complete => CompleteRows==Rows.Count;
    public string Status => Complete ? "RAW_COLOR_MASK_MEASURED" : "INCOMPLETE";
    public bool PhysicalQualityCertified => false;
    public bool ProductionEligible => false;
}

// An explicit requested-RGB observation, never learned from the after frame.
// This is not held-out Solid, route eligibility, or permission to paint.
public static class StandardPaintMask
{
    public const string Revision = "native-requested-rgb12-contrast80-mask-v1";
    public const int ColorTolerance = 12;
    public const int ContrastThreshold = 80;

    public static StandardPaintMaskResult Measure(PixelImage baseline, PixelImage observed, ScreenRect canvas,
        ScreenRect region, ScreenLine axis, Rgb requested)
    {
        ArgumentNullException.ThrowIfNull(baseline); ArgumentNullException.ThrowIfNull(observed);
        long w=(long)canvas.Right-canvas.Left,h=(long)canvas.Bottom-canvas.Top;
        if(w<1||h<1||w*h>16_000_000||w!=baseline.Width||h!=baseline.Height||observed.Width!=w||observed.Height!=h
            ||region.Right<=region.Left||region.Bottom<=region.Top||region.Left<canvas.Left||region.Right>canvas.Right
            ||region.Top<canvas.Top||region.Bottom>canvas.Bottom
            ||axis.X1!=axis.X2&&axis.Y1!=axis.Y2||axis.X1==axis.X2&&axis.Y1==axis.Y2)
            throw new InvalidDataException("Invalid native mask geometry.");
        bool vertical=axis.X1==axis.X2;
        int first=vertical?Math.Min(axis.Y1,axis.Y2):Math.Min(axis.X1,axis.X2);
        int last=vertical?Math.Max(axis.Y1,axis.Y2):Math.Max(axis.X1,axis.X2);
        bool Inside(int x,int y)=>x>=region.Left&&x<region.Right&&y>=region.Top&&y<region.Bottom;
        if((long)last-first>32768||!Inside(axis.X1,axis.Y1)||!Inside(axis.X2,axis.Y2))
            throw new InvalidDataException("Mask target is outside the observed region.");
        int matches=0,wrong=0;var spans=new List<BrushSpan>();
        var selected=new bool[checked(region.Width*region.Height)];
        for(int y=region.Top;y<region.Bottom;y++)
        {
            int? start=null; // Negative screen coordinates are valid pixels.
            for(int x=region.Left;x<region.Right;x++)
            {
                int i=(y-canvas.Top)*baseline.Width+x-canvas.Left;
                if(baseline.Alpha(i)!=255||observed.Alpha(i)!=255)
                    throw new InvalidDataException("Native RGB mask requires opaque baseline and observed pixels.");
                var actual=observed.Color(i);
                bool strong=RustSlider.Delta(baseline.Color(i),actual)>ContrastThreshold;
                bool color=RustSlider.Delta(actual,requested)<=ColorTolerance;
                bool on=strong&&color;
                selected[(y-region.Top)*region.Width+x-region.Left]=on;
                if(on){matches++;start??=x;}
                else {if(strong)wrong++;if(start is { } begin){spans.Add(new(y,begin,x));start=null;}}
            }
            if(start is { } remaining)spans.Add(new(y,remaining,region.Right));
        }
        var rows=new List<StandardPaintMaskRow>();
        int min=vertical?region.Left:region.Top,max=vertical?region.Right:region.Bottom;
        for(int along=first;;along++)
        {
            int runs=0,lo=0,hi=0;bool was=false;
            for(int cross=min;cross<max;cross++)
            {
                int x=vertical?cross:along,y=vertical?along:cross;
                bool on=selected[(y-region.Top)*region.Width+x-region.Left];
                if(on){if(!was){if(runs==0)lo=cross;runs++;}hi=cross+1;}was=on;
            }
            string? reason=runs==0?"no_requested_color_match":runs>1?"disconnected_requested_color_mask":
                lo==min||hi==max?"mask_reaches_search_boundary":null;
            rows.Add(new(along,runs,reason is null?lo:null,reason is null?hi:null,reason));
            if(along==last)break; // Avoid overflow at Int32.MaxValue.
        }
        return new(requested,matches,wrong,spans,rows);
    }
}
