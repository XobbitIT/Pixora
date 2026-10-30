using CanvasForge.App;
using CanvasForge.Core;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static partial class Program
{
    private static void WriteRc5BrushReplay(string output,string input)
    {
        var results=new List<object>();
        foreach(double size in BrushFootprints.CalibrationSizes)
        {
            var stamps=new List<BrushStamp>();var repeats=new List<object>();
            foreach(int repeat in new[]{1,2,3})
            {
                string stem=$"shape-3-size-{size}-repeat-{repeat}";var m=JsonNode.Parse(File.ReadAllText(Path.Combine(input,stem+"-metrics.json")))!;
                var area=m["Area"]!.Deserialize<ScreenRect>();var command=m["command"]!.Deserialize<ScreenPoint>();
                var proof=m["localColor"]!.Deserialize<BrushLocalColorProof>()!;
                var stamp=BrushLocalColor.Measure(Images.Load(Path.Combine(input,stem+"-before.png")),Images.Load(Path.Combine(input,stem+"-after.png")),
                    Images.Load(Path.Combine(input,stem+"-saturation.png")),new(command.X-area.Left,command.Y-area.Top),size,proof.Requested,proof.PaintSource);
                stamps.Add(stamp);
                File.WriteAllText(Path.Combine(output,stem+"-replay.json"),JsonSerializer.Serialize(stamp));
                repeats.Add(new{repeat,proof.PaintVerified,proof.Passed,referenceDelta=RustSlider.Delta(proof.Reference,proof.ConfirmedReference),
                    proof.StablePixels,solidPixels=BrushFootprints.Points(stamp.Solid).Count(),possiblePixels=BrushFootprints.Points(stamp.Possible).Count(),command});
            }
            var common=BrushFootprints.Points(stamps[0].Solid).ToHashSet();foreach(var stamp in stamps.Skip(1))common.IntersectWith(BrushFootprints.Points(stamp.Solid));
            results.Add(new{size,repeats,commonSolid=common.Count,core=BrushFootprints.Core(BrushFootprints.Spans(common)),contrast=80,tolerance=12,recentered=false});
        }
        File.WriteAllText(Path.Combine(output,"brush-replay-summary.json"),JsonSerializer.Serialize(new{results,nativeInput=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("21 RAW PNG BRUSH MEASUREMENTS REPLAYED; NO NATIVE INPUT");
    }
}
