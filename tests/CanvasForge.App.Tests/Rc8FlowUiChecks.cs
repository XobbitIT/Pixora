using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void CheckRc8SetupScene(string output)
    {
        var folder=Path.Combine(output,"rc8-scene-flow");Directory.CreateDirectory(folder);
        var s=ReadySettings("English");s.Set("release_profile","Standard");s.Save(Path.Combine(folder,SettingsStore.ConfigName));
        var w=FixtureWindow(folder);int reads=0,captures=0;
        w.SetupSceneInspector=()=>Task.FromResult(++reads==1?new EditorEvidenceResult(EditorEvidenceState.Missing,"editor_structure_mismatch",["canvas-structure"])
            :new EditorEvidenceResult(EditorEvidenceState.Present,"present",[]));
        w.SetupCaptureAction=scope=>{Assert(scope==SetupCaptureScope.Canvas,"Canvas change requested unrelated capture");captures++;return Task.CompletedTask;};
        CompleteUiTask(()=>(Task)Invoke(w,"EnsureSetupScene")!);
        Assert(reads==2&&captures==1,"Combined setup did not reprove captured scene");
        w.SetupSceneInspector=()=>Task.FromResult(new EditorEvidenceResult(EditorEvidenceState.Ambiguous,"unavailable",["canvas-structure"]));
        bool failed=false;
        CompleteUiTask(async()=>{try{await (Task)Invoke(w,"EnsureSetupScene")!;}catch(InvalidDataException){failed=true;}});
        Assert(failed&&captures==2,"Unproved recapture escaped into Colors");
        SetField(w,"source",new PixelImage(16,16));SetField(w,"setupRunning",true);Invoke(w,"UpdateReady");
        Assert(Field<TextBlock>(w,"badge").Text=="RUNNING"&&!Field<TextBlock>(w,"ready").Text.Contains("Everything is ready"),"Setup shows green Ready");
        SetField(w,"preparationResume",new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));Invoke(w,"UpdateReady");
        Assert(Field<TextBlock>(w,"badge").Text=="PAUSED"&&Field<TextBlock>(w,"ready").Text.Contains("Resume")
            &&Field<List<TextBlock>>(w,"setupSummaries").All(t=>t.Text.Contains("paused")),"Setup pause contradicts readiness or summary");
        SetField(w,"preparationResume",null);SetField(w,"setupRunning",false);SetField(w,"setupCompletion",SetupStageState.Cancelled);
        SetField(w,"setupOutcome","Setup cancelled. Completed stages are retained; clear the test Canvas.");Invoke(w,"UpdateReady");
        Assert(Field<TextBlock>(w,"badge").Text=="SETUP REQUIRED"&&Field<TextBlock>(w,"ready").Text.Contains("cancelled"),"Cancelled setup silently shows Ready");
        Render(w,Path.Combine(output,"rc8-setup-cancelled.png"),1280,780,false);
        SetField(w,"setupCompletion",null);Invoke(w,"UpdateReady");
        Console.WriteLine("PASS rc8-one-flow-recapture-proof-and-running-paused-cancelled-status");
    }
    private static void CheckRc8TargetControls(string output)
    {
        string folder=Path.Combine(output,"rc8-target-controls");Directory.CreateDirectory(folder);
        var s=ReadySettings("English");s.Set("release_profile","Standard");s.Set("edge_merge",true);s.Save(Path.Combine(folder,SettingsStore.ConfigName));
        var w=FixtureWindow(folder);var paint=Field<Dictionary<string,FrameworkElement>>(w,"pages")["paint"];
        var checkbox=LogicalNodes(paint).OfType<CheckBox>().Single(c=>Equals(c.Tag,"edge_merge"));
        Assert(checkbox.IsChecked==true,"Existing ON preference hidden or silently changed");
        checkbox.IsChecked=false;Invoke(w,"ReadSettings");Assert(!Field<Settings>(w,"settings").Bool("edge_merge"),"Standard cannot disable destructive edge merging");
        Assert(!LogicalNodes(paint).OfType<Button>().Any(b=>Equals(b.Content,"Compare ordinary and Adaptive")),"Research comparison added to Standard");
        Render(w,Path.Combine(output,"rc8-standard-target-controls.png"),1280,780);
        Console.WriteLine("PASS rc8-standard-edge-merge-visible-editable-without-silent-reset");
    }
    private static void CheckRc8NativeEvidence(string output)
    {
        string folder=Path.Combine(output,"rc8-native-evidence");var image=new PixelImage(8,8);
        for(int i=0;i<64;i++)image.Set(i,new((byte)i,25,150));
        var rect=new ScreenRect(10,20,18,28);
        PaintRunEvidence.Save(folder,"before",image,rect,image,rect,new{dpi=144,mapping="identity",nativeInput=false});
        var reread=Images.Load(Path.Combine(folder,"native-before.png"));Assert(reread.Rgba.SequenceEqual(image.Rgba),"Native evidence pixels changed");
        using var metadata=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"native-before.json")));
        Assert(!metadata.RootElement.GetProperty("physicalQualityCertified").GetBoolean(),"Lossless capture claimed physical certification");
        var s=Settings.Defaults();var c=s.Calibration;c.SetRect("canvas",rect);s.SetCalibration(c);s.SetPalette([new(new(0,0,0),new(50,50),"main")]);
        var plan=Planner.Build(image,DrawingWorkflow.Effective(s));var groups=TransferSchedule.Build(plan,s);
        PaintRunEvidence.SavePlan(folder,plan,s,groups,TransferSchedule.Order(plan,groups));
        using var file=File.OpenRead(Path.Combine(folder,"command-plan.json.gz"));using var gzip=new GZipStream(file,CompressionMode.Decompress);
        using var commands=JsonDocument.Parse(gzip);
        Assert(commands.RootElement.GetProperty("Identity").GetString()==plan.Identity
            &&commands.RootElement.GetProperty("plannedNotObserved").GetBoolean(),"Command evidence identity/provenance missing");
        try{PaintRunEvidence.Save(folder,"after",image,new(10,20,19,28),image,rect,new{});Assert(false,"Mismatched screenshot geometry accepted");}catch(ArgumentException){}
        Console.WriteLine("PASS rc8-lossless-native-evidence-and-replayable-plan-without-quality-verdict");
    }
    private sealed class Rc8TraceClock:ICalibratedStrokeInput
    {
        public double Seconds{get;private set;}
        public ScreenPoint Cursor;
        public int Downs,Ups;
        public void Move(IReadOnlyList<ScreenPoint> points){foreach(var p in points)Cursor=p;}
        public void Wait(double seconds)=>Seconds+=seconds;
        public void Button(bool up){if(up)Ups++;else Downs++;}
        public void Shift(bool up){}
    }
    private static void CheckRc8StrokeTrace()
    {
        var s=Settings.Defaults();var speed=SpeedProfile.Get("Rapid");var ordinary=new Rc8TraceClock();var recorded=new Rc8TraceClock();
        var trace=new StrokeTraceInput(recorded,()=>recorded.Cursor);
        var line=new ScreenLine(30,30,70,30);
        StandardStrokeMotion.Draw(line,s,speed,true,ordinary);StandardStrokeMotion.Draw(line,s,speed,true,trace);
        Assert(recorded.Seconds==ordinary.Seconds&&recorded.Downs==1&&recorded.Ups==1&&recorded.Cursor==ordinary.Cursor,"Recording changed emitter commands or requested waits");
        Assert(trace.Samples.Any(p=>p.Phase=="held")&&trace.Samples.Any(p=>p.Phase=="released")
            &&trace.Samples.Where(p=>p.Phase=="move").All(p=>p.Commands[^1]==p.Cursor),"Recorded command/cursor phases missing");
        var limited=new StrokeTraceInput(recorded,()=>recorded.Cursor,2);
        for(int i=0;i<10;i++)limited.Move([new(40+i,30)]);
        Assert(limited.Truncated&&limited.Samples.Count==2,"Unbounded trace growth");
        var failed=new StrokeTraceInput(recorded,()=>throw new CursorReadException(5));
        try{StandardStrokeMotion.Draw(line,s,speed,false,failed);Assert(false,"Cursor read error masked by trace");}catch(CursorReadException){}
        Console.WriteLine("PASS rc8-opt-in-cursor-trace-preserves-commands-and-bounds-data");
    }
}
