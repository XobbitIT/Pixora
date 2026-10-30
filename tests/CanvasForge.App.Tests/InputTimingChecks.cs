using System.IO;
using System.Text.Json;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void CheckNativeSendTimingScope()
    {
        var outer=new NativeSendMetrics();var inner=new NativeSendMetrics();var before=NativeSendMetrics.Current;
        using(outer.Observe())
        {
            Assert(ReferenceEquals(NativeSendMetrics.Current,outer),"Worker input scope absent");
            long t=outer.Begin(48);outer.End(t,48,48);
            using(inner.Observe()){Assert(ReferenceEquals(NativeSendMetrics.Current,inner),"Nested timing scope not isolated");t=inner.Begin(2);inner.End(t,2,1);}
            Assert(ReferenceEquals(NativeSendMetrics.Current,outer)&&Task.Run(()=>NativeSendMetrics.Current is null).GetAwaiter().GetResult(),"Timing observer escaped its worker thread");
        }
        Assert(ReferenceEquals(NativeSendMetrics.Current,before)&&outer.Costs.Calls==1&&outer.Costs.RequestedEvents==48&&outer.Costs.AcceptedEvents==48
            &&outer.Costs.FailedCalls==0&&inner.Costs.FailedCalls==1&&inner.Costs.AcceptedEvents==1,"Timing discarded partial SendInput result or failed to restore scope");
        Console.WriteLine("PASS native-send-timing-worker-scope-nested-restore-partial-counts-no-native-input");
    }
    private static void CheckCursorInputTiming()
    {
        var meter=new CursorReadMetrics();int calls=0;
        var point=meter.Read(()=>{calls++;return new(51,72);});bool failed=false;
        try{meter.Read(()=>{calls++;throw new CursorReadException(5);});}catch(CursorReadException){failed=true;}
        Assert(point==new ScreenPoint(51,72)&&failed&&calls==2&&meter.Costs.Reads==2&&meter.Costs.FailedReads==1&&meter.Costs.Seconds>=0,"Timing changed cursor calls/results/errors");
        var a=new InputCosts(4,.01,2,.02,3,.04,.05,1,.006,1,.007){GuardCursor=new(2,1,.001),MovementPoints=49};
        var b=new InputCosts(2,.004,1,.01,1,.01,.015,0,0,0,0){GuardCursor=new(1,0,.0004),MovementPoints=1};
        var difference=a-b;var rebuilt=difference+b;
        Assert(difference.GuardCursor.Reads==1&&difference.MovementPoints==48&&rebuilt.GuardCursor.Reads==a.GuardCursor.Reads
            &&Math.Abs(rebuilt.GuardCursor.Seconds-a.GuardCursor.Seconds)<1e-12&&rebuilt.MovementPoints==49,"Nested timing deltas dropped counters");
        Console.WriteLine("PASS input-timing-counts-only-real-cursor-reads-and-preserves-errors-and-packet-deltas");
    }
    private static void CheckInputTimingSnapshot(string output)
    {
        string dir=Path.Combine(output,"input-timing-snapshot");Directory.CreateDirectory(dir);
        var estimate=new EtaEstimate(12,EtaBasis.Measured,20,20,5,0,100,1){LowerSeconds=10,UpperSeconds=15};
        var input=new InputCosts(100,.2,10,.03,20,.5,.7,2,.1,3,.2){GuardCursor=new(40,0,.02),MovementPoints=401};
        var snapshot=new PaintRunSnapshot("test","plan","target","running",true,null,new(20,2,10,3,1){Countdown=2,Finish=2},true,5,10,0,5,null,true,1,DateTimeOffset.UtcNow)
            {inputDiagnostics=input,remainingEstimate=estimate};
        PaintRunSnapshotStore.Write(dir,snapshot);
        using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(dir,"run.json")));var root=json.RootElement;
        Assert(root.GetProperty("revision").GetInt32()==4&&root.GetProperty("times").GetProperty("Finish").GetDouble()==2&&root.GetProperty("inputDiagnostics").GetProperty("GuardCursor").GetProperty("Reads").GetInt64()==40
            &&root.GetProperty("remainingEstimate").GetProperty("Seconds").GetDouble()==12&&root.GetProperty("estimateClock").GetString()!.Contains("excludes_pause"),"Run snapshot omitted finish input costs or remaining-clock semantics");
        Assert(root.GetProperty("inputDiagnostics").GetProperty("overlap").GetString()!.Contains("not additive"),"Nested metrics could be interpreted as additive phase totals");
        Console.WriteLine("PASS run-json-separated-phases-nested-input-counters-and-active-remaining-estimate");
    }
}
