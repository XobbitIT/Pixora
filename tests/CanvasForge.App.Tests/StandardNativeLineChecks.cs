using System.IO;
using System.IO.Compression;
using System.Text.Json;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void CheckNativeLineGuard()
    {
        int checks=0,releases=0,denials=0,downs=0;
        var reasons=new List<string>();
        var runtime=new GuardedNativeInput.Environment(()=>new(20,50),p=>p.X==50?2:1,()=>1,()=>releases++,up=>{if(!up)downs++;});
        void Denied(){denials++;throw new OperationCanceledException();}
        void ReasonDenied(string reason){reasons.Add(reason);Denied();}
        var noGo=new InputCommandGuard(new(10,10,100,100),new(0,0,200,200),[new(45,45,55,55)]);
        try{GuardedNativeInput.Line(noGo,new(20,50,80,50),()=>checks++,Denied,runtime,ReasonDenied);throw new Exception("Interior control passed");}
        catch(OperationCanceledException){}
        var open=new InputCommandGuard(new(10,10,100,100),new(0,0,200,200),[]);
        try{GuardedNativeInput.Line(open,new(20,50,80,50),()=>checks++,Denied,runtime,ReasonDenied);throw new Exception("Foreign interior window passed");}
        catch(OperationCanceledException){}
        GuardedNativeInput.Line(open,new(20,30,40,30),()=>checks++,Denied,runtime);
        Assert(checks==3&&releases==2&&denials==2&&downs==0,"Line preflight emitted input, skipped a guard or lost release");
        Assert(reasons.SequenceEqual(new[]{"coordinate_out_of_bounds","foreign_window_on_route"}),"Foreign window mislabeled as coordinate failure");
        Console.WriteLine("PASS native-line-prototype-preflight-whole-route-before-input");
    }
    private static void CheckNativeLineRate(string directory)
    {
        string folder=Path.Combine(directory,"native-line-rate-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var s=Settings.Defaults();var rate=new MotionTransportRate(20,.0002,.01);string old=MotionTransportTiming.Context(s);
        s.Set("motion_transport_rate",rate);Painter.LoadTransportTiming(s,folder);
        Assert(s.Data["motion_transport_rate"] is null,"Imported unkeyed rate was trusted");
        File.WriteAllText(Path.Combine(folder,"timing-v3.json"),JsonSerializer.Serialize(new Dictionary<string,MotionTransportRate>{{old,rate}}));
        s.Set("standard_native_line",true);s.Set("motion_transport_rate",rate);Painter.LoadTransportTiming(s,folder);
        Assert(s.Data["motion_transport_rate"] is null,"Freehand rate reused for native line");
        File.WriteAllText(Path.Combine(folder,"timing-v3.json"),JsonSerializer.Serialize(new Dictionary<string,MotionTransportRate>{{MotionTransportTiming.Context(s),rate}}));
        Painter.LoadTransportTiming(s,folder);
        Assert(s.Data["motion_transport_rate"]?.Deserialize<MotionTransportRate>()==rate,"Matching context rate discarded");
        File.WriteAllText(Path.Combine(folder,"timing-v3.json"),"invalid JSON");Painter.LoadTransportTiming(s,folder);
        Assert(s.Data["motion_transport_rate"] is null,"Corrupt keyed history retained stale rate");
        Console.WriteLine("PASS native-line-prototype-timing-context-and-stale-rate-reset");
    }
    private static void CheckNativeLineEvidence(string directory)
    {
        var s=Settings.Defaults();s.Set("standard_native_line",true);s.SetPalette([new(new(0,0,0),new(120,120),"main")]);
        var c=s.Calibration;c.SetRect("canvas",new(10,20,90,100));s.SetCalibration(c);
        var image=new PixelImage(8,8);for(int i=0;i<64;i++)image.Set(i,new(0,0,0));
        var plan=Planner.Build(image,s);var groups=TransferSchedule.Build(plan,s);
        string folder=Path.Combine(directory,"native-line-evidence");Directory.CreateDirectory(folder);
        PaintRunEvidence.SavePlan(folder,plan,s,groups,TransferSchedule.Order(plan,groups),default);
        using var input=new GZipStream(File.OpenRead(Path.Combine(folder,"command-plan.json.gz")),CompressionMode.Decompress);
        using var json=JsonDocument.Parse(input);var root=json.RootElement;
        Assert(root.GetProperty("emitter").GetString()==StandardLineMotion.Revision&&!root.GetProperty("physicalResultVerified").GetBoolean(),"Prototype mislabeled or certified");
        var ops=root.GetProperty("groups").EnumerateArray().SelectMany(g=>g.GetProperty("operations").EnumerateArray()).ToArray();
        Assert(ops.Length>0&&ops.All(o=>o.GetProperty("route").GetString()=="native-line-prototype"),"Plan evidence claims old route");
        Console.WriteLine("PASS native-line-prototype-command-evidence-is-explicit-and-unqualified");
    }
    private static void CheckNativeLineBatchEvidence(string directory)
    {
        var s=Settings.Defaults();s.Set("standard_native_line",true);
        foreach(var segments in new[]{Array.Empty<ScreenLine>(),new[]{new ScreenLine(10,20,30,20),new(30,20,30,40)}})
        {
            string folder=Path.Combine(directory,"unsupported-batch-"+Guid.NewGuid().ToString("N"));
            var groups=new Dictionary<int,List<PaintBatch>>{{0,[new(0,segments,segments.Length)]}};
            bool rejected=false;try{PaintRunEvidence.SavePlan(folder,null!,s,groups,[0]);}catch(InvalidDataException){rejected=true;}
            Assert(rejected&&!Directory.Exists(folder),"Unsupported batch produced incomplete first-segment evidence");
        }
        Console.WriteLine("PASS native-line-standard-evidence-rejects-empty-and-multisegment-batches");
    }
}
