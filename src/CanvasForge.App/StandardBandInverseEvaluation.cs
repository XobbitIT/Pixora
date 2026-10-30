using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

internal static class StandardBandInverseEvaluation
{
    internal static void Run(string source,string output,bool synthetic=false)
    {
        source=Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);output=Path.GetFullPath(output).TrimEnd(Path.DirectorySeparatorChar);
        bool Within(string a,string b)=>a.Equals(b,StringComparison.OrdinalIgnoreCase)||a.StartsWith(b+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        if(Within(source,output)||Within(output,source)||Directory.Exists(output)||File.Exists(output))throw new IOException("Use a new separate inverse evaluation directory.");
        var m=StandardBandInversePilotCli.Read(source,offline:true);
        if(m.SyntheticSource!=synthetic)throw new InvalidDataException("Synthetic/native inverse evidence mismatch.");
        StandardTrackPairCli.ValidateCompleted(source,StandardBandInversePilotCli.ValidationView(m));
        var original=Directory.GetFiles(source,"*",SearchOption.AllDirectories).ToDictionary(p=>Path.GetRelativePath(source,p),StandardBoundaryReview.Hash);
        var canvas=m.Context.Canvas;var baseline=StandardStraightPilotCli.Frame(Path.Combine(source,"baseline.png"),canvas,synthetic);
        using var models=JsonDocument.Parse(File.ReadAllText(Path.Combine(source,"model/inverse-candidates.json")));var result=new List<object>();
        foreach(var c in m.Cases)
        {
            var sourceModel=models.RootElement.EnumerateArray().Single(x=>x.GetProperty("sourceSample").GetInt32()==c.SourceSample);
            var candidate=sourceModel.GetProperty("candidate");var target=candidate.GetProperty("Target").Deserialize<ScreenLine>();
            var after=StandardStraightPilotCli.Frame(Path.Combine(source,$"sample-{c.Sample.Index:D2}/black-band/after.png"),canvas,synthetic);
            int first=Math.Min(target.Y1,target.Y2),last=Math.Max(target.Y1,target.Y2);double width=candidate.GetProperty("TargetWidth").GetDouble();
            var rows=Enumerable.Range(first,last-first+1).Select(y=>StandardBandResponse.MeasureSection(baseline,after,canvas,true,target.X1,y)).ToArray();
            var edges=rows.Where(r=>r.Edges is not null).Select(r=>r.Edges!).ToArray();
            var steps=rows.Zip(rows.Skip(1)).Where(p=>p.First.Edges is not null&&p.Second.Edges is not null)
                .Select(p=>Math.Max(Math.Abs(p.First.Edges!.Low-p.Second.Edges!.Low),Math.Abs(p.First.Edges.High-p.Second.Edges.High))).ToArray();
            int capLow=0,capHigh=0;var possible=new List<ScreenPoint>();
            for(int y=c.Sample.Reservation.Top;y<c.Sample.Reservation.Bottom;y++)for(int x=c.Sample.Reservation.Left;x<c.Sample.Reservation.Right;x++)
            {
                int i=(y-canvas.Top)*canvas.Width+x-canvas.Left;if(RustSlider.Delta(baseline.Color(i),after.Color(i))<=12)continue;
                possible.Add(new(x,y));if(y<first)capLow++;if(y>last)capHigh++;
            }
            var observed=new StandardBandScore(rows.Count(r=>r.Edges is null),edges.Length==0?null:edges.Max(e=>Math.Max(Math.Abs(e.Low-target.X1+width/2),Math.Abs(e.High-target.X1-width/2))),
                edges.Length==0?null:edges.Max(e=>e.High-e.Low)-edges.Min(e=>e.High-e.Low),steps.Length==0?null:steps.Max(),capLow,capHigh);
            var predicted=candidate.GetProperty("Rows").EnumerateArray().ToDictionary(x=>x.GetProperty("Along").GetInt32());
            double? discrepancy=edges.Length==0?null:edges.Max(e=>Math.Max(Math.Abs(e.Low-predicted[e.Along].GetProperty("PredictedLow").GetDouble()),Math.Abs(e.High-predicted[e.Along].GetProperty("PredictedHigh").GetDouble())));
            result.Add(new{c.SourceSample,c.CandidateId,observed,prediction=candidate.GetProperty("Prediction"),maximumObservedVsPredictedTwoEdgeDifferencePx=discrepancy,
                historicalControl=sourceModel.GetProperty("controlScore"),historicalShifted=sourceModel.GetProperty("shiftedScore"),rows,observedPossible=BrushFootprints.Spans(possible),
                comparisonScope="historical straight bands vs this single new curved-path attempt; not a new same-build randomized pair",physicalQualityCertified=false});
        }
        if(Directory.GetFiles(source,"*",SearchOption.AllDirectories).Length!=original.Count||original.Any(x=>StandardBoundaryReview.Hash(Path.Combine(source,x.Key))!=x.Value))throw new IOException("Inverse evidence changed during evaluation.");
        Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"physical-comparison.json"),JsonSerializer.Serialize(new{status="MEASURED_CORRECTED_ATTEMPT_NOT_QUALIFIED",
            synthetic,nativeInputByReviewer=false,physicalQualityCertified=false,results=result,sourceFiles=original},new JsonSerializerOptions{WriteIndented=true}));
    }
}
