using System.IO;
using System.Text.Json;
using CanvasForge.App;

internal static partial class Program
{
    private static void CheckBandResponsePipeline(string output)
    {
        string source=Path.Combine(output,"track-pair"),target=Path.Combine(output,"band-response");
        StandardBandResponseReview.Run(source,target,allowSynthetic:true);
        using var summary=JsonDocument.Parse(File.ReadAllText(Path.Combine(target,"summary.json")));var s=summary.RootElement;
        Assert(s.GetProperty("observedStates").GetInt32()==60&&s.GetProperty("bands").GetInt32()==6,"Incomplete response catalog");
        Assert(s.GetProperty("validatedNativeFrames").GetInt32()==0&&s.GetProperty("synthetic").GetBoolean(),"Fixture counted as native");
        Assert(!s.GetProperty("dispatchAllowed").GetBoolean()&&!s.GetProperty("physicalQualityCertified").GetBoolean(),"Response catalog authorized paint");
        Assert(s.GetProperty("selectionRevision").GetString()==CanvasForge.Core.StandardBandResponse.SelectionRevision,"Unversioned prefix ranking policy");
        using var models=JsonDocument.Parse(File.ReadAllText(Path.Combine(target,"band-responses.json")));
        foreach(var m in models.RootElement.EnumerateArray())
        {
            Assert(m.GetProperty("States").GetArrayLength()==10,"Prefix skipped");
            foreach(var state in m.GetProperty("States").EnumerateArray())
                Assert(state.GetProperty("Sections").EnumerateArray().Count(r=>r.GetProperty("Mandatory").GetBoolean())==401,"End sections omitted");
            foreach(var score in m.GetProperty("scores").EnumerateArray())
                Assert(score.GetProperty("score").TryGetProperty("MeanSquaredTwoEdgeErrorPx2",out _),"Aggregate target fit missing from the review output");
        }
    }
    private static void CheckBandInversePipeline(string output)
    {
        string source=Path.Combine(output,"track-pair"),target=Path.Combine(output,"band-inverse");
        StandardBandResponseReview.Run(source,target,allowSynthetic:true,inverse:true);
        using var candidates=JsonDocument.Parse(File.ReadAllText(Path.Combine(target,"inverse-candidates.json")));
        Assert(candidates.RootElement.GetArrayLength()==3,"Source case omitted from inverse result");
        foreach(var item in candidates.RootElement.EnumerateArray())
        {
            var c=item.GetProperty("candidate");if(c.ValueKind==JsonValueKind.Null)continue;
            Assert(!c.GetProperty("DispatchAllowed").GetBoolean()&&!c.GetProperty("PhysicalQualityCertified").GetBoolean(),"Corrected prediction promoted to measured quality");
            Assert(c.GetProperty("Rows").GetArrayLength()==401&&c.GetProperty("Paths").EnumerateArray().All(p=>p.GetArrayLength()==401),"Full corrected route omitted");
        }
        using var policy=JsonDocument.Parse(File.ReadAllText(Path.Combine(target,"inverse-policy.json")));
        Assert(!policy.RootElement.GetProperty("ordinaryStartChanged").GetBoolean()&&!policy.RootElement.GetProperty("nativeInputByReviewer").GetBoolean(),"Offline inverse changed ordinary painting");
    }
    private static void CheckBandResponseIncomplete(string output)
    {
        string source=Path.Combine(output,"track-pair"),target=Path.Combine(output,"band-response-missing");
        string file=Path.Combine(source,"shifted/sample-02/black-band/stroke-009-after.json");string original=File.ReadAllText(file);File.Delete(file);
        bool failed=false;try{StandardBandResponseReview.Run(source,target,allowSynthetic:true);}catch(FileNotFoundException){failed=true;}
        finally{File.WriteAllText(file,original);}
        using var status=JsonDocument.Parse(File.ReadAllText(Path.Combine(target,"response-status.json")));
        Assert(failed&&status.RootElement.GetProperty("status").GetString()=="INCOMPLETE","Missing native metadata accepted");
        Assert(!File.Exists(Path.Combine(target,"band-responses.json")),"Partial response catalog published");
    }
    private static void CheckBandResponseOwnership(string output)
    {
        string source=Path.Combine(output,"track-pair");
        foreach(string target in new[]{source,Path.Combine(source,"nested"),output,Path.Combine(output,"band-response")})
        {
            bool failed=false;try{StandardBandResponseReview.Run(source,target,allowSynthetic:true);}catch(IOException){failed=true;}
            Assert(failed,"Source or existing output writable through review");
        }
        string rejected=Path.Combine(output,"band-response-synthetic-as-native");bool refused=false;
        try{StandardBandResponseReview.Run(source,rejected);}catch(InvalidDataException){refused=true;}
        Assert(refused&&!File.Exists(Path.Combine(rejected,"band-responses.json")),"Synthetic promoted to native profile");
    }
}
