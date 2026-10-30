using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Controls;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void CheckStartUnavailableReasons(string output,string language)
    {
        bool en=language=="English";string name="start-reasons-"+(en?"en":"ua");
        string directory=Path.Combine(output,name);Directory.CreateDirectory(directory);
        var baseline=ReadySettings(language);baseline.Set("release_profile","Standard");baseline.Save(Path.Combine(directory,SettingsStore.ConfigName));
        var w=FixtureWindow(directory);baseline=Field<Settings>(w,"settings").Clone();SetField(w,"source",new PixelImage(16,16));
        void Check(Action<Settings> mutation,string token)
        {
            var s=baseline.Clone();mutation(s);SetField(w,"settings",s);Invoke(w,"UpdateReady");
            var button=Field<Button>(w,"startButton");string tooltip=button.ToolTip?.ToString()??"";
            Assert(!button.IsEnabled&&tooltip.Contains(token,StringComparison.Ordinal)&&Field<TextBlock>(w,"ready").Text==tooltip,
                "START and its visible reason disagree: "+tooltip);
            if(en)Assert(!System.Text.RegularExpressions.Regex.IsMatch(tooltip,@"[\u0400-\u04FF]"),"Unavailable reason is not localized: "+tooltip);
        }
        Check(s=>{var c=s.Calibration;c.SetRect("canvas",default);s.SetCalibration(c);},en?"Canvas:":"Полотно:");
        Check(s=>{var c=s.Calibration;c.SetRect("size_track",default);s.SetCalibration(c);},en?"Size: the brush-size track":"Size: не захоплена смуга");
        Check(s=>{var c=s.Calibration;c.SetRect("size_value_field",default);s.SetCalibration(c);},en?"Size: the brush-size numeric field":"Size: не захоплене числове поле");
        Check(s=>s.Set("controls_validation_failed",true),en?"Size: the previous control check failed":"Size: попередню перевірку");
        Check(s=>{s.Data.Remove(EditorEvidence.Key);s.Data.Remove("editor_references");},en?"Editor: no saved evidence":"Редактор: немає збереженого доказу");
        Check(s=>{var c=s.Calibration;var r=c.Rect("canvas");c.SetRect("canvas",r with{Left=r.Left+1});s.SetCalibration(c);},en?"Canvas: the selection changed":"Полотно: виділення змінилося");
        Check(s=>{var c=s.Calibration;c.SetRect("size_track",c.Rect("size_track") with{Left=c.Rect("size_track").Left+1});s.SetCalibration(c);},en?"Size: the track changed":"Size: смуга змінилася");
        SetField(w,"settings",baseline.Clone());SetField(w,"imageLoading",true);Invoke(w,"UpdateReady");
        Assert(!Field<Button>(w,"startButton").IsEnabled&&Field<Button>(w,"startButton").ToolTip.ToString()!.Contains(en?"still loading":"ще завантажується"),"Busy START lacks an image-loading reason");
        Assert(Field<TextBlock>(w,"badge").Text==(en?"RUNNING":"ВИКОНУЄТЬСЯ")&&Field<TextBlock>(w,"ready").Text==Field<Button>(w,"startButton").ToolTip.ToString(),"Image loading still claims green Ready");
        SetField(w,"imageLoading",false);SetField(w,"startPreparing",true);Invoke(w,"UpdateReady");
        Assert(!Field<Button>(w,"startButton").IsEnabled&&Field<Button>(w,"startButton").ToolTip.ToString()!.Contains(en?"Preparing the plan":"Готую план"),"Busy START claims it can start");
        SetField(w,"startPreparing",false);Invoke(w,"UpdateReady");
        Assert(Field<Button>(w,"startButton").IsEnabled,"Correcting the saved evidence did not restore START");
        Console.WriteLine("PASS "+name+"-canvas-editor-size-and-busy-reasons");
    }

    private static void CheckStartPlanningJournal(string output)
    {
        var w=LifecycleWindow(output,"start-planning-journal");SetField(w,"source",new PixelImage(8,8));
        string folder=Path.Combine(output,"start-planning-journal"),checkpoint=Path.Combine(folder,"resume-v3.json");
        File.WriteAllText(checkpoint,"older resumable progress");
        var gate=new TaskCompletionSource<PaintPlan>(TaskCreationOptions.RunContinuationsAsynchronously);
        w.PlanBuilder=(_,_,_,_)=>gate.Task;string? directory=null;DateTimeOffset requested=DateTimeOffset.UtcNow;
        CompleteUiTask(async()=>
        {
            var task=LifecycleTask(w,"Start",false);
            directory=Directory.GetDirectories(Path.Combine(folder,"last-run")).Single();
            using(var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"run.json"))))
            {
                var r=json.RootElement;Assert(r.GetProperty("outcome").GetString()=="planning"&&r.GetProperty("identity").ValueKind==JsonValueKind.Null
                    &&r.GetProperty("startedUtc").GetDateTimeOffset()>=requested&&!r.GetProperty("checkpointOwned").GetBoolean(),"START has no current planning status before plan completion");
            }
            Invoke(w,"CancelActiveWork");gate.SetResult(Planner.Build(Field<PixelImage>(w,"source"),Field<Settings>(w,"settings")));await task;
        });
        using(var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory!,"run.json"))))
        {
            var r=json.RootElement;Assert(r.GetProperty("outcome").GetString()=="cancelled"&&r.GetProperty("done").GetInt32()==0&&r.GetProperty("sequence").GetInt64()==2
                &&r.GetProperty("updatedUtc").GetDateTimeOffset()>=r.GetProperty("startedUtc").GetDateTimeOffset(),"Cancelled planning kept a stale run state");
        }
        Assert(File.ReadAllText(checkpoint)=="older resumable progress"&&Field<object?>(w,"painter") is null,"Planning metadata changed checkpoint or reached native input");
        // Accepted START can be cancelled after scheduling but before the worker
        // starts its clock. It must not leave the shared journal at planning.
        string early=PaintRunStart.Begin(folder,DateTimeOffset.UtcNow,true);
        var earlyWorker=(Painter)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Painter));
        void Set(string name,object? value)=>typeof(Painter).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(earlyWorker,value);
        Set("settings",Settings.Defaults());Set("token",new CancellationToken(true));Set("runSnapshotDirectory",early);Set("runSnapshotSequence",1L);
        bool cancelled=false;try{earlyWorker.Run(null!);}catch(OperationCanceledException){cancelled=true;}
        using(var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(early,"run.json"))))
            Assert(cancelled&&json.RootElement.GetProperty("outcome").GetString()=="cancelled"&&json.RootElement.GetProperty("phase").GetString()=="Planning"
                &&json.RootElement.GetProperty("times").ValueKind==JsonValueKind.Null,"Early worker cancellation retained stale planning or invented measured timings");
        Assert(File.ReadAllText(checkpoint)=="older resumable progress","Cancelled worker changed checkpoint before its first stroke");
        Console.WriteLine("PASS start-planning-cancel-journal-without-checkpoint-or-native-input");
    }

    private static void CheckF6RunJournal(string output)
    {
        string directory=Path.Combine(output,"f6-run-journal");Directory.CreateDirectory(directory);
        string resume=Path.Combine(directory,"resume-v3.json");File.WriteAllText(resume,"older progress");
        var worker=(Painter)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Painter));
        void Set(string name,object? value)=>typeof(Painter).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(worker,value);
        void Save(string state)=>typeof(Painter).GetMethod("SaveRunSnapshotMetadata",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(worker,[state]);
        var checkpoint=new CheckpointJournal(resume,new("new-plan",0,0,0),deferUntilPaint:true);
        double seconds=0;var times=new RunTimeBreakdown(()=>seconds);
        Set("runSnapshotDirectory",directory);Set("runIdentity","new-plan");Set("runTargetIdentity","target");
        Set("runTimes",times);Set("checkpoint",checkpoint);Set("timingTotal",50);Set("logPath",Path.Combine(directory,"session.jsonl"));
        var requested=DateTimeOffset.UtcNow.AddSeconds(-30);Set("runStartedUtc",(DateTimeOffset?)requested);
        Save("countdown");
        Assert(File.ReadAllText(resume)=="older progress"&&!checkpoint.OwnsRun,"Countdown metadata replaced an old checkpoint");
        checkpoint.BeginPaint();checkpoint.Update(new("new-plan",1,2,7));Set("timingDone",7);
        seconds=10;worker.HandlePauseHotkey(true);worker.HandlePauseHotkey(true);
        Assert(worker.Paused,"One held F6 key toggled pause twice");worker.BeginRunPauseObservation();
        seconds=20;Save("paused");
        using(var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"run.json"))))
        {
            var r=json.RootElement;Assert(r.GetProperty("outcome").GetString()=="paused"&&r.GetProperty("pauseReason").GetString()=="f6"
                &&r.GetProperty("done").GetInt32()==7&&r.GetProperty("total").GetInt32()==50&&r.GetProperty("group").GetInt32()==1&&r.GetProperty("line").GetInt32()==2
                &&r.GetProperty("times").GetProperty("Pause").GetDouble()==10&&r.GetProperty("startedUtc").GetDateTimeOffset()==requested
                &&r.GetProperty("stateChangedUtc").GetDateTimeOffset()<=r.GetProperty("updatedUtc").GetDateTimeOffset()
                &&r.GetProperty("progressScope").GetString()=="completed_operations","F6 status has stale reason, counts, positions or timestamps");
        }
        Assert(File.ReadAllText(resume)=="older progress"&&!File.Exists(Path.Combine(directory,"run.json.tmp")),"Pause diagnostic metadata changed checkpoint or left a temporary file");
        Console.WriteLine("PASS F6-handler-and-pause-boundary-publish-current-progress-times-without-checkpoint-mutation");
    }
}
