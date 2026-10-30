namespace CanvasForge.Core;

// Observation coordinates, deliberately separate from a physical stamp height.
public sealed record TexelRowObservation(double Scale, double PoleY, double TopPhase, double BottomPhase, int EdgeLabelSpan);
public sealed record TexelRowInputMapping(double ScaleOffset, double Anchor, double Phase, int IndexOffset);
public sealed record TexelRowAudit(int? CommonIndex, double MinimumMargin);

public sealed class TexelGridPath
{
    public IReadOnlyList<ScreenPoint> Points { get; }
    public int? Row { get; }
    public double? MinimumRowMargin { get; }
    public string GridId { get; }
    public bool DispatchAllowed => false;
    public bool ProductionEligible => false;
    internal TexelGridPath(string id, ScreenPoint[] points, int? row, double? margin)
    { GridId=id; Points=Array.AsReadOnly(points.ToArray()); Row=row; MinimumRowMargin=margin; }
}

// An explicit experimental hypothesis. No Settings/START integration or
// assertion that effective periods equal a native texture resolution.
public sealed class TexelGrid
{
    public const string Revision = "texel-research-grid-v1";
    public StandardTexelColumnHypothesis Columns { get; }
    public TexelRowObservation Rows { get; }
    public ScreenRect RowDomain { get; }
    public StandardBandResponseContext Context => Columns.Context;
    public IReadOnlyList<TexelRowInputMapping> InputFamily { get; }
    public string RowSourceSha256 { get; }
    public string InputFamilySourceSha256 { get; }
    public string Id { get; }
    public bool Experimental => true;
    public bool DispatchAllowed => false;
    public bool ProductionEligible => false;
    public bool PhysicalQualityCertified => false;

    public TexelGrid(bool experimental, StandardTexelColumnHypothesis columns,
        TexelRowObservation rows, ScreenRect rowDomain, IEnumerable<TexelRowInputMapping> inputFamily,
        string rowSourceSha256, string inputFamilySourceSha256)
    {
        ArgumentNullException.ThrowIfNull(columns); ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(inputFamily);
        var family=inputFamily.Take(4097).ToArray(); var c=columns.Context.Geometry.Canvas;
        if(!experimental || family.Length is <1 or >4096 || family.Distinct().Count()!=family.Length
            || !StandardPhysicalQuality.Hash(rowSourceSha256) || !StandardPhysicalQuality.Hash(inputFamilySourceSha256)
            || !rowDomain.Valid || rowDomain.Left<c.Left || rowDomain.Right>c.Right || rowDomain.Top<c.Top || rowDomain.Bottom>c.Bottom
            || rowDomain.Width>32768 || rowDomain.Height>32768
            || new[]{rows.Scale,rows.PoleY,rows.TopPhase,rows.BottomPhase}.Any(v=>!double.IsFinite(v))
            || rows.Scale<=0 || rows.PoleY<=rowDomain.Bottom || Math.Abs(rows.TopPhase)>.5 || Math.Abs(rows.BottomPhase)>.5
            || rows.EdgeLabelSpan is <1 or >32768 || rows.EdgeLabelSpan+rows.BottomPhase-rows.TopPhase<=0)
            throw new InvalidDataException("Invalid experimental grid, provenance or observation domain.");
        foreach(var f in family)
        {
            if(new[]{f.ScaleOffset,f.Anchor,f.Phase}.Any(v=>!double.IsFinite(v)) || f.ScaleOffset<=-1
                || f.Phase is <0 or >=1 || Math.Abs((long)f.IndexOffset)>32768)
                throw new InvalidDataException("Invalid row-input family.");
            foreach(int y in new[]{rowDomain.Top,rowDomain.Bottom-1})
            {
                double a=y+f.ScaleOffset*(y-f.Anchor), derivative=rows.Scale*(1+f.ScaleOffset)/Math.Pow(1-a/rows.PoleY,2);
                if(!double.IsFinite(a)||a>=rows.PoleY||!double.IsFinite(derivative)||derivative<=0||derivative>=1)
                    throw new InvalidDataException("Unbounded or non-monotone row hypothesis.");
            }
        }
        Columns=columns; Rows=rows; RowDomain=rowDomain; InputFamily=Array.AsReadOnly(family);
        RowSourceSha256=rowSourceSha256; InputFamilySourceSha256=inputFamilySourceSha256;
        Id=GeometryHash.Of(new{Revision,implementation=GeometryHash.ImplementationSha256,columns.Id,rows,rowDomain,family,rowSourceSha256,inputFamilySourceSha256});
    }
    public void ValidateContext(StandardBandResponseContext actual)
    {
        ArgumentNullException.ThrowIfNull(actual); actual.Validate();
        if(actual.Id!=Context.Id)throw new InvalidDataException("Texel context changed; no transfer or extrapolation.");
    }
    private void Point(ScreenPoint p)
    {
        if(p.X<RowDomain.Left||p.X>=RowDomain.Right||p.Y<RowDomain.Top||p.Y>=RowDomain.Bottom)
            throw new InvalidDataException("Unmeasured row coordinate.");
    }
    public double ObservationCoordinate(double y)
    {
        if(!double.IsFinite(y)||y<RowDomain.Top-.5||y>RowDomain.Bottom-.5)
            throw new InvalidDataException("Observation outside row domain.");
        return Rows.Scale*y/(1-y/Rows.PoleY);
    }
    private double Inverse(double coordinate)
    {
        double divisor=Rows.Scale+coordinate/Rows.PoleY, y=coordinate/divisor;
        if(!double.IsFinite(y)||divisor<=0)throw new InvalidDataException("Singular row inverse.");
        return y;
    }
    public StandardEdgeSection ProjectedRowEdges(int row,int x)
    {
        Point(new(x,RowDomain.Top));
        double low=Inverse(row+Rows.TopPhase),high=Inverse((double)row+Rows.EdgeLabelSpan+Rows.BottomPhase);
        if(low<RowDomain.Top-.5||high>RowDomain.Bottom-.5||low>=high)
            throw new InvalidDataException("Projected observation outside support.");
        return new(x,low,high);
    }
    public TexelRowAudit AuditRow(ScreenPoint point)
    {
        Point(point); int? common=null; bool agrees=true; double minimum=.5;
        foreach(var f in InputFamily)
        {
            double a=point.Y+f.ScaleOffset*(point.Y-f.Anchor),u=Rows.Scale*a/(1-a/Rows.PoleY)+f.Phase;
            double raw=Math.Floor(u)+f.IndexOffset;
            if(!double.IsFinite(raw)||raw<int.MinValue||raw>int.MaxValue)throw new InvalidDataException("Row index overflow.");
            int index=(int)raw; if(common is null)common=index; else if(common!=index)agrees=false;
            double frac=u-Math.Floor(u); minimum=Math.Min(minimum,Math.Min(frac,1-frac));
        }
        return new(agrees?common:null,minimum);
    }
    public TexelGridPath HorizontalAtScreenY(StandardBandResponseContext actual,int y,int firstX,int lastX,int? expectedRow,double minimumMargin)
    {
        ValidateContext(actual);
        if(firstX>lastX || (long)lastX-firstX>32767 || !double.IsFinite(minimumMargin) || minimumMargin is <0 or >=.5
            || expectedRow is null&&minimumMargin!=0)
            throw new InvalidDataException("Invalid screen-dense research route.");
        Point(new(firstX,y)); Point(new(lastX,y));
        var audit=AuditRow(new(firstX,y));
        if(expectedRow is not null&&(audit.CommonIndex!=expectedRow||audit.MinimumMargin<minimumMargin))
            throw new InvalidDataException("Input family does not support the requested row/margin.");
        var points=Enumerable.Range(firstX,lastX-firstX+1).Select(x=>new ScreenPoint(x,y)).ToArray();
        return new(Id,points,expectedRow,expectedRow is null?null:audit.MinimumMargin);
    }
    private int CenteredY(int row,double minimum)
    {
        double lower=RowDomain.Top,upper=RowDomain.Bottom;
        foreach(var f in InputFamily)
        {
            double Solve(double index)=>(Inverse(index-f.Phase)+f.ScaleOffset*f.Anchor)/(1+f.ScaleOffset);
            lower=Math.Max(lower,Solve((double)row-f.IndexOffset));
            upper=Math.Min(upper,Solve((double)row+1-f.IndexOffset));
        }
        if(lower>upper||!double.IsFinite(lower)||!double.IsFinite(upper))throw new InvalidDataException("No common integer row command.");
        int first=Math.Max(RowDomain.Top,(int)Math.Floor(lower)-1),last=Math.Min(RowDomain.Bottom-1,(int)Math.Ceiling(upper)+1);
        int? best=null; double margin=-1;
        for(int y=first;y<=last;y++)
        {
            var audit=AuditRow(new(RowDomain.Left,y));
            if(audit.CommonIndex==row&&audit.MinimumMargin>margin){margin=audit.MinimumMargin;best=y;}
        }
        if(best is null||margin<minimum)throw new InvalidDataException("No jointly supported integer command with the requested margin.");
        return best.Value;
    }
    public TexelGridPath CenteredRowScreenSpan(StandardBandResponseContext actual,int row,int firstX,int lastX,double rowMargin)
    {
        ValidateContext(actual);
        if(!double.IsFinite(rowMargin)||rowMargin is <0 or >=.5)throw new InvalidDataException("Invalid row margin.");
        return HorizontalAtScreenY(actual,CenteredY(row,rowMargin),firstX,lastX,row,rowMargin);
    }
    private int CenteredX(int column,int y,double minimum)
    {
        var p=Columns.Parameters; double left=p.LeftIntercept+p.LeftSlope*y,w=p.RightIntercept+p.RightSlope*y-left;
        double x=(w*(column+.5-p.CursorPhase)/p.EffectiveColumns+left+p.InputScaleOffset*p.InputAnchor)/(1+p.InputScaleOffset);
        if(!double.IsFinite(x)||x<int.MinValue||x>int.MaxValue)throw new InvalidDataException("Column inverse overflow.");
        int rounded=(int)Math.Round(x,MidpointRounding.ToEven); var point=new ScreenPoint(rounded,y);
        if(Columns.PredictedColumn(point)!=column||Columns.PredictedMargin(point)<minimum)
            throw new InvalidDataException("Column centre lacks measured support or margin.");
        return rounded;
    }
    public TexelGridPath CenteredHorizontal(StandardBandResponseContext actual,int row,int firstColumn,int lastColumn,double rowMargin,double columnMargin)
    {
        ValidateContext(actual);
        if(firstColumn>lastColumn||!double.IsFinite(rowMargin)||!double.IsFinite(columnMargin)||rowMargin is <0 or >=.5||columnMargin is <0 or >=.5)
            throw new InvalidDataException("Invalid experimental logical route.");
        int y=CenteredY(row,rowMargin),a=CenteredX(firstColumn,y,columnMargin),b=CenteredX(lastColumn,y,columnMargin);
        return HorizontalAtScreenY(actual,y,a,b,row,rowMargin);
    }
}

// Immutable resampling in an explicitly declared research index rectangle.
// Its dimensions are a requested target, not a discovered Rust resolution.
public sealed class TexelTarget
{
    private readonly byte[] rgba;
    public ScreenRect Indices { get; }
    public string SourceSha256 { get; }
    public string Id { get; }
    public bool ProductionEligible => false;
    private TexelTarget(ScreenRect indices,byte[] pixels,string source)
    {Indices=indices;rgba=pixels;SourceSha256=source;Id=GeometryHash.Of(new{revision="texel-target-nearest-v1",indices,source,pixels});}
    public PixelImage Snapshot()=>new(Indices.Width,Indices.Height,rgba.ToArray());
    public static TexelTarget Resample(bool experimental,PixelImage source,ScreenRect indices)
    {
        ArgumentNullException.ThrowIfNull(source);
        if(!experimental||!indices.Valid||indices.Left<0||indices.Top<0||indices.Right>32768||indices.Bottom>32768||(long)indices.Width*indices.Height>1_000_000)
            throw new InvalidDataException("Invalid experimental texel target domain.");
        var output=new PixelImage(indices.Width,indices.Height);
        for(int y=0;y<indices.Height;y++)for(int x=0;x<indices.Width;x++)
        {
            int sx=(int)(((2L*x+1)*source.Width)/(2L*indices.Width)),sy=(int)(((2L*y+1)*source.Height)/(2L*indices.Height));
            int at=sy*source.Width+sx; output.Set(y*indices.Width+x,source.Color(at),source.Alpha(at));
        }
        return new(indices,output.Rgba,GeometryHash.Of(new{source.Width,source.Height,source.Rgba}));
    }
}
public sealed record TexelLogicalRun(int Row,int FirstColumn,int LastColumn,Rgb Color,TexelGridPath Path);
public sealed class TexelPlan
{
    public IReadOnlyList<TexelLogicalRun> Runs { get; }
    public string Id { get; }
    public bool DispatchAllowed => false;
    public bool ProductionEligible => false;
    public int EstablishedSolidPixels => 0;
    internal TexelPlan(string target,string grid,TexelLogicalRun[] runs)
    {Runs=Array.AsReadOnly(runs.ToArray());Id=GeometryHash.Of(new{revision="texel-logical-runs-v1",target,grid,runs=runs.Select(r=>new{r.Row,r.FirstColumn,r.LastColumn,r.Color,points=r.Path.Points})});}
}
public static class TexelPlanner
{
    public static TexelPlan Plan(bool experimental,TexelGrid grid,StandardBandResponseContext actual,TexelTarget target,double rowMargin,double columnMargin)
    {
        ArgumentNullException.ThrowIfNull(grid);ArgumentNullException.ThrowIfNull(target);
        if(!experimental)throw new InvalidDataException("Experimental mode must be explicit.");
        grid.ValidateContext(actual);
        if(!double.IsFinite(rowMargin)||!double.IsFinite(columnMargin)||rowMargin is <0 or >=.5||columnMargin is <0 or >=.5)throw new InvalidDataException("Invalid planner margins.");
        var image=target.Snapshot();var box=target.Indices;var runs=new List<TexelLogicalRun>();
        for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;)
        {
            int at=y*image.Width+x;byte alpha=image.Alpha(at);
            if(alpha is not 0 and not 255)throw new InvalidDataException("Partial-alpha paint/history contract is unproven.");
            if(alpha==0){x++;continue;}
            int start=x;var color=image.Color(at);x++;
            while(x<image.Width&&image.Alpha(y*image.Width+x)==255&&image.Color(y*image.Width+x)==color)x++;
            var path=grid.CenteredHorizontal(actual,box.Top+y,box.Left+start,box.Left+x-1,rowMargin,columnMargin);
            runs.Add(new(box.Top+y,box.Left+start,box.Left+x-1,color,path));
        }
        return new(target.Id,grid.Id,runs.ToArray());
    }
}
