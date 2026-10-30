using System.Globalization;
using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

internal static class StandardPlaneAnalysis
{
    internal sealed record Threshold(int Row,int Index,int Sign);
    internal sealed record Pair(int Observed,int Predicted,double PhaseError,int RowError);
    internal sealed record Matching(int Observed,int Predicted,int Matched,double? Precision,double? Recall,bool Passed,Pair[] Pairs);
    internal sealed record Edge(StandardTexelAnalysis.Event[] Events,double SignedEventSum,double Min,double Max,double? GridMeanSigned,double? GridP95Absolute,Matching? Control);
    internal sealed record Line(string Label,string Role,int Required,int Observed,Edge? Low,Edge? High,double? WidthMean,double? WidthRange);
    internal sealed record Result(string Status,string Mechanism,string[] Reasons,Line[] Lines);
    internal static double Phase(int x,int y)
    {
        var p=StandardPlanePilot.Parameters().Columns;double left=p.LeftIntercept+p.LeftSlope*y,w=p.RightIntercept+p.RightSlope*y-left;
        return p.EffectiveColumns*(x+p.InputScaleOffset*(x-p.InputAnchor)-left)/w+p.CursorPhase;
    }
    internal static Threshold[] Predict(StandardPlanePilot.Probe p)
    {
        if(p.Role!="screen-control"||!p.Vertical||p.Paths.Length!=1)throw new InvalidDataException("Only the frozen vertical controls have phase predictions.");
        int x=p.Paths[0][0].X;var result=new List<Threshold>();
        for(int y=p.BodyFirst+1;y<=p.BodyLast;y++)
        {
            int a=(int)Math.Floor(Phase(x,y-1)),b=(int)Math.Floor(Phase(x,y));
            if(a==b)continue;if(Math.Abs(b-a)!=1)throw new InvalidDataException("Unsupported multiple-threshold control step.");
            result.Add(new(y,b>a?b:a,Math.Sign(b-a)));
        }
        return result.ToArray();
    }
    internal static Matching Match(StandardPlanePilot.Probe probe,StandardTexelAnalysis.Event[] observed)
    {
        var predicted=Predict(probe);int x=probe.Paths[0][0].X;
        if(observed.Length>probe.BodyLast-probe.BodyFirst||observed.Where((e,i)=>e.StartY<probe.BodyFirst||e.EndY>probe.BodyLast||e.StartY>e.EndY
            ||!double.IsFinite(e.Delta)||Math.Abs(e.Delta)<1.2||e.Sign!=Math.Sign(e.Delta)||i>0&&e.StartY<=observed[i-1].EndY).Any())
            throw new InvalidDataException("Invalid control-event sequence.");
        var count=new int[observed.Length+1,predicted.Length+1];var cost=new double[observed.Length+1,predicted.Length+1];var step=new byte[observed.Length+1,predicted.Length+1];
        for(int i=0;i<=observed.Length;i++)for(int j=0;j<=predicted.Length;j++)
        {
            if(i==0&&j==0)continue;int best=-1;double bestCost=double.PositiveInfinity;byte action=0;
            void Offer(int n,double c,byte a){if(n>best||n==best&&c<bestCost){best=n;bestCost=c;action=a;}}
            if(i>0)Offer(count[i-1,j],cost[i-1,j],1);if(j>0)Offer(count[i,j-1],cost[i,j-1],2);
            if(i>0&&j>0)
            {
                // Absolute, unwrapped phase against THIS predicted integer;
                // never pick the nearest integer or best row in an event.
                double error=Phase(x,observed[i-1].StartY)-predicted[j-1].Index;
                if(observed[i-1].Sign==predicted[j-1].Sign&&Math.Abs(error)<=.20)
                    Offer(count[i-1,j-1]+1,cost[i-1,j-1]+Math.Abs(error),3);
            }
            count[i,j]=best;cost[i,j]=bestCost;step[i,j]=action;
        }
        var pairs=new List<Pair>();int a=observed.Length,b=predicted.Length;
        while(a>0||b>0)
        {
            byte s=step[a,b];if(s==3){pairs.Add(new(a-1,b-1,Phase(x,observed[a-1].StartY)-predicted[b-1].Index,observed[a-1].StartY-predicted[b-1].Row));a--;b--;}
            else if(s==1)a--;else if(s==2)b--;else throw new InvalidDataException("Invalid phase matching traceback.");
        }
        pairs.Reverse();double? precision=observed.Length==0?null:pairs.Count/(double)observed.Length,recall=predicted.Length==0?null:pairs.Count/(double)predicted.Length;
        return new(observed.Length,predicted.Length,pairs.Count,precision,recall,observed.Length>0&&predicted.Length>0&&precision>=.8&&recall>=.8,pairs.ToArray());
    }
    internal static Result Analyze(StandardPlanePilot.Probe[] probes,IReadOnlyDictionary<string,StandardEdgeSection[]> source,IEnumerable<string>? failures=null)
    {
        // Contract is fixed in code AND independently hash-bound blueprint.
        if(probes.Length!=13||probes.Count(p=>p.Role=="position")!=8||probes.Count(p=>p.Role=="mechanism-centered")!=2
            ||probes.Count(p=>p.Role=="screen-control")!=2||probes.Count(p=>p.Role=="descriptive-only")!=1||probes.Select(p=>p.Label).Distinct().Count()!=13)
            throw new InvalidDataException("Frozen plane denominator changed.");
        var lines=probes.Where(p=>p.Role!="descriptive-only").ToArray();var reasons=failures?.ToList()??[];
        foreach(var p in lines)
            if(!source.TryGetValue(p.Label,out var rows)||!rows.Select(r=>r.Along).SequenceEqual(Enumerable.Range(p.BodyFirst,p.BodyLast-p.BodyFirst+1))
                ||rows.Any(r=>!double.IsFinite(r.Low)||!double.IsFinite(r.High)||r.Low>=r.High))reasons.Add(p.Label+": mandatory body sections missing/invalid/out of order");
        if(source.Keys.Any(k=>!lines.Any(l=>l.Label==k)))reasons.Add("Unexpected plane line.");
        int Observed(StandardPlanePilot.Probe p)=>(source.GetValueOrDefault(p.Label)??[]).Where(r=>r.Along>=p.BodyFirst&&r.Along<=p.BodyLast&&double.IsFinite(r.Low)&&double.IsFinite(r.High)&&r.Low<r.High).Select(r=>r.Along).Distinct().Count();
        if(reasons.Count>0)return new("INCOMPLETE","NOT_EVALUATED",reasons.ToArray(),lines.Select(p=>new Line(p.Label,p.Role,p.BodyLast-p.BodyFirst+1,Observed(p),null,null,null,null)).ToArray());
        var model=StandardPlanePilot.Parameters();var results=new List<Line>();
        foreach(var p in lines)
        {
            var rows=source[p.Label];var width=rows.Select(r=>r.High-r.Low).ToArray();
            Edge Measure(bool low)
            {
                var events=StandardTexelAnalysis.Detect(rows,low);double Value(StandardEdgeSection r)=>low?r.Low:r.High;
                double[]? residual=null;
                if(p.ConstantIndex is int index)residual=rows.Select(r=>
                {
                    double value=Value(r);
                    if(p.Vertical){var c=model.Columns;double left=c.LeftIntercept+c.LeftSlope*r.Along,w=c.RightIntercept+c.RightSlope*r.Along-left;
                        return (value-left)*c.EffectiveColumns/w-(low?c.LowEdgePhase:c.HighEdgePhase)-(low?index-c.StampColumns:index);}
                    var q=model.Rows;return q.Scale*value/(1-value/q.PoleY)-(low?q.TopEdgePhase:q.BottomEdgePhase)-(low?index:index+q.StampRows);
                }).ToArray();
                return new(events,events.Sum(e=>e.Delta),rows.Min(Value),rows.Max(Value),residual?.Average(),residual is null?null:residual.Select(Math.Abs).Order().ElementAt((int)Math.Ceiling(.95*residual.Length)-1),
                    p.Role=="screen-control"?Match(p,events):null);
            }
            results.Add(new(p.Label,p.Role,rows.Length,rows.Length,Measure(true),Measure(false),width.Average(),width.Max()-width.Min()));
        }
        bool centered=results.Where(l=>l.Role=="mechanism-centered").All(l=>l.Low!.Events.Length==0&&l.High!.Events.Length==0);
        bool controls=results.Where(l=>l.Role=="screen-control").All(l=>l.Low!.Control!.Passed&&l.High!.Control!.Passed);
        // One mechanism decision only. Position errors, exact width, caps and
        // 2D stamp are reported but cannot become physical/product B PASS.
        string mechanism=!centered?"CONTRADICTED":controls?"CONSISTENT":"INCONCLUSIVE";
        return new("MEASURED_NOT_QUALIFIED",mechanism,[],results.ToArray());
    }
    internal static Result Review(string directory,string output,bool synthetic=false)
    {
        directory=Path.GetFullPath(directory);output=Path.GetFullPath(output);StandardUvPilot.Separate(directory,output);
        var files=Directory.GetFiles(directory,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToDictionary(p=>p,StandardPlanePilot.Hash,StringComparer.Ordinal);
        var m=StandardPlanePilot.Read(directory,true);if(m.SyntheticSource!=synthetic)throw new InvalidDataException("Synthetic/native mismatch.");
        var failures=new List<string>();try{StandardPlanePilot.Verify(directory,m);}catch(Exception e) when(e is IOException or InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException){failures.Add(e.Message);}
        var body=new Dictionary<string,StandardEdgeSection[]>();var csv=new List<string>{"label,along,low,high,region,status,reason"};
        string Number(double d)=>d.ToString("R",CultureInfo.InvariantCulture);
        PixelImage Frame(string path)=>StandardStraightPilotCli.Frame(path,m.Context.Canvas,synthetic);
        if(failures.Count==0)foreach(var (p,i) in m.Probes.Select((p,i)=>(p,i)).Where(p=>p.p.Role!="descriptive-only"))
        {
            string stem=Path.Combine(directory,$"sample-{i:D2}/black-band/stroke-000");var before=Frame(stem+"-before.png");var after=Frame(stem+"-after.png");
            var rows=new List<StandardEdgeSection>();var path=p.Paths[0];
            foreach(var point in path)
            {
                int along=p.Vertical?point.Y:point.X,cross=p.Vertical?point.X:point.Y;bool mandatory=along>=p.BodyFirst&&along<=p.BodyLast;
                var row=StandardBandResponse.MeasureSection(before,after,m.Context.Canvas,p.Vertical,cross,along,mandatory);
                string region=mandatory?"body":along<p.BodyFirst?"start_cap":"end_cap";
                if(row.Edges is{} edge){if(mandatory)rows.Add(edge);csv.Add($"{p.Label},{along},{Number(edge.Low)},{Number(edge.High)},{region},measured,");}
                else csv.Add($"{p.Label},{along},,,{region},missing,{row.Reason}");
            }
            body.Add(p.Label,rows.ToArray());
        }
        var result=Analyze(m.Probes,body,failures);object? block=null;
        if(failures.Count==0)
        {
            var sample=m.Cases[^1];var before=Frame(Path.Combine(directory,"sample-12/black-band/before.png"));var after=Frame(Path.Combine(directory,"sample-12/black-band/after.png"));
            var c=m.Context.Canvas;var r=sample.Sample.Reservation;int changed=0,darkened=0,requestedBlack=0;
            for(int y=r.Top;y<r.Bottom;y++)for(int x=r.Left;x<r.Right;x++)
            {
                int at=(y-c.Top)*c.Width+x-c.Left;var a=before.Color(at);var b=after.Color(at);
                if(Math.Max(Math.Abs(a.R-b.R),Math.Max(Math.Abs(a.G-b.G),Math.Abs(a.B-b.B)))>12)changed++;
                if((a.R+a.G+a.B-b.R-b.G-b.B)/3.0>80)darkened++;
                if(Math.Max(b.R,Math.Max(b.G,b.B))<=12&&(a.R+a.G+a.B-b.R-b.G-b.B)/3.0>80)requestedBlack++;
            }
            object Perimeter(bool vertical)
            {
                var points=sample.Paths.SelectMany(p=>p).ToArray();int cross=vertical?(points.Min(p=>p.X)+points.Max(p=>p.X))/2:(points.Min(p=>p.Y)+points.Max(p=>p.Y))/2;
                int first=vertical?points.Min(p=>p.Y):points.Min(p=>p.X),last=vertical?points.Max(p=>p.Y):points.Max(p=>p.X);
                var sections=Enumerable.Range(first,last-first+1).Select(along=>StandardBandResponse.MeasureSection(before,after,c,vertical,cross,along,false)).ToArray();
                bool complete=sections.All(s=>s.Edges is not null);var edges=sections.Where(s=>s.Edges is not null).Select(s=>s.Edges!).ToArray();
                return new{vertical,sections,lowEvents=complete?StandardTexelAnalysis.Detect(edges,true):null,highEvents=complete?StandardTexelAnalysis.Detect(edges,false):null,
                    verdict="DESCRIPTIVE_ONLY",missing=sections.Count(s=>s.Edges is null)};
            }
            block=new{releasedSweeps=16,changedRgb12Pixels=changed,darkenedContrast80Pixels=darkened,requestedBlackRgb12Pixels=requestedBlack,reservationPixels=r.Width*r.Height,
                verticalPerimeter=Perimeter(true),horizontalPerimeter=Perimeter(false),verdict="DESCRIPTIVE_ONLY",solidPixels=0,physicalQualityCertified=false};
        }
        if(files.Count!=Directory.GetFiles(directory,"*",SearchOption.AllDirectories).Length||files.Any(p=>!File.Exists(p.Key)||StandardPlanePilot.Hash(p.Key)!=p.Value))throw new IOException("Plane evidence changed during review.");
        Directory.CreateDirectory(output);File.WriteAllLines(Path.Combine(output,"sections.csv"),csv);
        var events=new List<string>{"label,edge,start_along,end_along,delta_px,sign"};
        foreach(var l in result.Lines)foreach(var (e,name) in new[]{(l.Low,"low"),(l.High,"high")})if(e is not null)foreach(var d in e.Events)
            events.Add($"{l.Label},{name},{d.StartY},{d.EndY},{Number(d.Delta)},{d.Sign}");
        File.WriteAllLines(Path.Combine(output,"events.csv"),events);
        StandardUvPilot.WriteNew(Path.Combine(output,"summary.json"),new{StandardPlanePilot.Revision,StandardPlanePilot.Policy,m.ProtocolId,result,block,
            mandatoryBodySections=m.Probes.Where(p=>p.Role!="descriptive-only").Sum(p=>p.BodyLast-p.BodyFirst+1),
            predictedControlThresholds=m.Probes.Where(p=>p.Role=="screen-control").ToDictionary(p=>p.Label,Predict),
            requiredCases=13,requiredStrokes=28,completeDataBeforeArithmetic=true,exactWidthCertified=false,physicalUncertainty=(double?)null,
            positionPolicy="Grid mean and P95 are diagnostic, no acceptance without observable U. Short probes do not prove straightness.",
            mechanismPolicy="CONSISTENT only if both long centered lines have zero D on both edges AND every control edge passes signed absolute phase020 precision/recall. Any centered D => CONTRADICTED; other complete outcomes INCONCLUSIVE.",
            caps="separate diagnostic, missing caps never redefine body",sourceFiles=files.ToDictionary(p=>Path.GetRelativePath(directory,p.Key).Replace('\\','/'),p=>p.Value),sourceUnchanged=true,
            synthetic,nativeInputByReviewer=false,productionEligible=false,physicalQualityCertified=false});return result;
    }
}
