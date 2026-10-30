using System.IO;
using System.Text.Json;
using CanvasForge.App;

internal static partial class Program
{
    private static void CheckEdgeCorrectionReview(string output)
    {
        string source=Path.Combine(output,"repeat-synthetic-full"),review=Path.Combine(output,"edge-correction-synthetic-review");
        Assert(Directory.Exists(source),"Required complete synthetic source is missing");
        var hashes=Directory.GetFiles(source,"*",SearchOption.AllDirectories).ToDictionary(p=>p,StandardBoundaryReview.Hash);
        StandardEdgeCorrectionReview.Run(source,review,ReadSyntheticRepeat);
        Assert(hashes.All(p=>StandardBoundaryReview.Hash(p.Key)==p.Value),"Correction review changed source evidence");
        using var summary=JsonDocument.Parse(File.ReadAllText(Path.Combine(review,"summary.json")));
        var s=summary.RootElement;
        Assert(s.GetProperty("cases").GetInt32()==32&&s.GetProperty("measurements").GetInt32()==13488
            &&s.GetProperty("missingInteriorSections").GetInt32()==0,"Correction measurements omitted required sections");
        Assert(s.GetProperty("actualCorrectedNativeObservations").GetInt32()==0&&!s.GetProperty("dispatchAllowed").GetBoolean()
            &&!s.GetProperty("physicalQualityCertified").GetBoolean()&&s.GetProperty("synthetic").GetBoolean(),"Surrogate pixels reported as physical correction");
        using var candidates=JsonDocument.Parse(File.ReadAllText(Path.Combine(review,"edge-correction-candidates.json")));
        Assert(candidates.RootElement.EnumerateArray().All(c=>c.GetProperty("candidate").GetProperty("Status").GetString()=="INCOMPLETE"
            &&c.GetProperty("candidate").GetProperty("CandidatePoints").GetArrayLength()==0
            &&c.GetProperty("candidate").GetProperty("SurrogateMaximumEdgeError").GetDouble()==0),"Synthetic fit extrapolated missing caps or failed exact edges");
        bool refused=false;try{StandardEdgeCorrectionReview.Run(source,review,ReadSyntheticRepeat);}catch(IOException){refused=true;}
        Assert(refused,"Correction output overwritten");
        refused=false;try{StandardEdgeCorrectionReview.Run(source,Path.Combine(source,"forbidden-review"),ReadSyntheticRepeat);}catch(IOException){refused=true;}
        Assert(refused&&!Directory.Exists(Path.Combine(source,"forbidden-review")),"Review wrote into source evidence");
    }
}
