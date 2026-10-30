using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using CanvasForge.Core;

// Offline mode of the existing test host; not a product or collection CLI.
internal static class StandardStrokeObservationReplay
{
    private sealed record Frame(string Path,int Width,int Height,string RgbaSha256);
    private sealed record Hypothesis(string Name,string GeometryId,StandardGeometricStrokeSection[] Geometry);
    private sealed record Case(string Label,int Cross,ScreenPoint[] Path,Frame Before,string HistoryId,string EvidenceId,StandardBandSection[] Observed,Hypothesis[] Hypotheses);
    private sealed record Input(StandardGeometryContext Context,string AppSha256,string CoreSha256,string ProtocolId,string FrozenHypothesesSha256,Case[] Cases);
    private static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    public static void Run(string inputFile,string output)
    {
        inputFile=Path.GetFullPath(inputFile);output=Path.GetFullPath(output);
        if(Directory.Exists(output)||File.Exists(output))throw new IOException("Replay output must be a fresh directory.");
        string original=Hash(inputFile);var input=JsonSerializer.Deserialize<Input>(File.ReadAllBytes(inputFile))??throw new InvalidDataException("Missing native replay input.");
        if(input.Cases.Length!=12||input.Cases.Any(c=>c.Path.Length!=901||c.Observed.Length!=901||c.Hypotheses.Length!=8)
            ||input.Cases.Select(c=>c.Label).Distinct().Count()!=12)throw new InvalidDataException("Frozen UV replay denominator changed.");
        var context=new StandardBandResponseContext(input.Context,input.AppSha256,input.CoreSha256,StandardContourMotion.Revision,input.Context.Speed,"per-stroke released native pairs; supplied geometry is a hypothesis");
        context.Validate();var frames=new Dictionary<string,string>();var comparisons=new List<object>();int fullIncomplete=0,interiorComplete=0,interiorIncomplete=0;
        Directory.CreateDirectory(output);var invariant=CultureInfo.InvariantCulture;
        using(var csv=new StreamWriter(Path.Combine(output,"predicted-observed.csv")))
        {
            csv.WriteLine("label,hypothesis,kernel,scope,along,predicted_low,predicted_high,observed_low,observed_high,prediction_reason,observation_reason");
            foreach(var item in input.Cases)
            {
                string file=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(inputFile)!,item.Before.Path));frames.Add(file,Hash(file));
                using var source=File.OpenRead(file);using var gzip=new GZipStream(source,CompressionMode.Decompress);using var bytes=new MemoryStream();gzip.CopyTo(bytes);
                byte[] rgba=bytes.ToArray();if(Convert.ToHexString(SHA256.HashData(rgba)).ToLowerInvariant()!=item.Before.RgbaSha256)throw new InvalidDataException("Before RGBA hash differs.");
                var before=new PixelImage(item.Before.Width,item.Before.Height,rgba);var observed=item.Observed.ToDictionary(r=>r.Along);
                foreach(var hypothesis in item.Hypotheses)
                foreach(var kernel in new[]{(Name:"sharp",Taps:new[]{new StandardObservationTap(0,1)}),(Name:"two-row-hypothesis",Taps:new[]{new StandardObservationTap(0,.5),new(-1,.5)})})
                foreach(var range in new[]{(Name:"frozen-full-INCOMPLETE",First:200,Last:1100),(Name:"post-run-interior-NOT-QUALIFIED",First:216,Last:1084)})
                {
                    var model=new StandardStrokeObservationModel(context,item.Path,item.HistoryId,hypothesis.GeometryId,hypothesis.Geometry,kernel.Taps,true,item.Cross,range.First,range.Last,new(0,0,0));
                    var predicted=model.Predict(before);
                    var result=predicted.Compare(model.ObservationContextId,item.EvidenceId,item.Observed.Where(r=>r.Along>=range.First&&r.Along<=range.Last).ToArray());
                    if(range.First==200){if(result.Complete||result.MaximumTwoEdgeResidualPx.HasValue)throw new InvalidDataException("Missing Y200 became a complete comparison.");fullIncomplete++;}
                    else {if(result.Complete)interiorComplete++;else interiorIncomplete++;}
                    comparisons.Add(new{item.Label,hypothesis=hypothesis.Name,kernel=kernel.Name,scope=range.Name,model.Id,model.ObservationContextId,result});
                    foreach(var p in predicted.Sections)
                    {
                        var o=observed[p.Along];string F(double? d)=>d?.ToString("R",invariant)??"";
                        csv.WriteLine(string.Join(",",item.Label,hypothesis.Name,kernel.Name,range.Name,p.Along.ToString(invariant),F(p.Edges?.Low),F(p.Edges?.High),F(o.Edges?.Low),F(o.Edges?.High),p.Reason??"",o.Reason??""));
                    }
                }
            }
        }
        if(Hash(inputFile)!=original||frames.Any(p=>Hash(p.Key)!=p.Value))throw new IOException("Replay input changed.");
        File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{revision=StandardStrokeObservationModel.Revision,
            inputSha256=original,input.ProtocolId,input.FrozenHypothesesSha256,modelCoreSha256=Hash(typeof(StandardStrokeObservationModel).Assembly.Location),
            physicalCollectionCoreSha256=input.CoreSha256,frames,fullIncomplete,interiorComplete,interiorIncomplete,comparisons,
            fitsApplied=0,phaseSelected=false,kernelsPhysicallyCalibrated=false,physicalQualityCertified=false,productionEligible=false,nativeInput=false,
            scope="All eight pre-live hypotheses retained. Geometry and two-row kernel are hypotheses; post-run interior residuals are not independent qualification; frozen full trial remains INCOMPLETE."},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"{fullIncomplete} full comparisons INCOMPLETE; interior: {interiorComplete} complete + {interiorIncomplete} incomplete (retained); no fit, phase selection or native input.");
    }
}
