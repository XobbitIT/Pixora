namespace CanvasForge.Core;

public sealed record TexelCompensatedRun(int Row,int FirstColumn,int LastColumn,TexelGridPath Path)
{
    public int CommandY=>Path.Points[0].Y;
}

// Experimental candidate, never a certified footprint. Preserve requested mask
// separately from the immutable, forward-only physical-screen command sequence.
public sealed class TexelCompensatedPlan
{
    public string Id{get;}
    public string GridId{get;}
    public StandardBandResponseContext Context{get;}
    public ScreenRect Indices{get;}
    public IReadOnlyList<string> Mask{get;}
    public IReadOnlyList<TexelCompensatedRun> Runs{get;}
    public int EndTrimPx{get;}
    // Null for the trim-only research routes (pic2/pic3); otherwise each run ends at the first pixel with cursor phase >= b+EndPhase.
    public double? EndPhase{get;}
    public bool PhysicalQualityCertified=>false;
    public bool ProductionEligible=>false;
    public int EstablishedSolidPixels=>0;
    internal TexelCompensatedPlan(TexelGrid grid,ScreenRect indices,string[] mask,int trim,double? endPhase,TexelCompensatedRun[] runs)
    {
        GridId=grid.Id;Context=grid.Context;Indices=indices;EndTrimPx=trim;EndPhase=endPhase;
        Mask=Array.AsReadOnly(mask.ToArray());Runs=Array.AsReadOnly(runs.ToArray());
        Id=GeometryHash.Of(new{TexelCompensatedPlanner.Revision,GridId,indices,mask,trim,endPhase,
            runs=runs.Select(r=>new{r.Row,r.FirstColumn,r.LastColumn,points=r.Path.Points})});
    }
}
public static class TexelCompensatedPlanner
{
    // v2 adds the full-coverage end (pic8/pic9, 0095-0098); trim-only routes are unchanged.
    public const string Revision="texel-compensated-mask-start2-end1-two-row-v2-endphase";
    public const double MinimumMargin=.25;
    // Full coverage needs cursor phase >= b+1.617 (pic7 c339); 1.867 adds the 0.25 margin. Verified natively by pic8 and pic9.
    public const double FullCoverageEndPhase=1.867;
    public static TexelCompensatedPlan Plan(bool experimental,TexelGrid grid,StandardBandResponseContext context,
        IEnumerable<string> mask,ScreenRect indices,int endTrimPx=0,CancellationToken token=default,double? endPhase=null)
    {
        ArgumentNullException.ThrowIfNull(grid);ArgumentNullException.ThrowIfNull(mask);grid.ValidateContext(context);
        if(!experimental||!indices.Valid||indices.Left<0||indices.Top<0||indices.Right>32768||indices.Bottom>32768
            ||(long)indices.Width*indices.Height>1_000_000||endTrimPx is <0 or >8)
            throw new InvalidDataException("Invalid experimental mask bounds or end trim (integer0..8px).");
        if(endPhase is double e&&(!double.IsFinite(e)||e<1.5||e>=2.5||endTrimPx!=0))
            throw new InvalidDataException("End phase must be in [1.5,2.5) and cannot be combined with an end trim.");
        var cells=mask.Take(indices.Height+1).ToArray();
        if(cells.Length!=indices.Height||cells.Any(r=>r is null||r.Length!=indices.Width||r.Any(c=>c is not ('#' or '.'))))
            throw new InvalidDataException("Invalid compensated picture mask.");
        bool T(int x,int y)=>y>=0&&y<cells.Length&&x>=0&&x<indices.Width&&cells[y][x]=='#';
        for(int y=0;y<cells.Length;y++)
        {
            token.ThrowIfCancellationRequested();
            for(int x=0;x<indices.Width;x++)if(T(x,y)&&!T(x,y-1)&&!T(x,y+1))
                throw new InvalidDataException("Single-row detail is unsupported by the experimental two-row candidate.");
        }
        var runs=new List<TexelCompensatedRun>();
        for(int y=0;y<cells.Length;y++)
        {
            token.ThrowIfCancellationRequested();
            for(int x=0;x<indices.Width;)
            {
                if(!(T(x,y)&&T(x,y+1))){x++;continue;}
                int first=x;while(x+1<indices.Width&&T(x+1,y)&&T(x+1,y+1))x++;
                int a=indices.Left+first,b=indices.Left+x++;
                if(b+1<a+2)throw new InvalidDataException("Feature is too narrow for the experimental endpoint compensation.");
                var original=grid.CenteredHorizontal(context,indices.Top+y,a+2,b+1,MinimumMargin,MinimumMargin);
                if(original.Points.Count-endTrimPx<2)throw new InvalidDataException("Run too short for the selected end trim.");
                var points=original.Points.Take(original.Points.Count-endTrimPx).ToArray();
                if(endPhase is double end)
                {
                    var tail=points.ToList();
                    while(grid.Columns.PredictedPhase(tail[^1])<b+end)
                    {
                        var next=new ScreenPoint(tail[^1].X+1,tail[^1].Y);
                        if(next.X>=grid.RowDomain.Right)throw new InvalidDataException("Run end leaves the measured row domain.");
                        tail.Add(next);
                    }
                    points=tail.ToArray();
                }
                var path=new TexelGridPath(grid.Id,points,original.Row,original.MinimumRowMargin);
                runs.Add(new(indices.Top+y,a,b,path));
            }
        }
        if(runs.Count==0)throw new InvalidDataException("The experimental target has no paintable runs.");
        return new(grid,indices,cells,endTrimPx,endPhase,runs.ToArray());
    }
}
public static class TexelExperimentSettings
{
    public const string EnabledKey="texel_experiment_enabled",TrimKey="texel_end_trim_px",ColumnKey="texel_first_column",RowKey="texel_first_row";
    public static IReadOnlyList<string> Keys{get;}=Array.AsReadOnly(new[]{EnabledKey,TrimKey,ColumnKey,RowKey});
    public static bool Enabled(Settings s)=>s.Bool(EnabledKey);
    public static void Validate(Settings s)
    {
        foreach(var (key,fallback,max) in new[]{(TrimKey,0,8),(ColumnKey,225,32767),(RowKey,203,32767)})
        {
            double n=s.Number(key,fallback);
            if(!double.IsFinite(n)||n<0||n>max||n!=Math.Truncate(n))throw new InvalidDataException("Invalid experimental integer setting: "+key);
        }
        if(Enabled(s)&&!ReleaseProfile.Standard(s))throw new InvalidDataException("The texel experiment requires Standard Size3; disable it before switching to Adaptive.");
    }
    public static void ValidatePlan(Settings s,TexelCompensatedPlan plan)
    {
        Validate(s);
        // The texel route is measured only with the Rust palette; HEX Direct would read hex_controls (or throw) in PaintCalibration.
        if(s.Mode!=ColorMode.RustPalette)throw new InvalidDataException("The texel experiment requires Rust Palette; HEX Direct is not supported.");
        // The product always uses the full-coverage end; the legacy trim key stays readable but no longer shapes the route.
        if(!Enabled(s)||plan.EndTrimPx!=0||plan.EndPhase!=TexelCompensatedPlanner.FullCoverageEndPhase||s.Int(ColumnKey,225)!=plan.Indices.Left||s.Int(RowKey,203)!=plan.Indices.Top
            ||s.PaintCalibration().Rect("canvas")!=plan.Context.Geometry.Canvas)
            throw new InvalidDataException("Experimental plan or captured Canvas changed. Rebuild the plan.");
    }
}
