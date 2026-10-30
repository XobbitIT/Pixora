using CanvasForge.App;
using CanvasForge.Core;
using System.IO;
using System.Text.Json;
using System.Security.Cryptography;

internal static partial class Program
{
    private static void StandardSpeedReport(string output,string runFolder,string timingFile)
    {
        var source=Images.Load(Path.Combine(runFolder,"source.png"));
        var original=Settings.Load(Path.Combine(runFolder,"effective-settings.json"));var rates=JsonSerializer.Deserialize<Dictionary<string,MotionTransportRate>>(File.ReadAllText(timingFile))!;
        string Hash(PixelImage im)=>Convert.ToHexString(SHA256.HashData(im.Rgba));var rows=new List<object>();
        foreach(var speed in SpeedProfile.All)
        {
            var s=DrawingWorkflow.Effective(original);s.Set("speed_profile",speed.Name);var plan=Planner.Build(source,s);var groups=TransferSchedule.Build(plan,s);
            var rate=rates.FirstOrDefault(kv=>kv.Key.Replace(',','.')==MotionTransportTiming.Context(s).Replace(',','.')).Value;
            if(rate is not null)s.Set("motion_transport_rate",rate);else s.Data.Remove("motion_transport_rate");
            var work=PaintTimingPlan.Build(s,groups,TransferSchedule.Order(plan,groups));
            var segments=groups.Values.SelectMany(g=>g).SelectMany(b=>b.Segments).ToArray();
            rows.Add(new{speed.Name,speed.Pitch,speed.BatchSize,speed.PointDelay,operations=groups.Values.Sum(g=>g.Count),
                sourceStrokes=groups.Values.Sum(g=>g.Sum(b=>b.SourceStrokes)),colorGroups=groups.Values.Count(g=>g.Count>0),
                targetSha256=Hash(plan.Preview),totalLength=segments.Sum(TransferSchedule.Length),
                movementPoints=segments.Sum(l=>1+(int)Math.Ceiling(TransferSchedule.Length(l)/(double)speed.Pitch)),
                modeledMotionSeconds=work.Where(w=>w.Motion).Sum(w=>w.PlannedSeconds),modeledOverheadSeconds=work.Where(w=>!w.Motion).Sum(w=>w.PlannedSeconds),
                transportRate=rate,scope=rate is null?"requested waits only, no observations for this route":"requested waits plus saved transport-rate model; not a new physical test"});
        }
        File.WriteAllText(Path.Combine(output,"standard-speed-report.json"),JsonSerializer.Serialize(new{rows,
            warning="Turbo/Max Speed change row and point spacing. Equal target hashes do not prove equal painted coverage. Default Rapid is unchanged; live A/B required.",nativeInput=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("STANDARD SPEED OFFLINE REPORT WRITTEN; NO NATIVE INPUT");
    }
}
