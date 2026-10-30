using System.Globalization;
using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

// Mechanism diagnostics and practical line quality have separate verdicts.
// These records are reports, not a trust boundary or a Solid/eligibility API.
internal static class StandardUvAnalysis
{
    internal sealed record Jump(int FromY,int ToY,double Delta,string Sign);
    internal sealed record Segment(int FirstY,int LastY,int Count,double? Slope);
    internal sealed record Edge(double S,int Jumps,int PositiveJumps,int NegativeJumps,string Classification,
        bool C1,bool C1Opposite,double OlsDriftPx,Jump[] Events,Segment[] Segments);
    internal sealed record LineResult(string Label,int ObservedRows,int MissingRows,Edge? Low,Edge? High,
        bool Consistent,bool Contradictory,bool? PracticalC3);
    internal sealed record Result(string Verdict,string[] Reasons,bool C1,bool C2,bool LeftC1Opposite,
        int ConsistentLines,int ContradictoryLines,LineResult[] Lines);
    internal static string Classify(double sum,int commandSign)
    {
        if(!double.IsFinite(sum))return "INCOMPLETE";
        return Math.Abs(sum)<=2.6?"CONSISTENT":Math.Abs(sum)>=4&&commandSign!=0&&Math.Sign(sum)==commandSign?"CONTRADICTORY":"INCONCLUSIVE";
    }
    private static double Slope(StandardEdgeSection[] rows,bool low)
    {
        double x=rows.Average(r=>r.Along),y=rows.Average(r=>low?r.Low:r.High);
        return rows.Sum(r=>(r.Along-x)*((low?r.Low:r.High)-y))/rows.Sum(r=>(r.Along-x)*(r.Along-x));
    }
    private static Edge Measure(StandardEdgeSection[] rows,bool low,int columnSign,int commandSign)
    {
        double Value(int i)=>low?rows[i].Low:rows[i].High;
        int[] jumps=Enumerable.Range(1,900).Where(i=>Math.Abs(Value(i)-Value(i-1))>=1.5).ToArray();
        var events=jumps.Select(i=>new Jump(rows[i-1].Along,rows[i].Along,Value(i)-Value(i-1),Value(i)>Value(i-1)?"positive":"negative")).ToArray();
        int[] cuts=[0,..jumps,rows.Length];var segments=new List<Segment>();
        for(int i=1;i<cuts.Length;i++)
        {
            var part=rows.Skip(cuts[i-1]).Take(cuts[i]-cuts[i-1]).ToArray();
            segments.Add(new(part[0].Along,part[^1].Along,part.Length,part.Length<2?null:Slope(part,low)));
        }
        bool Signs(int wanted)=>segments.All(s=>s.Slope.HasValue&&Math.Sign(s.Slope.Value)==wanted);
        bool c1=jumps.Length>=2&&Signs(columnSign)&&events.All(e=>Math.Sign(e.Delta)==-columnSign);
        bool opposite=jumps.Length>=2&&Signs(-columnSign)&&events.All(e=>Math.Sign(e.Delta)==columnSign);
        double sum=events.Sum(e=>e.Delta);
        return new(sum,jumps.Length,events.Count(e=>e.Delta>0),events.Count(e=>e.Delta<0),Classify(sum,commandSign),
            c1,opposite,900*Slope(rows,low),events,segments.ToArray());
    }
    internal static Result Analyze(StandardUvPilot.Line[] design,IReadOnlyDictionary<string,StandardEdgeSection[]> source,IEnumerable<string>? failures=null)
    {
        var reasons=failures?.ToList()??[];
        if(design.Length!=12||design.Select(l=>l.Label).Distinct().Count()!=12)throw new InvalidDataException("Exactly twelve frozen UV lines are required.");
        foreach(var line in design)
        {
            if(!source.TryGetValue(line.Label,out var rows)||rows.Length!=901||!rows.Select(r=>r.Along).SequenceEqual(Enumerable.Range(200,901)))reasons.Add(line.Label+": mandatory rows missing, duplicated or out of order");
            else if(rows.Any(r=>!double.IsFinite(r.Low)||!double.IsFinite(r.High)||r.Low>=r.High))reasons.Add(line.Label+": invalid edge");
        }
        if(reasons.Count>0)return new("INCOMPLETE",reasons.ToArray(),false,false,false,0,0,design.Select(l=>
        {
            var rows=source.GetValueOrDefault(l.Label)??[];
            int observed=rows.Where(r=>r.Along is >=200 and <=1100&&double.IsFinite(r.Low)&&double.IsFinite(r.High)&&r.Low<r.High).Select(r=>r.Along).Distinct().Count();
            return new LineResult(l.Label,observed,901-observed,null,null,false,false,null);
        }).ToArray());
        var result=new List<LineResult>();
        foreach(var line in design)
        {
            var rows=source[line.Label];double expected=StandardUvPilot.Drift(line);int commandSign=line.Tilted?Math.Sign(expected):0;
            var low=Measure(rows,true,Math.Sign(expected),commandSign);var high=Measure(rows,false,Math.Sign(expected),commandSign);
            double tolerance=line.Label.StartsWith("995-",StringComparison.Ordinal)?.5:.35*Math.Abs(expected);
            bool practical=low.Jumps==0&&high.Jumps==0&&Math.Abs(low.OlsDriftPx-expected)<=tolerance&&Math.Abs(high.OlsDriftPx-expected)<=tolerance;
            result.Add(new(line.Label,901,0,low,high,low.Classification=="CONSISTENT"&&high.Classification=="CONSISTENT",
                low.Classification=="CONTRADICTORY"&&high.Classification=="CONTRADICTORY",line.Tilted?practical:null));
        }
        var by=result.ToDictionary(l=>l.Label,StringComparer.Ordinal);
        bool c1=new[]{by["600-C"],by["1400-C"]}.All(l=>l.Low!.C1&&l.High!.C1);
        bool c2=by["995-C"].Low!.Jumps==0&&by["995-C"].High!.Jumps==0;
        bool opposite=by["600-C"].Low!.C1Opposite&&by["600-C"].High!.C1Opposite;
        var outer=result.Where(l=>design.Single(d=>d.Label==l.Label).Tilted&&!l.Label.StartsWith("995-",StringComparison.Ordinal)).ToArray();
        int consistent=outer.Count(l=>l.Consistent),contradictory=outer.Count(l=>l.Contradictory);
        bool support=c1&&c2&&consistent>=5,reject=opposite||contradictory>=4;
        if(support&&reject)throw new InvalidDataException("Conflicting UV verdicts.");
        return new(support?"SUPPORTED":reject?"REJECTED":"INCONCLUSIVE",[],c1,c2,opposite,consistent,contradictory,result.ToArray());
    }
    internal static Dictionary<string,Dictionary<string,StandardEdgeSection[]>> ReadRows(string csv)
    {
        var rows=File.ReadLines(csv).Skip(1).Select(s=>s.Split(',')).ToArray();
        return rows.GroupBy(r=>r[0],StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.GroupBy(r=>r[1],StringComparer.Ordinal).ToDictionary(l=>l.Key,
            l=>l.Select(r=>new StandardEdgeSection(int.Parse(r[2],CultureInfo.InvariantCulture),double.Parse(r[3],CultureInfo.InvariantCulture),double.Parse(r[4],CultureInfo.InvariantCulture))).ToArray(),StringComparer.Ordinal),StringComparer.Ordinal);
    }
    internal static void CheckPaths(string fixtures,StandardUvPilot.Line[] lines)
    {
        using var paths=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixtures,"Paths.json")));
        foreach(var line in lines)
        {
            var recorded=paths.RootElement.GetProperty("cases").EnumerateArray().Single(c=>c.GetProperty("label").GetString()==line.Label);
            if(!recorded.GetProperty("path").Deserialize<ScreenPoint[]>()!.SequenceEqual(line.Path))throw new InvalidDataException("UV command paths differ from the checked 0027 vectors.");
        }
    }
    internal static void CheckFixtures(string fixtures,StandardUvPilot.Line[] lines)
    {
        StandardUvPilot.RequireFixtures(fixtures);CheckPaths(fixtures,lines);
        foreach(var (csv,expected) in new[]{("synthetic-inputs.csv","analytic-results.json"),("extractor-rows.csv","extractor-results.json")})
        {
            using var doc=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixtures,"Analysis",expected)));
            var scenes=doc.RootElement.GetProperty("scenes").EnumerateArray().ToArray();var data=ReadRows(Path.Combine(fixtures,"Analysis",csv));
            if(data.Count!=8||scenes.Length!=8)throw new InvalidDataException("Incomplete golden models.");
            foreach(var (name,rows) in data)
            {
                var actual=Analyze(lines,rows);var model=scenes.Single(s=>s.GetProperty("scene").GetString()==name);
                if(actual.Verdict!=model.GetProperty("verdict").GetString())throw new InvalidDataException("Golden model verdict changed.");
                foreach(var l in actual.Lines)
                {
                    var m=model.GetProperty("cases").EnumerateArray().Single(c=>c.GetProperty("label").GetString()==l.Label);
                    foreach(var (edge,key) in new[]{(l.Low!,"low"),(l.High!,"high")})
                    {
                        var e=m.GetProperty(key);
                        if(Math.Abs(edge.S-e.GetProperty("signedJumpSumPx").GetDouble())>1e-10||edge.Jumps!=e.GetProperty("jumps").GetInt32()
                            ||edge.PositiveJumps!=e.GetProperty("positiveJumps").GetInt32()||edge.NegativeJumps!=e.GetProperty("negativeJumps").GetInt32()
                            ||edge.Classification!=e.GetProperty("C3Prime").GetString())throw new InvalidDataException("Golden edge sum/count/sign changed.");
                    }
                }
            }
        }
    }
    internal static Result Review(string directory,string output,bool synthetic=false)
    {
        directory=Path.GetFullPath(directory);output=Path.GetFullPath(output);StandardUvPilot.Separate(directory,output);
        var initial=Directory.GetFiles(directory,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToDictionary(p=>p,StandardUvPilot.Hash,StringComparer.Ordinal);
        var m=StandardUvPilot.Read(directory,offline:true);
        if(m.SyntheticSource!=synthetic)throw new InvalidDataException("Native and synthetic evidence cannot be interchanged.");
        var failures=new List<string>();var sections=new Dictionary<string,StandardEdgeSection[]>(StringComparer.Ordinal);var raw=new List<string>{"label,along,low,high,status"};
        try{StandardUvPilot.Verify(directory,m);}catch(Exception e) when(e is IOException or InvalidDataException or JsonException or KeyNotFoundException)
        {failures.Add(e.GetType().Name+": "+e.Message);}
        if(failures.Count==0)
        {
            foreach(var (line,index) in m.Lines.Select((l,i)=>(l,i)))
            {
                string stem=Path.Combine(directory,$"sample-{index/4:D2}/black-band/stroke-{index%4:D3}");
                var before=StandardStraightPilotCli.Frame(stem+"-before.png",m.Context.Canvas,synthetic);
                var after=StandardStraightPilotCli.Frame(stem+"-after.png",m.Context.Canvas,synthetic);var rows=new List<StandardEdgeSection>();
                foreach(int y in Enumerable.Range(200,901))
                {
                    var row=StandardBandResponse.MeasureSection(before,after,m.Context.Canvas,true,(int)Math.Round(line.AnchorX,MidpointRounding.ToEven),y);
                    if(row.Edges is { } edge)
                    {rows.Add(edge);raw.Add($"{line.Label},{y},{edge.Low.ToString("R",CultureInfo.InvariantCulture)},{edge.High.ToString("R",CultureInfo.InvariantCulture)},measured");}
                    else raw.Add($"{line.Label},{y},,,missing");
                }
                sections.Add(line.Label,rows.ToArray());
            }
        }
        var result=Analyze(m.Lines,sections,failures);
        if(initial.Count!=Directory.GetFiles(directory,"*",SearchOption.AllDirectories).Length||initial.Any(p=>!File.Exists(p.Key)||StandardUvPilot.Hash(p.Key)!=p.Value))throw new IOException("UV source evidence changed during review.");
        Directory.CreateDirectory(output);File.WriteAllLines(Path.Combine(output,"sections.csv"),raw);
        var edges=new List<string>{"label,edge,observed_rows,missing_rows,S_px,jumps,positive_jumps,negative_jumps,classification,practical_C3"};
        foreach(var l in result.Lines)foreach(var (e,name) in new[]{(l.Low,"low"),(l.High,"high")})
            edges.Add(string.Join(",",l.Label,name,l.ObservedRows,l.MissingRows,e?.S.ToString("R",CultureInfo.InvariantCulture)??"",e?.Jumps.ToString(CultureInfo.InvariantCulture)??"",
                e?.PositiveJumps.ToString(CultureInfo.InvariantCulture)??"",e?.NegativeJumps.ToString(CultureInfo.InvariantCulture)??"",e?.Classification??"INCOMPLETE",l.PracticalC3?.ToString()??""));
        File.WriteAllLines(Path.Combine(output,"edges.csv"),edges);
        StandardUvPilot.WriteNew(Path.Combine(output,"summary.json"),new{revision=StandardUvPilot.Revision,policy=StandardUvPilot.Policy,m.ProtocolId,
            result,requiredLines=12,requiredSections=10812,reportedEdges=24,observable="native-band-local50-radius32-v1",
            sourceFiles=initial.ToDictionary(p=>Path.GetRelativePath(directory,p.Key).Replace('\\','/'),p=>p.Value,StringComparer.Ordinal),sourceUnchanged=true,
            nativeInputByReviewer=false,synthetic,physicalQualityCertified=false,productionEligible=false,
            interpretation="Two preregistered mechanisms only; REJECTED is not universal Rust impossibility; candidate C3 is separate; UV quality contract not certified"});
        return result;
    }
}
