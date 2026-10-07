using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void CheckUnmeasuredAdaptiveClick(MainWindow window)
    {
        var toggle=Field<CheckBox>(window,"adaptiveEnabled");Assert(toggle.IsEnabled,"Missing adaptive explanation");
        toggle.IsChecked=true;toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert(!Field<Settings>(window,"settings").Bool("adaptive_brush")&&toggle.IsChecked==false,"Unmeasured Size 1 enabled adaptive");
    }
    private static void CheckUnifiedSetup(string output,string language,int width)
    {
        bool en=language=="English";string name="unified-"+(en?"en":"ua"),directory=Path.Combine(output,name);Directory.CreateDirectory(directory);
        var s=Settings.Defaults();s.Set("language",language);s.Set("drawing_mode","Simple");s.Set("manual_brush_controls",true);s.Set("coverage_audit",true);s.Save(Path.Combine(directory,"config-csharp.json"));
        var w=new MainWindow(directory);var pages=Field<Dictionary<string,FrameworkElement>>(w,"pages");
        Assert(pages.Count==5&&pages.ContainsKey("adaptive")&&pages.ContainsKey("speed"),"Single interface lost advanced pages");
        var live=Field<Settings>(w,"settings");Assert(!live.Data.ContainsKey("drawing_mode")&&!live.Data.ContainsKey("manual_brush_controls")&&live.Bool("coverage_audit"),"Migration changed preferences or retained manual bypass");
        var buttons=Field<List<Button>>(w,"allSetupButtons");Assert(buttons.Count==2&&buttons.All(b=>b.IsEnabled),"Fresh automatic setup is inaccessible");
        var results=Field<Dictionary<SetupStage,(SetupStageState State,string Detail)>>(w,"setupResults");
        results[SetupStage.Capture]=(SetupStageState.Passed,"");results[SetupStage.Brush]=(SetupStageState.Failed,"Size 1");Invoke(w,"RefreshSetupStatus");
        var rows=Field<List<Dictionary<SetupStage,TextBlock>>>(w,"setupRows").Single();
        Assert(rows[SetupStage.Brush].Text.Contains(en?"failed":"помилка")&&Equals(rows[SetupStage.Brush].ToolTip,"Size 1"),"Failed stage has no explanation");
        Assert(rows[SetupStage.Speed].Text.Contains(en?"pending":"очікує"),"Unexecuted Speed is shown as passed");
        Invoke(w,"BuildUi");Assert(Field<Dictionary<SetupStage,(SetupStageState State,string Detail)>>(w,"setupResults")[SetupStage.Brush].State==SetupStageState.Failed,"UI rebuild lost partial results");
        string failure="Вимірювання пензля: Size 1 не має підтвердженого суцільного ядра. Відкрий «Пензель» і переглянь причини; швидкі проби не запускалися.";
        SetField(w,"setupOutcome",failure);results=Field<Dictionary<SetupStage,(SetupStageState State,string Detail)>>(w,"setupResults");
        results[SetupStage.Controls]=(SetupStageState.Passed,"Форма 4 · Нові підтверджені Size: 1, 3");Invoke(w,"RefreshSetupStatus");
        if(en)Assert(Field<List<TextBlock>>(w,"setupSummaries").All(t=>t.Text.Contains("Brush measurement")&&t.Text.Contains("no verified solid core")),"Cached setup error did not translate");
        w.ShowPage("capture");Render(w,Path.Combine(output,name+".png"),width,780);
        Assert(Captions(Field<Dictionary<string,FrameworkElement>>(w,"pages")["capture"]).Any(c=>c.Contains(en?"Test Rust controls":"Перевірити керування Rust")),"Individual controls disappeared");
        SetField(w,"setupRunning",true);w.SetEditing(true);
        Assert(!Field<Button>(w,"startButton").IsEnabled&&Field<Button>(w,"stopButton").IsEnabled&&!Field<Dictionary<string,FrameworkElement>>(w,"pages")["capture"].IsEnabled,"An inner check re-enabled editing during setup");
        SetField(w,"setupRunning",false);w.SetEditing(true);Console.WriteLine("PASS "+name);
    }
    private static void CheckUnifiedFreshWindow(string output)
    {
        string d=Path.Combine(output,"unified-fresh");Directory.CreateDirectory(d);var w=new MainWindow(d);
        Assert(!Field<Button>(w,"startButton").IsEnabled&&Field<List<Button>>(w,"allSetupButtons").All(b=>b.IsEnabled),"Fresh setup requires an image");
        Assert(Field<Dictionary<string,FrameworkElement>>(w,"pages")["paint"].AllowDrop,"Image drag-and-drop disappeared");
        Render(w,Path.Combine(output,"unified-fresh.png"),900,620);
        var start=Field<Button>(w,"startButton");var position=start.TranslatePoint(new Point(),(FrameworkElement)w.Content);
        Assert(position.Y>=0&&position.Y+start.ActualHeight<=620,"Painting action is hidden in a short window");
        Console.WriteLine("PASS unified-fresh");
    }
    private static void CheckUnifiedHexWindow(string output)
    {
        string d=Path.Combine(output,"unified-hex");Directory.CreateDirectory(d);var s=Settings.Defaults();s.Set("language","English");s.Set("color_mode","HEX Direct");s.Set("drawing_mode","Simple");s.Set("manual_brush_controls",true);s.Save(Path.Combine(d,"config-csharp.json"));
        var w=new MainWindow(d);SetField(w,"source",new PixelImage(8,8));Invoke(w,"UpdateReady");
        Assert(!Field<Button>(w,"startButton").IsEnabled,"Legacy Simple HEX bypasses capture");
        Assert(Field<List<Button>>(w,"allSetupButtons").All(b=>b.IsEnabled),"Missing HEX disables setup action");
        w.ShowPage("capture");Render(w,Path.Combine(output,"unified-hex.png"),1280);Console.WriteLine("PASS unified-hex");
    }
    private static void CheckUnifiedFinish()
    {
        var s=Settings.Defaults();int writes=0;
        Painter.FinishControls(s,()=>false,_=>writes++);Assert(writes==0,"Finish ignored foreground guard");
        Painter.FinishControls(s,()=>true,value=>{Assert(value==1,"Finish changed opacity target");writes++;});Assert(writes==1,"Finish stopped restoring opacity");
        Console.WriteLine("PASS unified-finish");
    }
    private static void CompleteUiTask(Func<Task> action)
    {
        var previous=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        Task task;var frame=new DispatcherFrame();var deadline=DateTime.UtcNow.AddSeconds(20);var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(10)};
        try{task=action();}catch{SynchronizationContext.SetSynchronizationContext(previous);throw;}
        timer.Tick+=(_,_)=>{if(task.IsCompleted||DateTime.UtcNow>=deadline)frame.Continue=false;};timer.Start();
        try{if(!task.IsCompleted)Dispatcher.PushFrame(frame);}finally{timer.Stop();SynchronizationContext.SetSynchronizationContext(previous);}
        Assert(task.IsCompleted,"Image import did not complete");task.GetAwaiter().GetResult();
    }
    private static void CheckUnifiedImageImport(string output)
    {
        string d=Path.Combine(output,"unified-import");Directory.CreateDirectory(d);var s=Settings.Defaults();s.Set("language","English");s.Save(Path.Combine(d,"config-csharp.json"));
        var w=new MainWindow(d);var image=new PixelImage(32,32);for(int i=0;i<1024;i++)image.Set(i,i%32<16?new(255,0,0):new(0,255,0));
        string path=Path.Combine(d,"two-colors.png");Images.Save(image,path);
        CompleteUiTask(()=>(Task)typeof(MainWindow).GetMethod("LoadImageFile",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(w,[path])!);
        Assert(Field<PixelImage>(w,"source").Width==32&&Field<Image>(w,"originalImage").Source is not null,"Image import lost the original");
        Assert(!Field<Button>(w,"startButton").IsEnabled&&Field<List<Button>>(w,"allSetupButtons").All(b=>b.IsEnabled),"Import bypassed readiness or blocked setup");
        Render(w,Path.Combine(output,"unified-import.png"),900,620);Console.WriteLine("PASS unified-image-import");
    }
}
