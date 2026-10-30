using CanvasForge.Core;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;

internal static class Rc4LiveReplay
{
    private static PixelImage Load(string path)
    {using var gz=new GZipStream(File.OpenRead(path),CompressionMode.Decompress);using var r=new BinaryReader(gz);int w=r.ReadInt32(),h=r.ReadInt32();return new(w,h,r.ReadBytes(w*h*4));}
    public static void Run(string input,string output)
    {
        var rows=new List<object>();
        foreach(string folder in Directory.GetDirectories(input).Order())
        {
            var s=new Settings((JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(folder,"effective-settings.json")))!);
            if(!s.Bool("adaptive_brush"))continue;
            var clock=Stopwatch.StartNew();var plan=Planner.Build(Load(Path.Combine(folder,"source.rgba.gz")),s);
            var baseline=MeasuredColorPlan.BuildGeometry(plan,s)!;
            var selected=MeasuredColorPlan.TryBuild(plan,s)!;var rect=s.Calibration.Rect("canvas");
            var profiles=BrushFootprints.Read(s,true).ToDictionary(p=>p.Id);var coverage=new bool[rect.Width*rect.Height];int verified=0;
            int[] xb=Coverage.Partition(0,rect.Width,plan.Width),yb=Coverage.Partition(0,rect.Height,plan.Height);
            var labels=new int[coverage.Length];
            for(int y=0;y<plan.Height;y++)for(int x=0;x<plan.Width;x++)for(int py=yb[y];py<yb[y+1];py++)
                Array.Fill(labels,plan.Indices[y*plan.Width+x],py*rect.Width+xb[x],xb[x+1]-xb[x]);
            foreach(var(color,ops) in selected.Groups)foreach(var op in ops)
            {
                var p=profiles[op.ProfileId!];var l=op.Line;
                for(int k=0;k<=TransferSchedule.Length(l);k++)
                {
                    int cx=l.X1-rect.Left+k*Math.Sign(l.X2-l.X1),cy=l.Y1-rect.Top+k*Math.Sign(l.Y2-l.Y1);
                    if(!BrushFootprints.Safe(p,cx,cy,rect.Width,rect.Height,(x,y)=>labels[y*rect.Width+x]==color))
                        throw new Exception("Unsafe possible-mask projection: "+Path.GetFileName(folder));
                    foreach(var span in p.Solid)Array.Fill(coverage,true,(cy+span.Y)*rect.Width+cx+span.Left,span.Right-span.Left);
                }
                verified++;
            }
            int covered=coverage.Count(p=>p);
            if(covered!=selected.CoveredPixels)throw new Exception("Reported coverage differs from independent command raster");
            for(int i=0;i<coverage.Length;i++)if(selected.UnplannedMask[i]!=(labels[i]>=0&&!coverage[i]))throw new Exception("Missing mask differs from command raster");
            double Seconds(MeasuredColorResult r){var groups=TransferSchedule.Build(plan,s,r.Groups);return PaintTimingPlan.Build(s,groups,TransferSchedule.Order(plan,groups),forSelection:true).Sum(w=>w.PlannedSeconds);}
            rows.Add(new{run=Path.GetFileName(folder),plan.Width,plan.Height,colors=plan.Counts.Count,
                baseline=new{operations=baseline.Groups.Values.Sum(g=>g.Count),seconds=Seconds(baseline),baseline.CoveredPixels,baseline.UnplannedPixels},
                selected=new{operations=verified,seconds=Seconds(selected),selected.CoveredPixels,selected.UnplannedPixels,
                    profiles=selected.Groups.Values.SelectMany(g=>g).GroupBy(op=>op.Size).OrderBy(g=>g.Key).Select(g=>new{size=g.Key,operations=g.Count()}).ToArray(),
                    choice=selected.Diagnostics?.Choice,overlap=selected.Diagnostics?.StrokeSavings},
                replayMilliseconds=clock.ElapsedMilliseconds,possibleMaskViolations=0,independentCoverageMatches=true});
            Console.WriteLine("REPLAY "+Path.GetFileName(folder)+" "+baseline.Groups.Values.Sum(g=>g.Count)+" -> "+verified+" commands");
        }
        File.WriteAllText(output,JsonSerializer.Serialize(new{version=BuildInfo.Full,scope="Offline current planning on saved RC3 live inputs and certified masks; timings are model estimates, not a new Rust live test. Baseline and selected may have different coverage; same-coverage alternatives are inside choice.",rows},new JsonSerializerOptions{WriteIndented=true}));
    }
}
