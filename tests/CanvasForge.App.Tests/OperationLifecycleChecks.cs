using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static MainWindow LifecycleWindow(string output,string name)
    {
        string directory=Path.Combine(output,name);Directory.CreateDirectory(directory);
        var settings=ReadySettings("English");settings.Set("adaptive_brush",false);
        settings.Save(Path.Combine(directory,"config-csharp.json"));
        return new MainWindow(directory);
    }
    private static Task LifecycleTask(MainWindow window,string method,params object[] args)=>(Task)Invoke(window,method,args)!;
    private static TaskCompletionSource<PixelImage> ImageGate()=>new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void CheckLatestImageWins(string output)
    {
        var window=LifecycleWindow(output,"lifecycle-latest-image");var first=ImageGate();var second=ImageGate();
        window.ImageLoader=path=>path=="first.png"?first.Task:second.Task;
        CompleteUiTask(async()=>
        {
            var a=LifecycleTask(window,"LoadImageFile","first.png");var b=LifecycleTask(window,"LoadImageFile","second.png");
            Assert(!Field<Button>(window,"startButton").IsEnabled,"START remained enabled during image load");
            second.SetResult(new PixelImage(24,12));await b;
            first.SetResult(new PixelImage(8,8));await a;
        });
        Assert(Field<string>(window,"imagePath")=="second.png"&&Field<PixelImage>(window,"source").Width==24,"Old image load replaced the latest image");
        Assert(Field<PaintPlan>(window,"plan").Identity==PlanIdentity.Compute(Field<PixelImage>(window,"source"),Field<Settings>(window,"settings"),Field<PaintPlan>(window,"plan").Palette),"Plan belongs to an obsolete image");
        Console.WriteLine("PASS lifecycle-latest-image");
    }
    private static void CheckCancelledImageDiscarded(string output)
    {
        var window=LifecycleWindow(output,"lifecycle-cancel-image");var gate=ImageGate();var original=new PixelImage(12,12);
        SetField(window,"source",original);SetField(window,"imagePath","original.png");window.ImageLoader=_=>gate.Task;
        CompleteUiTask(async()=>
        {
            var pending=LifecycleTask(window,"LoadImageFile","cancelled.png");
            Assert(Field<bool>(window,"imageLoading"),"Loading was not tracked");Invoke(window,"CancelActiveWork");
            gate.SetResult(new PixelImage(32,32));await pending;
        });
        Assert(ReferenceEquals(Field<PixelImage>(window,"source"),original)&&Field<string>(window,"imagePath")=="original.png","Cancelled decoder changed source");
        Assert(!Field<bool>(window,"imageLoading"),"Cancelled image left the interface locked");
        Console.WriteLine("PASS lifecycle-cancel-image");
    }
    private static void CheckStartPreparationExclusive(string output)
    {
        var window=LifecycleWindow(output,"lifecycle-start-preparation");var source=new PixelImage(8,8);SetField(window,"source",source);
        var gate=new TaskCompletionSource<PaintPlan>(TaskCreationOptions.RunContinuationsAsynchronously);int builds=0;
        CancellationToken token=default;window.PlanBuilder=(image,settings,progress,cancel)=>{builds++;token=cancel;return gate.Task;};
        CompleteUiTask(async()=>
        {
            var start=LifecycleTask(window,"Start",false);Assert(Field<bool>(window,"startPreparing"),"START did not lock during planning");
            await LifecycleTask(window,"Start",false);await LifecycleTask(window,"Start",true);
            Assert(builds==1,"Repeated START/RESUME created another preparation");
            Field<Button>(window,"stopButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert(token.IsCancellationRequested,"STOP did not cancel plan preparation");
            gate.SetResult(Planner.Build(source,Field<Settings>(window,"settings")));await start;
        });
        Assert(!Field<bool>(window,"startPreparing")&&Invoke(window,"get_Painting") is false,"Cancelled preparation left active work");
        Assert(Field<object?>(window,"plan") is null&&Field<object?>(window,"painter") is null,"Cancelled plan reached native input");
        Console.WriteLine("PASS lifecycle-start-preparation");
    }
    private static void CheckFailedPlanClearsStaleResult(string output)
    {
        var window=LifecycleWindow(output,"lifecycle-stale-plan");var source=new PixelImage(8,8);SetField(window,"source",source);
        var settings=Field<Settings>(window,"settings");SetField(window,"plan",Planner.Build(source,settings));settings.SetPalette([]);
        bool failed=false;CompleteUiTask(async()=>
        {
            try{await LifecycleTask(window,"Start",false);}catch(InvalidOperationException e){failed=e.Message=="Cannot build plan.";}
        });
        Assert(failed&&Field<object?>(window,"plan") is null,"Failed build retained an executable stale plan");
        Assert(!Field<bool>(window,"startPreparing")&&Field<object?>(window,"painter") is null,"Failed START leaked resources");
        Assert(Field<Dictionary<string,FrameworkElement>>(window,"pages")["capture"].IsEnabled,"Failed START kept editing disabled");
        Console.WriteLine("PASS lifecycle-stale-plan");
    }
    private static void CheckInputCheckExclusive(string output)
    {
        var window=LifecycleWindow(output,"lifecycle-exclusive-check");var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int actions=0;
        CompleteUiTask(async()=>
        {
            var active=LifecycleTask(window,"RunInputCheck",(Func<Task>)(async()=>{actions++;await release.Task;}));
            Assert(Field<bool>(window,"inputCheckRunning")&&Field<Button>(window,"stopButton").IsEnabled,"Standalone check has no active STOP");
            Assert(Field<Button>(window,"setupStop").Visibility==Visibility.Visible,"Standalone check has no Stop action outside the painting page");
            Assert(!Field<Dictionary<string,FrameworkElement>>(window,"pages")["capture"].IsEnabled,"Standalone check allows capture edits");
            await LifecycleTask(window,"RunInputCheck",(Func<Task>)(()=>{actions++;return Task.CompletedTask;}));
            await LifecycleTask(window,"RunAutomaticSetup");Assert(actions==1&&!Field<bool>(window,"setupRunning"),"Another input operation overlapped the check");
            release.SetResult();await active;
        });
        Assert(!Field<bool>(window,"inputCheckRunning")&&Field<Dictionary<string,FrameworkElement>>(window,"pages")["capture"].IsEnabled,"Completed check kept editing disabled");
        Console.WriteLine("PASS lifecycle-exclusive-check");
    }
    private static void CheckStandaloneStop(string output)
    {
        var window=LifecycleWindow(output,"lifecycle-stop-check");CancellationToken token=default;
        CompleteUiTask(async()=>
        {
            var active=LifecycleTask(window,"RunInputCheck",(Func<Task>)(()=>
            {token=(CancellationToken)typeof(MainWindow).GetProperty("SetupToken",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;return Task.Delay(Timeout.Infinite,token);}));
            Field<Button>(window,"stopButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));await active;
        });
        Assert(token.IsCancellationRequested&&!Field<bool>(window,"inputCheckRunning"),"Standalone STOP did not finish the check");
        Assert(Field<object?>(window,"inputCheckCancel") is null&&Field<object?>(window,"inputCheckDone") is null,"Check leaked cancellation/completion state");
        Console.WriteLine("PASS lifecycle-stop-check");
    }
    private static void CheckClosingWaitsForCheck(string output)
    {
        var window=LifecycleWindow(output,"lifecycle-close-check");var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);CancellationToken token=default;
        CompleteUiTask(async()=>
        {
            var active=LifecycleTask(window,"RunInputCheck",(Func<Task>)(async()=>
            {token=(CancellationToken)typeof(MainWindow).GetProperty("SetupToken",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;await release.Task;token.ThrowIfCancellationRequested();}));
            var closing=new CancelEventArgs();typeof(MainWindow).GetMethod("OnClosing",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)!.Invoke(window,[window,closing]);
            Assert(closing.Cancel&&token.IsCancellationRequested&&!active.IsCompleted,"Close did not wait for active check cleanup");
            release.SetResult();await active;
        });
        Assert(Field<bool>(window,"closing")&&!Field<bool>(window,"inputCheckRunning"),"Close retained active check state");
        Invoke(window,"Error",new InvalidOperationException("Late worker failure"));
        Console.WriteLine("PASS lifecycle-close-check");
    }
    private static void CheckCaptureCanRebuildPlan(string output)
    {
        var window=LifecycleWindow(output,"lifecycle-capture-plan");SetField(window,"source",new PixelImage(8,8));
        CompleteUiTask(()=>LifecycleTask(window,"RunInputCheck",(Func<Task>)(()=>LifecycleTask(window,"BuildPlanCore",true))));
        Assert(Field<object?>(window,"plan") is PaintPlan&&!Field<bool>(window,"inputCheckRunning"),"Capture cannot rebuild its preview within the exclusive operation");
        Console.WriteLine("PASS lifecycle-capture-plan");
    }
    private static void CheckMalformedLegacyWindow(string output)
    {
        var window=LifecycleWindow(output,"lifecycle-malformed-calibration");var settings=Field<Settings>(window,"settings");
        settings.Data.Remove("brush_footprints");
        settings.Data["brush_calibration_points"]=System.Text.Json.Nodes.JsonNode.Parse("[[1,null,1],[3,{},1],[10,21,null],[20,9,99]]");
        Invoke(window,"BuildUi");window.ShowPage("adaptive");Invoke(window,"UpdateReady");
        Assert(!AdaptiveBrush.CalibrationCurrent(settings)&&!SpeedCalibration.BrushReady(settings,1),"Malformed legacy calibration became proof");
        Render(window,Path.Combine(output,"lifecycle-malformed-calibration.png"),900,620);
        Console.WriteLine("PASS lifecycle-malformed-calibration");
    }
}
