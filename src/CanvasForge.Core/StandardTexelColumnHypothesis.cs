namespace CanvasForge.Core;

// Empirical coordinates relative to the visible paper sides. EffectiveColumns
// is a fitted period, NOT an identified texture width or a production profile.
public sealed record StandardTexelColumnParameters(double EffectiveColumns,double LeftIntercept,double LeftSlope,
    double RightIntercept,double RightSlope,double InputScaleOffset,double InputAnchor,double CursorPhase,
    double LowEdgePhase,double HighEdgePhase,int StampColumns);

public sealed class StandardTexelColumnPath
{
    public string ModelId{get;}
    public string Id{get;}
    public int Column{get;}
    public IReadOnlyList<ScreenPoint> Points{get;}
    public double MinimumPredictedMargin{get;}
    public bool DispatchAllowed=>false;
    public bool ProductionEligible=>false;
    public bool PhysicalQualityCertified=>false;
    internal StandardTexelColumnPath(string model,int column,ScreenPoint[] points,double margin)
    {ModelId=model;Column=column;Points=Array.AsReadOnly(points.ToArray());MinimumPredictedMargin=margin;Id=GeometryHash.Of(new{model,column,points,margin});}
}

// Offline diagnostic hypothesis: commanded screen pixel -> empirical column.
// This does not move controls, rescale captures, modify target, or grant input.
// Width/caps/overlap/colour remain unqualified even when an address is constant.
public sealed class StandardTexelColumnHypothesis
{
    public const string Revision="standard-texel-column-hypothesis-v1";
    public StandardBandResponseContext Context{get;}
    public StandardTexelColumnParameters Parameters{get;}
    public ScreenRect Domain{get;}
    public string SourceEvidenceSha256{get;}
    public string Id{get;}
    public bool DispatchAllowed=>false;
    public bool ProductionEligible=>false;
    public bool PhysicalQualityCertified=>false;
    public StandardTexelColumnHypothesis(StandardBandResponseContext context,StandardTexelColumnParameters parameters,
        ScreenRect domain,string sourceEvidenceSha256)
    {
        ArgumentNullException.ThrowIfNull(context);ArgumentNullException.ThrowIfNull(parameters);context.Validate();
        var p=parameters;var c=context.Geometry.Canvas;
        double[] values=[p.EffectiveColumns,p.LeftIntercept,p.LeftSlope,p.RightIntercept,p.RightSlope,
            p.InputScaleOffset,p.InputAnchor,p.CursorPhase,p.LowEdgePhase,p.HighEdgePhase];
        if(values.Any(v=>!double.IsFinite(v))||p.EffectiveColumns<=0||p.EffectiveColumns>32768
            ||p.InputScaleOffset<=-1||p.StampColumns<1||p.StampColumns>p.EffectiveColumns
            ||p.CursorPhase is <0 or >=1||Math.Abs(p.LowEdgePhase)>.5||Math.Abs(p.HighEdgePhase)>.5
            ||p.StampColumns+p.HighEdgePhase-p.LowEdgePhase<=0
            ||!domain.Valid||domain.Left<c.Left||domain.Right>c.Right||domain.Top<c.Top||domain.Bottom>c.Bottom
            ||(long)domain.Height>32768||!StandardPhysicalQuality.Hash(sourceEvidenceSha256))
            throw new InvalidDataException("Invalid or unbounded texel-column hypothesis.");
        Context=context;Parameters=p;Domain=domain;SourceEvidenceSha256=sourceEvidenceSha256;
        foreach(int y in new[]{domain.Top,domain.Bottom-1})
            if(!double.IsFinite(Left(y))||!double.IsFinite(Width(y))||Width(y)<=0
                ||p.EffectiveColumns*(1+p.InputScaleOffset)/Width(y)>=1)
                throw new InvalidDataException("No constant-address rounding guarantee in this domain.");
        Id=GeometryHash.Of(new{Revision,implementation=GeometryHash.ImplementationSha256,context=context.Id,p,domain,sourceEvidenceSha256});
    }
    public double Left(int y)=>Parameters.LeftIntercept+Parameters.LeftSlope*y;
    public double Width(int y)=>Parameters.RightIntercept+Parameters.RightSlope*y-Left(y);
    private double Phase(ScreenPoint point)
    {
        if(point.X<Domain.Left||point.X>=Domain.Right||point.Y<Domain.Top||point.Y>=Domain.Bottom)
            throw new InvalidDataException("Texel hypothesis cannot extrapolate outside its declared screen domain.");
        var p=Parameters;double value=p.EffectiveColumns*(point.X+p.InputScaleOffset*(point.X-p.InputAnchor)-Left(point.Y))/Width(point.Y)+p.CursorPhase;
        if(!double.IsFinite(value)||value<int.MinValue||value>int.MaxValue)throw new InvalidDataException("Texel address overflow.");
        return value;
    }
    // Continuous cursor phase (floor = predicted column); the compensated planner places run ends on it.
    public double PredictedPhase(ScreenPoint point)=>Phase(point);
    public int PredictedColumn(ScreenPoint point)=>(int)Math.Floor(Phase(point));
    public double PredictedMargin(ScreenPoint point)
    {double phase=Phase(point),fraction=phase-Math.Floor(phase);return Math.Min(fraction,1-fraction);}
    public StandardEdgeSection ProjectedEdges(int column,int y)
    {
        if(y<Domain.Top||y>=Domain.Bottom||column<Parameters.StampColumns||column>=Parameters.EffectiveColumns)
            throw new InvalidDataException("Unmeasured column or row.");
        double w=Width(y),left=Left(y);var p=Parameters;
        double low=left+(column-p.StampColumns+p.LowEdgePhase)*w/p.EffectiveColumns,
            high=left+(column+p.HighEdgePhase)*w/p.EffectiveColumns;
        if(!double.IsFinite(low)||!double.IsFinite(high)||low>=high||low<Domain.Left-.5||high>Domain.Right-.5)
            throw new InvalidDataException("Projected edges leave the declared observation domain.");
        return new(y,low,high);
    }
    public StandardTexelColumnPath CenteredVertical(StandardBandResponseContext actualContext,int column,int firstY,int lastY,double minimumMargin)
    {
        ArgumentNullException.ThrowIfNull(actualContext);actualContext.Validate();
        if(actualContext.Id!=Context.Id||firstY<Domain.Top||lastY>=Domain.Bottom||firstY>=lastY
            ||column<Parameters.StampColumns||column>=Parameters.EffectiveColumns
            ||!double.IsFinite(minimumMargin)||minimumMargin is <0 or >=.5)
            throw new InvalidDataException("Texel hypothesis context, column, body, or margin changed.");
        var p=Parameters;var path=new List<ScreenPoint>();double margin=.5;
        for(int y=firstY;y<=lastY;y++)
        {
            double target=(Width(y)*(column+.5-p.CursorPhase)/p.EffectiveColumns+Left(y)+p.InputScaleOffset*p.InputAnchor)/(1+p.InputScaleOffset);
            if(!double.IsFinite(target)||target<int.MinValue||target>int.MaxValue)throw new InvalidDataException("Rounded command overflow.");
            var point=new ScreenPoint((int)Math.Round(target,MidpointRounding.ToEven),y);
            if(PredictedColumn(point)!=column||path.Count>0&&Math.Abs((long)point.X-path[^1].X)>1)
                throw new InvalidDataException("Integer path cannot maintain the declared column.");
            margin=Math.Min(margin,PredictedMargin(point));path.Add(point);
        }
        if(margin<minimumMargin)throw new InvalidDataException("Insufficient predicted margin; no clamping or best-effort path.");
        return new(Id,column,path.ToArray(),margin);
    }
}
