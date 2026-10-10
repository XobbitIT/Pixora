using System.Diagnostics;
using System.Text.Json;
using CanvasForge.Core;

internal static class ExecutionTimingChecks
{
    private static void Require(bool ok){if(!ok)throw new Exception("Execution/ETA regression");}
    private static RemainingTime Timer(int count=100)=>new(Enumerable.Range(0,count).Select(i=>new TimedWork($"m{i}","3:H",.08,true)));
    private static Settings Fixture()
    {
        var s=StartPreparationChecks.VerifiedSize3();var original=SpeedCalibration.Read(s)!;var id=original.Samples[0].SpatialId;
        s.Set("speed_probe_profile",original with{Samples=[new(3,StrokeMethod.Paced,false,8,12,1,15,3,1,id),new(3,StrokeMethod.Shift,false,8,12,1,15,3,1,id),new(3,StrokeMethod.Paced,true,8,12,1,15,3,1,id),new(3,StrokeMethod.Shift,true,8,12,1,15,3,1,id)]});
        s.Set("input_frame_delay_ms",100);Require(SpeedCalibration.Use(s));return s;
    }
    private sealed class Input:ICalibratedStrokeInput
    {
        public List<string> Events=new();public double Seconds{get;private set;}
        public void Move(IReadOnlyList<ScreenPoint> points)=>Events.Add("move:"+string.Join(";",points));
        public void Button(bool up)=>Events.Add("button:"+up);public void Shift(bool up)=>Events.Add("shift:"+up);
        public void Wait(double seconds){Seconds+=seconds;Events.Add("wait:"+seconds.ToString("R",System.Globalization.CultureInfo.InvariantCulture));}
    }
    public static void Run(Action<string,Action> test)
    {
        test("ETA includes measured work between strokes after warmup",()=>{
            var t=Timer();for(int i=0;i<20;i++){t.Complete($"m{i}",.092373);t.RecordOperationOverhead($"m{i}",.00989);}
            var e=t.Estimate(0);Require(Math.Abs(e.Seconds-80*(.092373+.00989))<1e-9&&Math.Abs(e.MeanMotionMs-92.373)<1e-9&&Math.Abs(e.OperationOverheadMs-9.89)<1e-9);
        });
        test("Operation overhead follows only the last fifty successful cycles",()=>{
            var t=Timer(150);for(int i=0;i<100;i++){t.Complete($"m{i}",.08);t.RecordOperationOverhead($"m{i}",i<50?.01:.002);}
            var e=t.Estimate(0);Require(e.OperationOverheadSamples==50&&Math.Abs(e.Seconds-50*.082)<1e-9);
        });
        test("Overhead cannot observe skipped aborted duplicate or invalid cycles",()=>{
            var t=Timer();Require(!t.RecordOperationOverhead("m0",10));t.Skip("m0");Require(!t.RecordOperationOverhead("m0",10));
            t.Complete("m1",.08);bool bad=false;try{t.RecordOperationOverhead("m1",double.NaN);}catch(ArgumentOutOfRangeException){bad=true;}
            Require(bad&&t.RecordOperationOverhead("m1",.01)&&!t.RecordOperationOverhead("m1",10)&&t.Estimate(0).OperationOverheadSamples==1);
        });
        test("Additive cycle overhead does not distort long versus short motion ratios",()=>{
            var work=Enumerable.Range(0,20).Select(i=>new TimedWork($"m{i}","3:H",i%2==0?.1:1,true)).Concat(new[]{new TimedWork("short","3:H",.1,true),new("long","3:H",1,true),new("color","color",2,false)});
            var t=new RemainingTime(work);for(int i=0;i<20;i++){t.Complete($"m{i}",i%2==0?.2:2);t.RecordOperationOverhead($"m{i}",.01);}
            Require(Math.Abs(t.Estimate(0).Seconds-4.22)<1e-9);
        });
        test("Pause time and pending audit are separate from operation overhead",()=>{
            var t=new RemainingTime(Enumerable.Range(0,20).Select(i=>new TimedWork($"m{i}","H",.1,true)).Append(new("audit","audit",.2,false)));
            for(int i=0;i<20;i++){t.Complete($"m{i}",.1);t.RecordOperationOverhead($"m{i}",.01);}
            Require(t.Estimate(0).Seconds==.2&&t.Estimate(0)==t.Estimate(1000));t.Complete("audit",.2);Require(t.Estimate(0).Seconds==0);
        });
        test("Frozen execution preserves route selection timing and fast-path policy",()=>{
            var s=Fixture();var e=new StrokeExecutionPlan(s);
            foreach(bool vertical in new[]{false,true})foreach(int n in new[]{0,2,7,8,15,16,31,100,218})
            {
                var l=vertical?new ScreenLine(10,10,10,10+n):new(10,10,10+n,10);var b=new PaintBatch(0,[l],1);
                Require(e.Resolve(3,l)==SpeedCalibration.Resolve(s,3,l)&&e.FastBatch(b)==TransferSchedule.FastBatch(s,b));
                Require(e.Estimate(b)==TransferSchedule.EstimateBatch(s,SpeedProfile.Get(s.Text("speed_profile")),b));
            }
            Require(e.Resolve(3,new(1,1,20,20)) is null&&e.Resolve(3,new(1,1,20,1),4) is null);
        });
        test("Cached dispatch emits identical Shift segments and waits",()=>{
            var s=Fixture();var e=new StrokeExecutionPlan(s);
            foreach(bool v in new[]{false,true})foreach(int n in new[]{8,15,16,31,218})
            {
                var l=v?new ScreenLine(10,10,10,10+n):new(10,10,10+n,10);var a=new Input();var b=new Input();
                var route=SpeedCalibration.Resolve(s,3,l);
                if(route is null){Require(e.Resolve(3,l) is null&&e.Estimate(new(3,[l],1))==StrokeTiming.Estimate(s,SpeedProfile.Get(s.Text("speed_profile")),n,false,false));continue;}
                CalibratedMotion.Draw(l,route,a);CalibratedMotion.Draw(l,e.Resolve(3,l)!,b);
                Require(a.Events.SequenceEqual(b.Events)&&a.Seconds==b.Seconds);
            }
        });
        test("Frozen proof is private and a new execution rejects changed source evidence",()=>{
            var s=Fixture();var line=new ScreenLine(10,10,25,10);var e=new StrokeExecutionPlan(s);var before=e.Resolve(3,line);
            s.Data["speed_probe_profile"]!["Samples"]![0]!["Coverage"]=.9;
            Require(before is not null&&e.Resolve(3,line)==before&&new StrokeExecutionPlan(s).Resolve(3,line) is null&&SpeedCalibration.Resolve(s,3,line) is null);
        });
        test("Invalid margins spatial IDs and missing brush proof never enter execution cache",()=>{
            foreach(string failure in new[]{"margin","spatial","brush"})
            {
                var s=Fixture();if(failure=="brush")s.Data.Remove("brush_footprints");
                else s.Data["speed_probe_profile"]!["Samples"]![0]![failure=="margin"?"SafeMs":"SpatialId"]=failure=="margin"?System.Text.Json.Nodes.JsonValue.Create(8):System.Text.Json.Nodes.JsonValue.Create("wrong");
                Require(new StrokeExecutionPlan(s).Resolve(3,new(10,10,100,10)) is null);
            }
        });
        test("Execution cache removes repeated proof allocations without changing commands",()=>{
            var s=BrushFootprints.Snapshot(Fixture());var b=new PaintBatch(0,[new(10,10,100,10)],1);var p=SpeedProfile.Get(s.Text("speed_profile"));var e=new StrokeExecutionPlan(s);
            e.Resolve(3,b.Segments[0]);const int count=256;
            long Old(){long start=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<count;i++){TransferSchedule.FastBatch(s,b);SpeedCalibration.Resolve(s,3,b.Segments[0]);TransferSchedule.EstimateBatch(s,p,b);}return GC.GetAllocatedBytesForCurrentThread()-start;}
            long New(){long start=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<count;i++){e.FastBatch(b);e.Resolve(3,b.Segments[0]);e.Estimate(b);}return GC.GetAllocatedBytesForCurrentThread()-start;}
            var clock=Stopwatch.StartNew();long oldBytes=Old();double oldMs=clock.Elapsed.TotalMilliseconds;clock.Restart();long newBytes=New();double newMs=clock.Elapsed.TotalMilliseconds;
            Require(newBytes<oldBytes/4);Console.WriteLine("EXECUTION_BENCHMARK "+JsonSerializer.Serialize(new{operations=count,oldMs,newMs,oldBytes,newBytes,inputWaitsChanged=false}));
        });
    }
}
