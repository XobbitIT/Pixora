namespace CanvasForge.Core;

public sealed record StandardDiscreteFrontier(int Along,int Command,double Edge,string StateId);
public sealed record StandardDiscreteRow(int Along,double Low,double High,string LowStateId,string HighStateId);
public sealed record StandardDiscreteScore(double MaximumTwoEdgeErrorPx,double MeanSquaredTwoEdgeErrorPx2,
    double MaximumAdjacentEdgeStepPx,double WidthRangePx);
public sealed record StandardDiscreteCandidate(string Revision,string AtlasId,ScreenLine Target,double TargetWidth,
    int FirstCommand,int LastCommand,StandardDiscreteScore Prediction,IReadOnlyList<StandardDiscreteRow> Rows,
    IReadOnlyList<IReadOnlyList<ScreenPoint>> Paths,string Id)
{
    public bool DispatchAllowed=>false;
    public bool PhysicalQualityCertified=>false;
    public bool ProductionEligible=>false;
    public int EstablishedSolidPixels=>0;
    public string PredictionContract=>"observed native low/high frontiers at exact commands; combined histories are a hypothesis, not a measured corrected band";
    public string RouteContract=>"constant straight full-length tracks only; no unmeasured turns, interpolation, endpoint ramps or translated masks";
}

// Ascending prefixes expose the high frontier; descending prefixes expose the
// low frontier. Neither edge is inferred by translating the other edge. The
// query remains exploratory: different overlap histories can affect paint.
public sealed class StandardDiscreteBand
{
    public const string Revision="standard-discrete-two-frontier-v1";
    public const string Policy="complete native mandatory rows; exact integer commands only; constant straight routes; maximum two-edge error, mean square target error, adjacent step, width range, track count";
    public StandardBandResponseContext Context{get;}
    public ScreenLine Target{get;}
    public ScreenRect Reservation{get;}
    public string AscendingModelId{get;}
    public string DescendingModelId{get;}
    public IReadOnlyList<int> Commands{get;}
    public IReadOnlyList<StandardDiscreteFrontier> LowFrontiers{get;}
    public IReadOnlyList<StandardDiscreteFrontier> HighFrontiers{get;}
    public string Id{get;}
    public bool PhysicalQualityCertified=>false;
    public bool ProductionEligible=>false;
    public bool DispatchAllowed=>false;
    private readonly Dictionary<(int Command,int Along),StandardDiscreteFrontier> low=[],high=[];
    private readonly int firstAlong,length;
    private readonly bool vertical;

    public StandardDiscreteBand(StandardBandResponse ascending,StandardBandResponse descending)
    {
        ascending.Context.Validate();descending.Context.Validate();
        if(ascending.Context.Id!=descending.Context.Id||ascending.Target!=descending.Target||ascending.Reservation!=descending.Reservation
            ||ascending.States.Count is <2 or >32||ascending.States.Count!=descending.States.Count)
            throw new InvalidDataException("A complete matching ascending/descending native frontier pair is required.");
        Context=ascending.Context;Target=ascending.Target;Reservation=ascending.Reservation;
        vertical=Target.X1==Target.X2;length=TransferSchedule.Length(Target);
        if(length>4096)throw new InvalidDataException("Discrete frontier length budget exceeded.");
        firstAlong=vertical?Math.Min(Target.Y1,Target.Y2):Math.Min(Target.X1,Target.X2);
        int Cross(ScreenLine x)=>vertical?x.X1:x.Y1;
        void Read(StandardBandResponse model,bool increasing,Dictionary<(int,int),StandardDiscreteFrontier> output)
        {
            int start=Cross(model.States[0].Commands[0]);
            for(int i=0;i<model.States.Count;i++)
            {
                var state=model.States[i];
                if(state.PrefixLength!=i+1||state.Commands.Where((c,k)=>Cross(c)!=start+(increasing?k:-k)).Any())
                    throw new InvalidDataException("Frontier prefix direction, spacing or history changed.");
                int cross=Cross(state.Commands[^1]);var rows=state.Sections.Where(r=>r.Mandatory).ToArray();
                if(rows.Length!=length+1||rows.Where((r,k)=>r.Along!=firstAlong+k).Any())
                    throw new InvalidDataException("Mandatory frontier denominator changed.");
                foreach(var row in rows)
                {
                    if(row.Edges is null)continue; // absent keys remain unavailable to every query
                    double edge=increasing?row.Edges.High:row.Edges.Low;
                    if(!double.IsFinite(edge))throw new InvalidDataException("Non-finite native frontier.");
                    output.Add((cross,row.Along),new(row.Along,cross,edge,state.Id));
                }
            }
        }
        Read(ascending,true,high);Read(descending,false,low);
        var commands=ascending.States.Select(s=>Cross(s.Commands[^1])).ToArray();
        if(!commands.SequenceEqual(descending.States.Select(s=>Cross(s.Commands[^1])).Reverse()))
            throw new InvalidDataException("Ascending and descending probes must observe the same command coordinates.");
        Commands=Array.AsReadOnly(commands);LowFrontiers=Array.AsReadOnly(low.Values.OrderBy(x=>x.Command).ThenBy(x=>x.Along).ToArray());
        HighFrontiers=Array.AsReadOnly(high.Values.OrderBy(x=>x.Command).ThenBy(x=>x.Along).ToArray());
        AscendingModelId=ascending.Id;DescendingModelId=descending.Id;
        Id=GeometryHash.Of(new{Revision,Policy,implementation=GeometryHash.ImplementationSha256,context=Context.Id,Target,Reservation,AscendingModelId,DescendingModelId,Commands});
    }

    public StandardDiscreteCandidate? PredictStraight(int firstCommand,int lastCommand,double targetWidth)
    {
        if(!double.IsFinite(targetWidth)||targetWidth<=0||targetWidth>64||firstCommand>lastCommand)
            throw new InvalidDataException("Invalid discrete band query.");
        if(!Commands.Contains(firstCommand)||!Commands.Contains(lastCommand))return null;
        var rows=new StandardDiscreteRow[length+1];double cross=vertical?Target.X1:Target.Y1,targetLow=cross-targetWidth/2,targetHigh=cross+targetWidth/2;
        double error=0,square=0,step=0,minWidth=double.PositiveInfinity,maxWidth=0;
        for(int i=0;i<=length;i++)
        {
            int along=firstAlong+i;
            if(!low.TryGetValue((firstCommand,along),out var l)||!high.TryGetValue((lastCommand,along),out var h))return null;
            if(l.Edge>=h.Edge)return null;
            rows[i]=new(along,l.Edge,h.Edge,l.StateId,h.StateId);
            double a=l.Edge-targetLow,b=h.Edge-targetHigh,width=h.Edge-l.Edge;
            error=Math.Max(error,Math.Max(Math.Abs(a),Math.Abs(b)));square+=(a*a+b*b)/(2*(length+1));
            minWidth=Math.Min(minWidth,width);maxWidth=Math.Max(maxWidth,width);
            if(i>0)step=Math.Max(step,Math.Max(Math.Abs(l.Edge-rows[i-1].Low),Math.Abs(h.Edge-rows[i-1].High)));
        }
        var paths=new List<IReadOnlyList<ScreenPoint>>();int start=vertical?Target.Y1:Target.X1,sign=vertical?Math.Sign(Target.Y2-Target.Y1):Math.Sign(Target.X2-Target.X1);
        for(int q=firstCommand;q<=lastCommand;q++)
        {
            var path=Enumerable.Range(0,length+1).Select(i=>vertical?new ScreenPoint(q,start+i*sign):new(start+i*sign,q)).ToArray();
            StandardBandContour.Validate(path,Context.Geometry.Canvas);
            if(path.Any(p=>p.X<Reservation.Left||p.X>=Reservation.Right||p.Y<Reservation.Top||p.Y>=Reservation.Bottom))
                throw new InvalidDataException("Discrete command exceeds the measured reservation.");
            paths.Add(Array.AsReadOnly(path));
        }
        var frozenRows=Array.AsReadOnly(rows);var frozenPaths=paths.AsReadOnly();var score=new StandardDiscreteScore(error,square,step,maxWidth-minWidth);
        string id=GeometryHash.Of(new{Revision,Policy,atlas=Id,firstCommand,lastCommand,targetWidth,score,rows=frozenRows,paths=frozenPaths});
        return new(Revision,Id,Target,targetWidth,firstCommand,lastCommand,score,frozenRows,frozenPaths,id);
    }
    public StandardDiscreteCandidate? BestStraight(double targetWidth)
    {
        if(!double.IsFinite(targetWidth)||targetWidth<=0||targetWidth>64)throw new InvalidDataException("Invalid discrete target width.");
        StandardDiscreteCandidate? best=null;
        foreach(int a in Commands)foreach(int b in Commands)if(a<=b)
        {var candidate=PredictStraight(a,b,targetWidth);if(candidate is not null&&(best is null||Compare(candidate,best)<0))best=candidate;}
        return best;
    }
    private static int Compare(StandardDiscreteCandidate a,StandardDiscreteCandidate b)
    {
        int c=a.Prediction.MaximumTwoEdgeErrorPx.CompareTo(b.Prediction.MaximumTwoEdgeErrorPx);if(c!=0)return c;
        c=a.Prediction.MeanSquaredTwoEdgeErrorPx2.CompareTo(b.Prediction.MeanSquaredTwoEdgeErrorPx2);if(c!=0)return c;
        c=a.Prediction.MaximumAdjacentEdgeStepPx.CompareTo(b.Prediction.MaximumAdjacentEdgeStepPx);if(c!=0)return c;
        c=a.Prediction.WidthRangePx.CompareTo(b.Prediction.WidthRangePx);if(c!=0)return c;
        c=a.Paths.Count.CompareTo(b.Paths.Count);return c!=0?c:a.FirstCommand.CompareTo(b.FirstCommand);
    }
}
