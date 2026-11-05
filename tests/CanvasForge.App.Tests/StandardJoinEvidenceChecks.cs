using System.IO;
using System.IO.Compression;
using System.Text.Json;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    // F3: run evidence records every joined segment in its executed orientation;
    // single strokes keep the Standard parity; OFF still rejects multi-segment ops.
    private static void CheckStandardJoinEvidence(string output)
    {
        var root=Path.Combine(output,"standard-join-evidence");if(Directory.Exists(root))Directory.Delete(root,true);
        var image=new PixelImage(60,60);for(int k=0;k<3600;k++)image.Set(k,new(40,120,200));
        // Isolated dots of a second color: single strokes at even and odd indices.
        for(int k=0;k<5;k++)image.Set((8+k*10)*60+30,new(250,214,51));
        var s=ReadySettings("English");s.Set("release_profile","Standard");s.Set("color_mode","HEX Direct");s.Set("hex_max_colors","Auto");
        var c=s.Calibration;c.SetRect("canvas",new(100,100,160,160));s.SetCalibration(c);s.Set("cell_px",1);s.Set("fit_mode","fit whole");s.Set("preblur",0);s.Set("edge_preserve",false);s.Set("median_cleanup",false);s.Set("smooth_passes",0);s.Set("min_region",1);
        s.Set(TransferSchedule.StandardJoinKey,true);
        var effective=DrawingWorkflow.Effective(s);var plan=Planner.Build(image,effective);
        var groups=TransferSchedule.Build(plan,effective);var order=TransferSchedule.Order(plan,groups);
        Assert(groups.Values.SelectMany(x=>x).Any(b=>b.Segments.Count>1),"Fixture produced no joined press");
        PaintRunEvidence.SavePlan(root,plan,effective,groups,order);
        using var file=File.OpenRead(Path.Combine(root,"command-plan.json.gz"));using var zip=new GZipStream(file,CompressionMode.Decompress);
        using var json=JsonDocument.Parse(zip);
        int joined=0,single=0;var parities=new HashSet<bool>();
        foreach(var group in json.RootElement.GetProperty("groups").EnumerateArray())
        {
            int color=order[group.GetProperty("group").GetInt32()];
            foreach(var op in group.GetProperty("operations").EnumerateArray())
            {
                int line=op.GetProperty("line").GetInt32();var batch=groups[color][line];
                var segments=op.GetProperty("Segments").EnumerateArray().Select(x=>new ScreenLine(x.GetProperty("X1").GetInt32(),x.GetProperty("Y1").GetInt32(),x.GetProperty("X2").GetInt32(),x.GetProperty("Y2").GetInt32())).ToArray();
                Assert(segments.SequenceEqual(batch.Segments),"Evidence lost or reordered a joined segment");
                if(batch.Segments.Count>1)
                {
                    joined++;
                    Assert(!op.GetProperty("reverse").GetBoolean()&&op.GetProperty("route").GetString()=="standard-join-experimental","Joined press recorded with parity or a wrong route");
                }
                else{single++;parities.Add(line%2==1);Assert(op.GetProperty("reverse").GetBoolean()==(line%2==1),"Single stroke lost its Standard parity");}
            }
        }
        Assert(joined>0,"No joined press reached the evidence");
        Assert(single>=2&&parities.Count==2,$"Singles at even and odd indices are not both covered ({single} singles)");
        var off=effective.Clone();off.Set(TransferSchedule.StandardJoinKey,false);
        bool rejected=false;try{PaintRunEvidence.SavePlan(Path.Combine(root,"off"),plan,off,groups,order);}catch(InvalidDataException){rejected=true;}
        Assert(rejected,"OFF evidence accepted multi-segment Standard operations");
        Console.WriteLine($"PASS standard-join-evidence: {joined} joined presses recorded in executed orientation, {single} singles keep parity, OFF rejects joins");
    }
}
