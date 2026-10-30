using System.IO;
using System.Text.Json;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void ReplayStrictPaintMasks(string output,string source)
    {
        source=Path.GetFullPath(source);output=Path.GetFullPath(output);
        if(output.StartsWith(source+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||output==source)
            throw new IOException("Read-only source and separate output required");
        Directory.CreateDirectory(output);
        var inputs=new Dictionary<string,string>();
        string ReadPath(string name)
        {string p=Path.Combine(source,name);inputs.TryAdd(name,StandardBoundaryReview.Hash(p));return p;}
        using var pair=JsonDocument.Parse(File.ReadAllBytes(ReadPath("pair.json")));
        using var protocol=JsonDocument.Parse(File.ReadAllBytes(ReadPath("ascending/protocol.json")));
        var canvas=pair.RootElement.GetProperty("Context").GetProperty("Canvas").Deserialize<ScreenRect>();
        var results=new List<object>();int expected=0,complete=0,missing=0,disconnected=0,clipped=0;
        foreach(var c in protocol.RootElement.GetProperty("Cases").EnumerateArray())
        {
            int id=c.GetProperty("SourceSample").GetInt32();var sample=c.GetProperty("Sample").Deserialize<EvidenceSeriesCase>()!;
            var target=pair.RootElement.GetProperty("Cases").EnumerateArray().Single(x=>x.GetProperty("SourceSample").GetInt32()==id).GetProperty("Target").Deserialize<ScreenLine>();
            string folder=$"ascending/sample-{sample.Index:D2}/black-band";
            PixelImage Frame(string name)
            {
                string relative=folder+"/"+name;ReadPath(Path.ChangeExtension(relative,".json"));
                return StandardStraightPilotCli.Frame(ReadPath(relative),canvas,false);
            }
            var baseline=Frame("before.png");var requested=sample.Stages.Single(s=>s.Name=="black-band").Colour;
            int tracks=c.GetProperty("Paths").GetArrayLength();
            for(int i=0;i<tracks;i++)
            {
                var mask=StandardPaintMask.Measure(baseline,Frame($"stroke-{i:D3}-after.png"),canvas,sample.Reservation,target,requested);
                expected+=mask.Rows.Count;complete+=mask.CompleteRows;missing+=mask.MissingRows;
                disconnected+=mask.DisconnectedRows;clipped+=mask.ClippedRows;
                results.Add(new{sourceSample=id,prefix=i+1,requested,mask.Status,mask.Complete,mask.MatchingPixels,
                    mask.StrongNonMatchingPixels,mask.CompleteRows,mask.MissingRows,mask.DisconnectedRows,mask.ClippedRows,
                    mask.Rows,mask.PhysicalQualityCertified,mask.ProductionEligible});
            }
        }
        foreach(var (name,hash) in inputs)if(StandardBoundaryReview.Hash(Path.Combine(source,name))!=hash)throw new IOException("Source changed: "+name);
        File.WriteAllText(Path.Combine(output,"strict-mask-native.json"),JsonSerializer.Serialize(new{revision=StandardPaintMask.Revision,
            cases=results.Count,expectedRows=expected,completeRows=complete,missingRows=missing,disconnectedRows=disconnected,clippedRows=clipped,
            sourceFiles=inputs,sourceUnchanged=true,nativeInput=false,physicalQualityCertified=false,results},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"STRICT MASK REPLAY: {results.Count} states, {expected} mandatory rows: complete {complete}, missing {missing}, disconnected {disconnected}, clipped {clipped}; no input or certification.");
    }
}
