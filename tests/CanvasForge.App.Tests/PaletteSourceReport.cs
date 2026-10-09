using System.IO;
using System.Text.Json;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void WritePaletteSourceReport(string output,string config,string imagePath)
    {
        var s=Settings.Load(config);var image=Images.Load(imagePath);var cases=new List<object>();
        foreach(string limit in new[]{"Auto","3","4","8"})
        {
            var snapshot=s.Clone();snapshot.Set("max_colors",limit);
            var plan=Planner.Build(image,snapshot);Images.Save(plan.Preview,Path.Combine(output,"palette-"+limit+".png"));
            cases.Add(new{limit,plan.ColorCount,sourceStrokes=plan.Strokes.Values.Sum(p=>p.Count),plan.Error,
                colors=plan.Counts.OrderByDescending(p=>p.Value).Select(p=>new{color=plan.Palette[p.Key].Color.Hex,cells=p.Value}),
                physicalBrushReady=PaintingReadiness.BrushProblem(snapshot) is null});
        }
        var histogram=Enumerable.Range(0,image.Width*image.Height).GroupBy(i=>image.Color(i).Hex)
            .Select(g=>new{color=g.Key,pixels=g.Count()}).OrderByDescending(p=>p.pixels).ToArray();
        var report=new{scope="Offline source-image/palette analysis, not a Rust paint or active-color test",inGameTest=false,
            sourceWidth=image.Width,sourceHeight=image.Height,uniqueRgbColors=histogram.Length,dominant=histogram.Take(15),cases};
        File.WriteAllText(Path.Combine(output,"Palette_Source_Analysis.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("OFFLINE PALETTE SOURCE REPORT COMPLETE");
    }
}
