using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void CheckWorkingBrush(string output,string language)
    {
        bool en=language=="English";string directory=Path.Combine(output,"working-brush-"+(en?"en":"ua"));Directory.CreateDirectory(directory);
        var s=ReadySettings(language);s.Set("adaptive_brush",false);s.Set("precision_brush_size","Profile");s.Set("speed_profile","Rapid");
        s.Set("calibrated_strokes",true);s.Set("coverage_audit",false);BrushFootprints.Save(s,[SetupStamp(s,3,3)]);s.Save(Path.Combine(directory,"config-csharp.json"));
        var w=new MainWindow(directory);SetField(w,"source",new PixelImage(8,8));Invoke(w,"UpdateReady");
        Assert(Field<Button>(w,"startButton").IsEnabled,"Unavailable Size 1/speed blocked normal START");
        var use=Field<Button>(w,"useMeasuredBrush");Assert(use.Visibility==Visibility.Visible&&use.Content.ToString()!.Contains("Size 3"),"No single-click recovery to measured Size 3");
        use.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        var live=Field<Settings>(w,"settings");Assert(PaintTimingPlan.DefaultSize(live)==3&&!AdaptiveBrush.CalibrationCurrent(live),"Recovery reused unverified Size 1");
        Assert(Field<TextBlock>(w,"workingBrushSummary").Text.Contains(en?"footprint verified":"слід підтверджений"),"Actual working Size is not explained");
        var worker=(Painter)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Painter));
        typeof(Painter).GetField("settings",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(worker,live);
        var controls=((double Size,double Interval,double Opacity))typeof(Painter).GetMethod("DesiredControls",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(worker,[null])!;
        Assert(controls.Size==3&&controls.Interval==.01&&controls.Opacity==1,"Executor did not use selected working Size");
        Render(w,Path.Combine(output,"working-brush-"+(en?"en":"ua")+".png"),1280,780);
        Console.WriteLine("PASS working-brush-"+(en?"en":"ua"));
    }
    private static void CheckStartButtonDispatch(string output)
    {
        var window=LifecycleWindow(output,"start-button-dispatch");SetField(window,"source",new PixelImage(8,8));Invoke(window,"UpdateReady");
        var gate=new TaskCompletionSource<PaintPlan>(TaskCreationOptions.RunContinuationsAsynchronously);
        window.PlanBuilder=(_,_,_,_)=>gate.Task;
        CompleteUiTask(async()=>
        {
            Field<Button>(window,"startButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert(Field<bool>(window,"startPreparing")&&Field<Button>(window,"stopButton").IsEnabled,"START click was ignored");
            Assert(Field<Button>(window,"startButton").Content.ToString()=="Preparing painting…","START preparation is invisible");
            var done=Field<TaskCompletionSource>(window,"startDone");Invoke(window,"CancelActiveWork");
            gate.SetResult(Planner.Build(Field<PixelImage>(window,"source"),Field<Settings>(window,"settings")));await done.Task;
        });
        var line=File.ReadAllLines(Path.Combine(output,"start-button-dispatch","session-csharp.jsonl")).Last();using var log=JsonDocument.Parse(line);
        Assert(log.RootElement.GetProperty("action").GetString()=="start_requested"&&log.RootElement.GetProperty("details").GetProperty("accepted").GetBoolean(),"START has no early diagnostic record");
        Assert(!Field<bool>(window,"startPreparing")&&Field<object?>(window,"painter") is null,"Cancelled START reached native input");
        Console.WriteLine("PASS start-button-dispatch");
    }
}
