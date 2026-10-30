namespace CanvasForge.Core;

public sealed record StandardInverseRow(int Along,int CommandShift,double PredictedLow,double PredictedHigh);
public sealed record StandardInverseScore(double MaximumTwoEdgeErrorPx,double MaximumAdjacentEdgeStepPx,double WidthRangePx);
public sealed record StandardInverseCandidate(string Revision,string ImplementationSha256,string ContextId,string LowModelId,string HighModelId,
    ScreenLine Target,double TargetWidth,int PrefixLength,int CalibrationShift,StandardInverseScore Prediction,
    IReadOnlyList<StandardInverseRow> Rows,IReadOnlyList<IReadOnlyList<ScreenPoint>> Paths,string Id)
{
    public bool PhysicalQualityCertified=>false;
    public bool ProductionEligible=>false;
    public bool DispatchAllowed=>false;
    public int EstablishedSolidPixels=>0;
    public string PredictionContract=>"exploratory row-local linear interpolation between two observed COMPLETE band prefixes; not a measured corrected stroke";
    public string UnvalidatedEffects=>"intermediate cross positions; longitudinal response to turns; full RGB/Possible; caps; overlap and changed input path";
}

// Invert the joint two-edge response of a COMPLETE band. All tracks share the
// same cross correction and retain their observed spacing/order. This is not
// independent single-footprint translation. The interpolation is an explicit
// hypothesis and must not be dispatched by the ordinary painter.
public static class StandardBandInverse
{
    public const string Revision="standard-joint-band-inverse-hypothesis-v1";
    public const string Policy="per prefix: full-body minimax two-edge error, then minimax adjacent-edge step, then sum squared two-edge error, then command total variation; across prefixes: maximum two-edge error, adjacent step, width range, prefix length; unit-connected common warp; no endpoint ramps";
    private const double Epsilon=1e-10;
    private sealed record Cell(double Low,double High,double Error,double Square);
    private sealed record Solution(StandardInverseRow[] Rows,StandardInverseScore Score,int[] Shifts);
    public static StandardInverseCandidate? Build(StandardBandResponse control,StandardBandResponse shifted,double targetWidth)
    {
        if(!double.IsFinite(targetWidth)||targetWidth<=0||targetWidth>64)throw new InvalidDataException("Invalid inverse band width.");
        control.Context.Validate();shifted.Context.Validate();
        if(control.Context.Id!=shifted.Context.Id||control.Target!=shifted.Target||control.States.Count==0||control.States.Count!=shifted.States.Count)
            throw new InvalidDataException("Inverse calibration needs matching complete context and target.");
        var target=control.Target;bool vertical=target.X1==target.X2;int Cross(ScreenLine l)=>vertical?l.X1:l.Y1;
        int delta=Cross(shifted.States[0].Commands[0])-Cross(control.States[0].Commands[0]);
        if(delta==0)return null; // identical placements contain no inverse calibration information
        if(Math.Abs(delta)>8)throw new InvalidDataException("Inverse calibration requires a bounded measured cross displacement.");
        for(int k=0;k<control.States.Count;k++)
        {
            var a=control.States[k];var b=shifted.States[k];
            if(a.PrefixLength!=k+1||b.PrefixLength!=k+1||a.Commands.Count!=b.Commands.Count
                ||a.Commands.Where((l,i)=>b.Commands[i]!=(vertical?l with{X1=l.X1+delta,X2=l.X2+delta}:l with{Y1=l.Y1+delta,Y2=l.Y2+delta})).Any())
                throw new InvalidDataException("Calibration prefix order or common shift changed.");
        }
        int length=TransferSchedule.Length(target);if(length>4096||control.States.Count>32)throw new InvalidDataException("Inverse route resource budget exceeded.");
        int[] shifts=Enumerable.Range(Math.Min(0,delta),Math.Abs(delta)+1).ToArray();double low=Cross(target)-targetWidth/2,high=Cross(target)+targetWidth/2;
        StandardInverseCandidate? best=null;
        for(int prefix=1;prefix<=control.States.Count;prefix++)
        {
            var a=control.States[prefix-1].Sections.Where(s=>s.Mandatory).ToArray();var b=shifted.States[prefix-1].Sections.Where(s=>s.Mandatory).ToArray();
            // A paint gap cannot be removed from the denominator or filled by interpolation.
            if(a.Length!=length+1||b.Length!=length+1||a.Where((s,i)=>s.Along!=b[i].Along||s.Edges is null||b[i].Edges is null).Any())continue;
            var table=new Cell[a.Length,shifts.Length];
            for(int y=0;y<a.Length;y++)for(int j=0;j<shifts.Length;j++)
            {
                double t=(double)shifts[j]/delta,l=a[y].Edges!.Low+(b[y].Edges!.Low-a[y].Edges!.Low)*t,h=a[y].Edges!.High+(b[y].Edges!.High-a[y].Edges!.High)*t;
                table[y,j]=new(l,h,Math.Max(Math.Abs(l-low),Math.Abs(h-high)),(l-low)*(l-low)+(h-high)*(h-high));
            }
            var solution=Solve(table,shifts,a.Select(s=>s.Along).ToArray());var paths=new List<IReadOnlyList<ScreenPoint>>();
            foreach(var command in control.States[prefix-1].Commands)
            {
                int start=vertical?command.Y1:command.X1,sign=vertical?Math.Sign(command.Y2-command.Y1):Math.Sign(command.X2-command.X1),first=a[0].Along;
                var path=Enumerable.Range(0,length+1).Select(i=>
                {
                    int along=start+i*sign,shift=solution.Shifts[along-first];
                    return vertical?new ScreenPoint(command.X1+shift,along):new ScreenPoint(along,command.Y1+shift);
                }).ToArray();
                // Includes the first/last command. No unmeasured endpoint ramp.
                StandardBandContour.Validate(path,control.Context.Geometry.Canvas);
                if(path.Any(p=>!Inside(control.Reservation,p)||!Inside(shifted.Reservation,p)))throw new InvalidDataException("Corrected command exceeds the shared measured reservation.");
                paths.Add(Array.AsReadOnly(path));
            }
            var rows=Array.AsReadOnly(solution.Rows);var frozen=paths.AsReadOnly();
            string id=GeometryHash.Of(new{Revision,Policy,implementation=GeometryHash.ImplementationSha256,context=control.Context.Id,control=control.Id,shifted=shifted.Id,targetWidth,prefix,rows,paths=frozen});
            var candidate=new StandardInverseCandidate(Revision,GeometryHash.ImplementationSha256,control.Context.Id,control.Id,shifted.Id,target,targetWidth,prefix,delta,solution.Score,rows,frozen,id);
            if(best is null||Compare(candidate,best)<0)best=candidate;
        }
        return best;
    }
    private static bool Inside(ScreenRect r,ScreenPoint p)=>p.X>=r.Left&&p.X<r.Right&&p.Y>=r.Top&&p.Y<r.Bottom;
    private static int Compare(StandardInverseCandidate a,StandardInverseCandidate b)
    {
        int c=a.Prediction.MaximumTwoEdgeErrorPx.CompareTo(b.Prediction.MaximumTwoEdgeErrorPx);if(c!=0)return c;
        c=a.Prediction.MaximumAdjacentEdgeStepPx.CompareTo(b.Prediction.MaximumAdjacentEdgeStepPx);if(c!=0)return c;
        c=a.Prediction.WidthRangePx.CompareTo(b.Prediction.WidthRangePx);return c!=0?c:a.PrefixLength.CompareTo(b.PrefixLength);
    }
    private static double Step(Cell a,Cell b)=>Math.Max(Math.Abs(a.Low-b.Low),Math.Abs(a.High-b.High));
    private static Solution Solve(Cell[,] table,int[] shifts,int[] along)
    {
        int n=table.GetLength(0),m=shifts.Length;
        var cost=Enumerable.Range(0,m).Select(j=>table[0,j].Error).ToArray();
        // Pass 1: exact bottleneck error over the whole commanded body.
        for(int y=1;y<n;y++)
        {
            var next=Enumerable.Repeat(double.PositiveInfinity,m).ToArray();
            for(int j=0;j<m;j++)for(int i=Math.Max(0,j-1);i<=Math.Min(m-1,j+1);i++)next[j]=Math.Min(next[j],Math.Max(cost[i],table[y,j].Error));
            cost=next;
        }
        double bound=cost.Min();cost=Enumerable.Range(0,m).Select(j=>table[0,j].Error<=bound+Epsilon?0d:double.PositiveInfinity).ToArray();
        // Pass 2: smallest adjacent physical edge step under the first bound.
        for(int y=1;y<n;y++)
        {
            var next=Enumerable.Repeat(double.PositiveInfinity,m).ToArray();
            for(int j=0;j<m;j++)if(table[y,j].Error<=bound+Epsilon)
                for(int i=Math.Max(0,j-1);i<=Math.Min(m-1,j+1);i++)next[j]=Math.Min(next[j],Math.Max(cost[i],Step(table[y-1,i],table[y,j])));
            cost=next;
        }
        double stepBound=cost.Min();var previous=new int[n,m];var variation=new int[m];
        cost=Enumerable.Range(0,m).Select(j=>table[0,j].Error<=bound+Epsilon?table[0,j].Square:double.PositiveInfinity).ToArray();
        // Pass 3: deterministic tie-break, without a fitted penalty weight.
        for(int y=1;y<n;y++)
        {
            var next=Enumerable.Repeat(double.PositiveInfinity,m).ToArray();var nv=Enumerable.Repeat(int.MaxValue,m).ToArray();
            for(int j=0;j<m;j++)if(table[y,j].Error<=bound+Epsilon)
                for(int i=Math.Max(0,j-1);i<=Math.Min(m-1,j+1);i++)
                {
                    if(!double.IsFinite(cost[i])||Step(table[y-1,i],table[y,j])>stepBound+Epsilon)continue;
                    double square=cost[i]+table[y,j].Square;int move=variation[i]+Math.Abs(shifts[i]-shifts[j]);
                    if(square<next[j]-Epsilon||Math.Abs(square-next[j])<=Epsilon&&move<nv[j]){next[j]=square;nv[j]=move;previous[y,j]=i;}
                }
            cost=next;variation=nv;
        }
        int state=Enumerable.Range(0,m).OrderBy(j=>cost[j]).ThenBy(j=>variation[j]).ThenBy(j=>j).First();
        if(!double.IsFinite(cost[state]))throw new InvalidDataException("Inverse route is unreachable under the frozen constraints.");
        var selected=new int[n];for(int y=n-1;y>=0;y--){selected[y]=state;if(y>0)state=previous[y,state];}
        var rows=Enumerable.Range(0,n).Select(y=>new StandardInverseRow(along[y],shifts[selected[y]],table[y,selected[y]].Low,table[y,selected[y]].High)).ToArray();
        var widths=rows.Select(r=>r.PredictedHigh-r.PredictedLow).ToArray();
        double actualError=Enumerable.Range(0,n).Max(y=>table[y,selected[y]].Error);
        double actualStep=Enumerable.Range(1,n-1).Max(y=>Step(table[y-1,selected[y-1]],table[y,selected[y]]));
        return new(rows,new(actualError,actualStep,widths.Max()-widths.Min()),rows.Select(r=>r.CommandShift).ToArray());
    }
}
