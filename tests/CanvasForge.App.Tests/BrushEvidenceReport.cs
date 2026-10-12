using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void WriteBrushEvidenceReport(string output,string input)
    {
        foreach(string run in Directory.EnumerateDirectories(input,"run-*"))
        {
            string dest=Path.Combine(output,Path.GetFileName(run));Directory.CreateDirectory(dest);var stamps=new List<BrushStamp>();
            foreach(string path in Directory.EnumerateFiles(run,"shape-3-size-3-repeat-*-metrics.json").Order())
            {
                string stem=Path.GetFileName(path)[..^13];var m=JsonNode.Parse(File.ReadAllText(path))!;
                var area=m["Area"]!.Deserialize<ScreenRect>();var command=m["command"]!.Deserialize<ScreenPoint>();
                var proof=m["localColor"]!.Deserialize<BrushLocalColorProof>()!;
                var stamp=BrushLocalColor.Measure(Images.Load(Path.Combine(run,stem+"-before.png")),Images.Load(Path.Combine(run,stem+"-after.png")),
                    Images.Load(Path.Combine(run,stem+"-saturation.png")),new(command.X-area.Left,command.Y-area.Top),3,proof.Requested,proof.PaintSource);
                stamps.Add(stamp);File.WriteAllText(Path.Combine(dest,stem+"-core-replay.json"),JsonSerializer.Serialize(stamp,new JsonSerializerOptions{WriteIndented=true}));
                var mask=new PixelImage(40,40);for(int i=0;i<1600;i++)mask.Set(i,new(28,31,38));
                foreach(var p in BrushFootprints.Points(stamp.Possible))mask.Set((p.Y+20)*40+p.X+20,new(120,120,120));
                foreach(var p in BrushFootprints.Points(stamp.Solid))mask.Set((p.Y+20)*40+p.X+20,new(30,160,240));mask.Set(20*40+20,new(255,128,0));
                Images.Save(mask,Path.Combine(dest,stem+"-command-mask.png"));
            }
            var common=stamps.Count==0?new HashSet<ScreenPoint>():BrushFootprints.Points(stamps[0].Solid).ToHashSet();foreach(var stamp in stamps.Skip(1))common.IntersectWith(BrushFootprints.Points(stamp.Solid));
            var core=BrushFootprints.Core(BrushFootprints.Spans(common));File.WriteAllText(Path.Combine(dest,"intersection-replay.json"),JsonSerializer.Serialize(new{repeats=stamps.Count,commonPixels=common.Count,core,solidCounts=stamps.Select(p=>p.Solid.Sum(s=>s.Right-s.Left)).ToArray(),contrast=80,tolerance=12,aligned=false},new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"REPLAY {Path.GetFileName(run)}: solid [{string.Join(',',stamps.Select(p=>p.Solid.Sum(s=>s.Right-s.Left)))}], common={common.Count}, core={core}");
        }
    }
}
