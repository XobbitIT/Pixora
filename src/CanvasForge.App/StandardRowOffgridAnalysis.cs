using System.Globalization;
using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;
internal static class StandardRowOffgridAnalysis
{
    internal sealed record Metrics(double MedianAbsolute,double MaximumAbsolute,double P95Absolute,double MeanSigned,double MedianSigned);
    internal sealed record Line(string Label,int Required,int Observed,Metrics? Top,Metrics? Bottom);
    internal sealed record Rival(double Scale,int RejectedBothEdgesLines,Metrics[][] Lines);
    internal sealed record Prefix(int Number,StandardEdgeSection[] Sections);
    internal sealed record PrefixMetrics(int Number,double LowMeanPx,double HighMeanPx,double WidthMeanPx);
    internal sealed record Increment(int Prefix,double DeltaPx,double Normalized,string Class);
    internal sealed record Block(string Label,PrefixMetrics[] Prefixes,Increment[] Increments);
    internal sealed record Step(double ObservedMeanAdvancePx,double Prediction,double RelativeError,bool WithinResearchBand);
    internal sealed record Result(string Status,string GridVerdict,string StepVerdict,string IncrementVerdict,bool CausalInterpretationAllowed,string[] Reasons,Line[] Lines,Rival[] Rivals,Block[] Blocks,Step? Step)
    {public bool ProductionEligible=>false;public bool PhysicalQualityCertified=>false;public bool UniqueCauseEstablished=>false;}
    internal static double Median(IEnumerable<double> values)
    {var a=values.Order().ToArray();if(a.Length==0)throw new InvalidDataException("No empty-denominator metric.");int m=a.Length/2;return a.Length%2==0?(a[m-1]+a[m])/2:a[m];}
    internal static Metrics Measure(double[] residual)
    {
        var absolute=residual.Select(Math.Abs).Order().ToArray();
        return new(Median(absolute),absolute[^1],absolute[(int)Math.Ceiling(.95*absolute.Length)-1],residual.Average(),Median(residual));
    }
    internal static string Classify(double value)=>!double.IsFinite(value)?"OUT_OF_CLASS":value is >=-.25 and <=.25?"zero":value is >=.75 and <=1.25?"one":value is >=1.75 and <=2.25?"two":"OUT_OF_CLASS";
    internal static Result Analyze(StandardRowOffgridPilot.Probe[] probes,TexelGrid grid,string assets,
        IReadOnlyDictionary<string,StandardEdgeSection[]> lines,IReadOnlyDictionary<string,Prefix[]> blocks,IEnumerable<string>? failures=null)
    {
        StandardRowOffgridPilot.CheckAssets(assets,probes);
        var reasons=failures?.ToList()??[];var expectedLines=probes.Take(6).ToArray();var expectedBlocks=probes.Skip(6).ToArray();
        bool Valid(StandardEdgeSection[]? rows,int first,int last)=>rows is not null&&rows.Length==last-first+1
            &&rows.Select(r=>r.Along).SequenceEqual(Enumerable.Range(first,last-first+1))
            &&rows.All(r=>double.IsFinite(r.Low)&&double.IsFinite(r.High)&&r.Low<r.High&&r.Low>=grid.RowDomain.Top-.5&&r.High<=grid.RowDomain.Bottom-.5);
        if(lines.Count!=6||lines.Keys.Any(k=>!expectedLines.Any(p=>p.Label==k)))reasons.Add("Short-line denominator changed.");
        if(blocks.Count!=2||blocks.Keys.Any(k=>!expectedBlocks.Any(p=>p.Label==k)))reasons.Add("Block denominator changed.");
        foreach(var p in expectedLines)if(!lines.TryGetValue(p.Label,out var sections)||!Valid(sections,p.BodyFirst,p.BodyLast))reasons.Add(p.Label+": missing/invalid/duplicate/out-of-order body.");
        foreach(var p in expectedBlocks)
        {
            if(!blocks.TryGetValue(p.Label,out var prefixes)||prefixes.Length!=p.Paths.Length){reasons.Add(p.Label+": missing/extra prefix.");continue;}
            if(prefixes.Where((q,i)=>q.Number!=i+1||!Valid(q.Sections,p.BodyFirst,p.BodyLast)).Any())reasons.Add(p.Label+": invalid prefix order/body.");
        }
        int Observed(StandardRowOffgridPilot.Probe p)=>lines.GetValueOrDefault(p.Label)?.Count(r=>double.IsFinite(r.Low)&&double.IsFinite(r.High)&&r.Low<r.High)??0;
        if(reasons.Count>0)return new("INCOMPLETE","NOT_EVALUATED","NOT_EVALUATED","NOT_EVALUATED",false,reasons.ToArray(),expectedLines.Select(p=>new Line(p.Label,p.BodyLast-p.BodyFirst+1,Observed(p),null,null)).ToArray(),[],[],null);
        // Every1123 mandatory section is validated before any metric arithmetic.
        var rows=grid.Rows;var measured=new List<Line>();
        foreach(var p in expectedLines)
        {
            var source=lines[p.Label];int index=p.ConstantIndex!.Value;
            measured.Add(new(p.Label,source.Length,source.Length,Measure(source.Select(r=>grid.ObservationCoordinate(r.Low)-rows.TopPhase-index).ToArray()),
                Measure(source.Select(r=>grid.ObservationCoordinate(r.High)-rows.BottomPhase-index-rows.EdgeLabelSpan).ToArray())));
        }
        using var doc=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(assets,"protocol-v1b.json")));var rivalList=new List<Rival>();
        foreach(var r in doc.RootElement.GetProperty("rivalPredictionTable").EnumerateArray())
        {
            double scale=r.GetProperty("scale").GetDouble();var phase=r.GetProperty("phase").EnumerateArray().Select(v=>v.GetDouble()).ToArray();var metrics=new List<Metrics[]>();
            foreach(var p in expectedLines)
            {
                double Residual(double y,int edge){double u=scale*y/(1-y/rows.PoleY)-phase[edge];return u-Math.Round(u,MidpointRounding.ToEven);}
                metrics.Add([Measure(lines[p.Label].Select(v=>Residual(v.Low,0)).ToArray()),Measure(lines[p.Label].Select(v=>Residual(v.High,1)).ToArray())]);
            }
            rivalList.Add(new(scale,metrics.Count(m=>m.All(v=>v.MedianAbsolute>.15)),metrics.ToArray()));
        }
        string gridVerdict=measured.Any(l=>l.Top!.MedianAbsolute>.15||l.Bottom!.MedianAbsolute>.15)?"CONTRADICTED":rivalList.All(r=>r.RejectedBothEdgesLines>=3)?"CONSISTENT":"INCONCLUSIVE";
        var blockList=new List<Block>();
        foreach(var p in expectedBlocks)
        {
            var policy=doc.RootElement.GetProperty("blocks").EnumerateArray().Single(b=>b.GetProperty("label").GetString()==p.Label);
            double pitch=policy.GetProperty("classificationPitchPx").GetDouble();
            var prefix=blocks[p.Label].Select(q=>new PrefixMetrics(q.Number,q.Sections.Average(s=>s.Low),q.Sections.Average(s=>s.High),q.Sections.Average(s=>s.High-s.Low))).ToArray();
            var inc=prefix.Zip(prefix.Skip(1)).Select(q=>{double d=q.Second.HighMeanPx-q.First.HighMeanPx,u=d/pitch;return new Increment(q.Second.Number,d,u,Classify(u));}).ToArray();
            blockList.Add(new(p.Label,prefix,inc));
        }
        double prediction=doc.RootElement.GetProperty("decisions").GetProperty("step").GetProperty("prediction").GetDouble();
        var center=blockList[0];var control=blockList[1];double observed=(center.Prefixes[^1].HighMeanPx-center.Prefixes[0].HighMeanPx)/14,error=Math.Abs(observed/prediction-1);
        var step=new Step(observed,prediction,error,error<=.01);
        bool Anomaly(Increment q)=>q.Class is "zero" or "two";
        string increment=center.Increments.Any(Anomaly)?"CENTERING_PREDICTION_CONTRADICTED":blockList.SelectMany(b=>b.Increments).Any(i=>i.Class=="OUT_OF_CLASS")?"INCONCLUSIVE_UNRESOLVED_INCREMENT":control.Increments.Any(Anomaly)?"CONSISTENT_WITH_PHASE_DEPENDENCE":"INCONCLUSIVE_NOT_REPRODUCED";
        return new("MEASURED_NOT_QUALIFIED",gridVerdict,step.WithinResearchBand?"WITHIN_RESEARCH_BAND":"OUTSIDE_RESEARCH_BAND",increment,gridVerdict=="CONSISTENT"&&step.WithinResearchBand,[],measured.ToArray(),rivalList.ToArray(),blockList.ToArray(),step);
    }
    internal static Result Review(string directory,string output,bool synthetic=false)
    {
        directory=Path.GetFullPath(directory);output=Path.GetFullPath(output);StandardUvPilot.Separate(directory,output);
        var files=Directory.GetFiles(directory,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToDictionary(p=>p,StandardRowOffgridPilot.Hash,StringComparer.Ordinal);
        var m=StandardRowOffgridPilot.Read(directory,true);if(m.SyntheticSource!=synthetic)throw new InvalidDataException("Synthetic/native mismatch.");
        var failures=new List<string>();try{StandardRowOffgridPilot.Verify(directory,m);}catch(Exception e) when(e is IOException or InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException){failures.Add(e.Message);}
        var lines=new Dictionary<string,StandardEdgeSection[]>();var blocks=new Dictionary<string,Prefix[]>();var csv=new List<string>{"label,prefix,along,low,high,region,status,reason"};
        string Number(double d)=>d.ToString("R",CultureInfo.InvariantCulture);
        PixelImage Frame(string path)=>StandardStraightPilotCli.Frame(path,m.Context.Canvas,synthetic);
        void Section(string label,int prefix,PixelImage before,PixelImage after,int cross,int along,bool mandatory,string region,List<StandardEdgeSection> found)
        {
            var row=StandardBandResponse.MeasureSection(before,after,m.Context.Canvas,false,cross,along,mandatory);
            if(row.Edges is{} edge){if(mandatory)found.Add(edge);csv.Add($"{label},{prefix},{along},{Number(edge.Low)},{Number(edge.High)},{region},measured,");}
            else csv.Add($"{label},{prefix},{along},,,{region},missing,{row.Reason}");
        }
        if(failures.Count==0)foreach(var (p,i) in m.Probes.Select((p,i)=>(p,i)))
        {
            string folder=Path.Combine(directory,$"sample-{i:D2}/black-band");
            if(p.Role=="grid-probe")
            {
                var before=Frame(Path.Combine(folder,"stroke-000-before.png"));var after=Frame(Path.Combine(folder,"stroke-000-after.png"));var body=new List<StandardEdgeSection>();
                foreach(var q in p.Paths[0])Section(p.Label,1,before,after,q.Y,q.X,q.X>=p.BodyFirst&&q.X<=p.BodyLast,q.X<p.BodyFirst?"start_cap":q.X>p.BodyLast?"end_cap":"body",body);
                lines.Add(p.Label,body.ToArray());
            }
            else
            {
                // One blank stage baseline against EVERY complete prefix-after.
                var before=Frame(Path.Combine(folder,"before.png"));var prefix=new List<Prefix>();
                for(int k=0;k<p.Paths.Length;k++)
                {
                    var after=Frame(Path.Combine(folder,$"stroke-{k:D3}-after.png"));int cross=(p.Paths[0][0].Y+p.Paths[k][0].Y)/2;var body=new List<StandardEdgeSection>();
                    for(int x=p.BodyFirst;x<=p.BodyLast;x++)Section(p.Label,k+1,before,after,cross,x,true,"cumulative_body",body);
                    prefix.Add(new(k+1,body.ToArray()));
                }
                blocks.Add(p.Label,prefix.ToArray());
            }
        }
        var result=Analyze(m.Probes,StandardRowOffgridPilot.Grid(m.Context,Path.Combine(directory,"fixtures")),Path.Combine(directory,"fixtures"),lines,blocks,failures);
        if(files.Count!=Directory.GetFiles(directory,"*",SearchOption.AllDirectories).Length||files.Any(p=>!File.Exists(p.Key)||StandardRowOffgridPilot.Hash(p.Key)!=p.Value))throw new IOException("Offgrid evidence changed during review.");
        Directory.CreateDirectory(output);File.WriteAllLines(Path.Combine(output,"sections.csv"),csv);
        StandardUvPilot.WriteNew(Path.Combine(output,"summary.json"),new{StandardRowOffgridPilot.Revision,StandardRowOffgridPilot.Policy,m.ProtocolId,result,requiredCases=8,requiredStrokes=37,mandatoryBodySections=1123,
            completeDataBeforeArithmetic=true,metric="same median(abs(residual)) peredge/line; expected-label unwrapped for candidate, nearest-integer for rivals; max and nearest-rank P95 descriptive",
            sourceFiles=files.ToDictionary(p=>Path.GetRelativePath(directory,p.Key).Replace('\\','/'),p=>p.Value),sourceUnchanged=true,synthetic,nativeInputByReviewer=false,physicalUncertainty=(double?)null,
            caps="Separate diagnostic; not certified",periodDenominator="14command increments, not independently counted physical rows",productionEligible=false,physicalQualityCertified=false});
        return result;
    }
}
