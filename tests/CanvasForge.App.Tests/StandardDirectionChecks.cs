using System.IO;
using System.Text.Json;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private sealed class DirectionClock:ICalibratedStrokeInput
    {
        public double Seconds{get;private set;}
        public ScreenPoint Cursor;
        public List<string> Events=[];
        public void Move(IReadOnlyList<ScreenPoint> points){Cursor=points[^1];Events.Add("move:"+JsonSerializer.Serialize(points));}
        public void Button(bool up)=>Events.Add("button:"+up);
        public void Shift(bool up)=>Events.Add("shift:"+up);
        public void Wait(double seconds){Seconds+=seconds;Events.Add("wait:"+seconds.ToString("R",System.Globalization.CultureInfo.InvariantCulture));}
    }
    private static void CheckDirectionTrace()
    {
        var settings=Settings.Defaults();var p=StandardDirectionExperiment.Create(new(0,0,700,700));
        foreach(var sample in p.Cases)
        {
            var original=new DirectionClock();var observed=new DirectionClock();var trace=new QualityStepInput(observed,()=>observed.Cursor,allowPackets:true);
            StandardStrokeMotion.DrawLegacy(sample.Line,settings,SpeedProfile.Get(sample.SpeedPreset),sample.Reverse,original);
            StandardStrokeMotion.DrawLegacy(sample.Line,settings,SpeedProfile.Get(sample.SpeedPreset),sample.Reverse,trace);
            Assert(original.Events.SequenceEqual(observed.Events)&&original.Seconds==observed.Seconds,"Instrumentation changed emission packets/timing/direction");
            var moves=trace.Samples.Where(s=>s.Phase=="move").ToArray();
            Assert(moves.All(s=>s.Command==s.Cursor&&s.StartedSeconds<=s.Seconds&&s.Commands[^1]==s.Command),"Wrong cursor or timestamp evidence");
            Assert(moves[0].Command==new ScreenPoint(sample.Reverse?sample.Line.X2:sample.Line.X1,sample.Reverse?sample.Line.Y2:sample.Line.Y1),"Reverse begins at wrong endpoint");
            Assert(sample.SpeedPreset=="Safe"?moves.All(s=>s.Commands.Length==1):moves.Any(s=>s.Commands.Length==16&&s.CursorScope=="packet end only"),"Max packets silently reduced to observed single steps");
        }
        var clock=new DirectionClock();var failed=new QualityStepInput(clock,()=>throw new IOException("cursor read failed"),allowPackets:true);
        bool denied=false;try{StandardStrokeMotion.DrawLegacy(new(50,50,450,50),settings,SpeedProfile.Get("Max Speed"),false,failed);}catch(IOException){denied=true;}
        Assert(denied&&!clock.Events.Contains("button:False")&&failed.Samples.Count==1&&!failed.Samples[0].CursorReadSucceeded&&failed.Samples[0].Cursor is null,"Failed cursor read allowed paint-down or claimed a zero cursor");
        Console.WriteLine("PASS direction-pilot-preserves-packets-requested-waits-reverse-and-explicit-read-failures");
    }
    private static void CheckNestedWaitGuardCosts()
    {
        using var delay=new InputDelay(precise:false);int checks=0;
        delay.Wait(0,()=>checks++);Assert(checks==2&&delay.GuardCosts.Calls==0,"Ordinary waits gained diagnostic samples or skipped guards");
        delay.CaptureGuardCosts=true;var before=delay.Costs;
        delay.Wait(.001,()=>Thread.Sleep(2));
        Assert(delay.GuardCosts.Calls>=2&&delay.GuardCosts.Seconds>=.001&&delay.Costs.ActualSeconds-before.ActualSeconds>=delay.GuardCosts.Seconds,
            "Guard time was not nested inside actual wait");
        var guards=delay.GuardCosts;bool interrupted=false;
        try{delay.Wait(0,()=>throw new OperationCanceledException());}catch(OperationCanceledException){interrupted=true;}
        Assert(interrupted&&delay.GuardCosts.Calls==guards.Calls+1&&delay.Costs.Calls==3,"Failure lost guard/wait diagnostics");
        Console.WriteLine("PASS diagnostic-wait-guard-costs-are-opt-in-nested-and-retained-on-interruption");
    }
    private static void CheckLiveRunSnapshot(string output)
    {
        string directory=Path.Combine(output,"live-run-status");Directory.CreateDirectory(directory);
        string resume=Path.Combine(directory,"resume-v3.json");File.WriteAllText(resume,"older resumable progress");
        var worker=(Painter)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Painter));
        void Set(string name,object value)=>typeof(Painter).GetField(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(worker,value);
        void Save(string state)=>typeof(Painter).GetMethod("SaveRunSnapshotMetadata",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(worker,[state]);
        var checkpoint=new CheckpointJournal(resume,new("new-plan",0,0,0),deferUntilPaint:true);double seconds=0;
        var times=new RunTimeBreakdown(()=>seconds);
        Set("runSnapshotDirectory",directory);Set("runIdentity","new-plan");Set("runTargetIdentity","target");
        Set("runSnapshotComplete",true);Set("checkpoint",checkpoint);Set("runTimes",times);Set("timingTotal",50);
        foreach(string state in new[]{"preparing","running"})
        {
            Save(state);
            Assert(File.ReadAllText(resume)=="older resumable progress"&&!checkpoint.OwnsRun,"Status acquired or replaced old checkpoint");
        }
        checkpoint.BeginPaint();checkpoint.Update(new("new-plan",1,2,7));Set("timingDone",7);Set("pauseReason","f6");
        seconds=10;times.Pause();seconds=20;Save("paused");
        using(var data=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"run.json"))))
        {
            var r=data.RootElement;Assert(r.GetProperty("outcome").GetString()=="paused"&&r.GetProperty("done").GetInt32()==7&&r.GetProperty("total").GetInt32()==50
                &&r.GetProperty("group").GetInt32()==1&&r.GetProperty("line").GetInt32()==2&&r.GetProperty("pauseReason").GetString()=="f6"
                &&r.GetProperty("times").GetProperty("Pause").GetDouble()==10&&r.GetProperty("sequence").GetInt64()==3,"Paused state/progress/time not current");
        }
        times.Resume();seconds=25;Save("running");
        foreach(string state in new[]{"completed","cancelled","failed"})
        {Save(state);using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"run.json")));Assert(doc.RootElement.GetProperty("outcome").GetString()==state,"Terminal state stale");}
        Assert(File.ReadAllText(resume)=="older resumable progress"&&!File.Exists(Path.Combine(directory,"run.json.tmp")),"Diagnostic snapshot touched checkpoint or left partial temp file");
        Console.WriteLine("PASS run-snapshot-start-pause-resume-terminal-progress-and-checkpoint-isolation");
    }
    private static void CheckDirectionSeriesFreeze(string output)
    {
        string legacyDir=Path.Combine(output,"direction-legacy-readonly");Directory.CreateDirectory(legacyDir);
        var s=Settings.Defaults();var cal=s.Calibration;cal.SetRect("canvas",new(0,0,700,700));s.SetCalibration(cal);
        string raw=s.Data.ToJsonString();File.WriteAllText(Path.Combine(legacyDir,"config-csharp.json"),raw);
        var loaded=StandardDirectionEvidenceCli.LoadSettingsSnapshot(legacyDir);
        Assert(Directory.GetFiles(legacyDir).Length==1&&File.ReadAllText(Path.Combine(legacyDir,"config-csharp.json"))==raw,"Evidence init imported or changed legacy settings");
        string frozen=loaded.Data.ToJsonString(),series=Path.Combine(output,"direction-frozen-series-"+Guid.NewGuid().ToString("N"));
        StandardDirectionEvidenceCli.CreateSeries(series,loaded);
        Assert(loaded.Data.ToJsonString()==frozen,"Init changed caller settings");
        using(var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(series,"protocol.json"))))
            Assert(manifest.RootElement.GetProperty("Revision").GetString()==StandardDirectionExperiment.Revision
                &&manifest.RootElement.GetProperty("Protocol").GetProperty("Cases").GetArrayLength()==24,"Wrong frozen pilot manifest");
        bool missing=false;try{StandardDirectionEvidenceCli.Run(["--standard-direction-evidence","sample",series,"1"]);}catch(InvalidDataException e) when(e.Message.Contains("mandatory sample")){missing=true;}
        Assert(missing,"Missing mandatory predecessor did not block before native input");
        File.AppendAllText(Path.Combine(series,"protocol.json")," ");bool changed=false;
        try{StandardDirectionEvidenceCli.Run(["--standard-direction-evidence","sample",series,"0"]);}catch(InvalidDataException){changed=true;}
        Assert(changed&&!Directory.Exists(Path.Combine(series,"sample-00")),"Modified protocol reached collection");
        Console.WriteLine("PASS direction-init-readonly-frozen-manifest-and-missing-sample-block-before-native-input");
    }
}
