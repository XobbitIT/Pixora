using System.IO;
using System.Windows;
using System.Windows.Controls;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static async Task Cancelled(Task task)
    {bool cancelled=false;try{await task;}catch(OperationCanceledException){cancelled=true;}Assert(cancelled,"Superseded preview was not cancelled");}
    private static void CheckPreviewQueueLatest()
    {
        CompleteUiTask(async()=>{
            var queue=new PreviewCalculationQueue();var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken firstToken=default;int queued=0,latest=0;
            var first=queue.RunAsync(async token=>{firstToken=token;await release.Task;});
            var second=queue.RunAsync(token=>{queued++;return Task.CompletedTask;});
            var third=queue.RunAsync(token=>{latest++;return Task.CompletedTask;});
            Assert(firstToken.IsCancellationRequested&&queued==0&&latest==0&&!queue.WhenIdle.IsCompleted,"Preview jobs overlapped or drain completed too early");
            release.SetResult();await Cancelled(first);await Cancelled(second);await third;await queue.WhenIdle;
            Assert(queued==0&&latest==1,"A stale queued preview ran");
        });Console.WriteLine("PASS preview-queue-latest-and-single-worker");
    }
    private static void CheckPreviewQueueCancellation()
    {
        CompleteUiTask(async()=>{
            var queue=new PreviewCalculationQueue();CancellationToken active=default;
            var pending=queue.RunAsync(async token=>{active=token;await Task.Delay(Timeout.Infinite,token);});
            queue.Cancel();await Cancelled(pending);await queue.WhenIdle;
            Assert(active.IsCancellationRequested,"Explicit cancellation did not reach the worker");
            bool failed=false;try{await queue.RunAsync(_=>throw new InvalidOperationException("fixture"));}catch(InvalidOperationException){failed=true;}
            int recovered=0;await queue.RunAsync(_=>{recovered++;return Task.CompletedTask;});
            Assert(failed&&recovered==1&&queue.WhenIdle.IsCompleted,"Worker failure leaked the gate");
        });Console.WriteLine("PASS preview-queue-cancellation-and-failure-recovery");
    }
    private static void CheckPreviewWorkerCancellation(string output)
    {
        var w=LifecycleWindow(output,"preview-token-flow");var s=Field<Settings>(w,"settings");
        var image=new PixelImage(8,8);var plan=Planner.Build(image,s);SetField(w,"source",image);SetField(w,"plan",plan);
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);CancellationToken active=default;
        w.MeasuredPlanner=(_,_,token)=>{active=token;entered.TrySetResult();token.WaitHandle.WaitOne();token.ThrowIfCancellationRequested();return null;};
        CompleteUiTask(async()=>{
            Invoke(w,"RenderPlan");await entered.Task;
            Assert(Field<bool>(w,"measuredPlanning")&&!Field<Button>(w,"startButton").IsEnabled,"START allowed a pending safe plan");
            Assert(Field<TextBlock>(w,"measuredPlanStatus").Text.Contains("Calculating"),"Pending model state was hidden");
            Invoke(w,"CancelActiveWork");await Field<PreviewCalculationQueue>(w,"previewWork").WhenIdle;
            Assert(active.IsCancellationRequested&&Field<object?>(w,"resumeSchedule") is null,"STOP did not discard preview work");
        });Console.WriteLine("PASS preview-worker-token-and-start-gate");
    }
    private static void CheckPreviewModelFailure(string output)
    {
        var w=LifecycleWindow(output,"preview-model-failure");var image=new PixelImage(8,8);SetField(w,"source",image);
        SetField(w,"plan",Planner.Build(image,Field<Settings>(w,"settings")));
        w.MeasuredPlanner=(_,_,_)=>throw new InvalidOperationException("fixture geometry failure");
        CompleteUiTask(async()=>{Invoke(w,"RenderPlan");await Field<PreviewCalculationQueue>(w,"previewWork").WhenIdle;await Task.Yield();});
        Assert(Field<bool>(w,"measuredPlanFailed")&&!Field<Button>(w,"startButton").IsEnabled,"Geometry failure did not block START");
        Assert(Field<Image>(w,"previewImage").Source is not null&&Field<TextBlock>(w,"measuredPlanStatus").Text.Contains("failed"),"Failure erased the image or hid its cause");
        Console.WriteLine("PASS preview-model-failure-preserves-image");
    }
    private static void CheckSupersededPreviewPublication(string output)
    {
        var w=LifecycleWindow(output,"preview-stale-publication");var firstImage=new PixelImage(8,8);var secondImage=new PixelImage(16,12);
        var s=Field<Settings>(w,"settings");var first=Planner.Build(firstImage,s);var second=Planner.Build(secondImage,s);
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);using var release=new ManualResetEventSlim();int calls=0;
        w.MeasuredPlanner=(_,_,_)=>{if(Interlocked.Increment(ref calls)==1){entered.TrySetResult();release.Wait();}return null;};
        CompleteUiTask(async()=>{
            SetField(w,"source",firstImage);SetField(w,"plan",first);Invoke(w,"RenderPlan");await entered.Task;
            SetField(w,"source",secondImage);SetField(w,"plan",second);Invoke(w,"RenderPlan");
            release.Set();await Field<PreviewCalculationQueue>(w,"previewWork").WhenIdle;await Task.Yield();
            var schedule=Field<(PaintPlan Plan,int[] GroupCounts)?>(w,"resumeSchedule");
            Assert(calls==2&&schedule is not null&&ReferenceEquals(schedule.Value.Plan,second),"A late stale calculation replaced the latest resume plan");
            Assert(ReferenceEquals(Field<PaintPlan>(w,"plan"),second)&&!Field<bool>(w,"measuredPlanning"),"Latest preview never completed");
        });Console.WriteLine("PASS preview-stale-result-cannot-replace-latest");
    }
    private static void CheckPreparationShortcuts(string output,string language)
    {
        string name="universal-painting-"+(language=="English"?"en":"ua");var settings=ReadySettings(language);settings.Data.Remove("brush_footprints");CalibrationReliability.ApplyPreset(settings,true);
        var w=PaintingUiWindow(output,name,settings);var pages=Field<Dictionary<string,FrameworkElement>>(w,"pages");
        Assert(!LogicalNodes(pages["paint"]).OfType<Button>().Any(b=>b.Tag?.ToString()?.Contains("preset:")==true),"Compatibility presets remain on Painting");
        var labels=pages.Values.SelectMany(LogicalNodes).Select(node=>node switch {TextBlock t=>t.Text,ContentControl c=>c.Content as string,_=>null}).Where(text=>text is not null);
        Assert(!System.Text.RegularExpressions.Regex.IsMatch(string.Join("\n",labels),"(?i)ноутбук|laptop|Звичайний ПК|Standard PC"),"Hardware-specific labels remain in the interface");
        Assert(Field<Button>(w,"controlDiagnosisButton").Visibility==Visibility.Collapsed,"An empty control-diagnostics action clutters preparation");
        var s=Field<Settings>(w,"settings");Assert(CalibrationReliability.Visual(s)&&s.Text("input_engine")=="Stable"&&!s.Bool("fast_transfer"),"Opening Painting changed saved confirmation preferences");
        Assert(Field<TextBlock>(w,"ready").Text==(language=="English"?"Open an image.":"Відкрий зображення."),"First-use guidance asks for calibration before an image");
        SetField(w,"source",new PixelImage(8,8));Invoke(w,"UpdateReady");
        Assert(!Field<Button>(w,"startButton").IsEnabled&&Field<TextBlock>(w,"ready").Text.Contains(language=="English"?"Measure the working brush":"виміряй робочий пензель"),"Ordered guidance weakened the visual imprint gate");
        SetField(w,"source",null!);Invoke(w,"UpdateReady");
        var chips=Field<object>(w,"workflowChips");var brush=(Border)chips.GetType().GetProperty("Item")!.GetValue(chips,["brush"])!;
        Assert(((TextBlock)brush.Child).Text==(language=="English"?"Pending":"Очікує")&&brush.ToolTip is not null,"Visual setup incorrectly retained a ready legacy brush chip");
        Assert(Field<string>(w,"currentPage")=="paint"&&Field<List<Dictionary<SetupStage,TextBlock>>>(w,"setupRows").Count==1,"The canonical preparation stepper is missing from Painting");
        var actions=LogicalNodes((DependencyObject)w.Content).OfType<Button>().Where(b=>Equals(b.Tag,"workflow-action:brush")).ToArray();
        Assert(actions.Length==1,"Brush readiness has no navigation action");actions[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert(Field<string>(w,"currentPage")=="adaptive","Brush chip opened the wrong section");
        w.ShowPage("paint");Render(w,Path.Combine(output,name+".png"),language=="English"?900:1280,780);
        Console.WriteLine("PASS "+name);
    }
    private static void CheckControlDiagnosisAction(string output,string language)
    {
        Window? shown=null;string name="control-diagnosis-action-"+(language=="English"?"en":"ua");
        var w=PaintingUiWindow(output,name,ReadySettings(language),dialog=>shown=dialog);
        string folder=Field<string>(w,"folder");var image=new PixelImage(360,80);
        for(int y=25;y<55;y++)for(int x=20;x<350;x++)image.Set(y*360+x,new(79,88,53));
        SliderDiagnostics.Save(folder,"size",image,new(100,100,460,180),new(20,25,270,55),RustSlider.Diagnose(image,new(20,25,270,55)));
        Invoke(w,"UpdateReady");var button=Field<Button>(w,"controlDiagnosisButton");Assert(button.IsEnabled&&button.Visibility==Visibility.Visible,"Saved control failure has no visible action");
        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert(shown is not null&&LogicalNodes((DependencyObject)shown!.Content).OfType<Image>().Single().Source is not null,"Control action did not show the saved crop");
        var root=(FrameworkElement)shown!.Content;root.Measure(new Size(800,600));root.Arrange(new Rect(0,0,800,600));root.UpdateLayout();
        Assert(Captions(root).Any(text=>text.Contains("10–40%")),"Control rejection criterion is missing");
        Console.WriteLine("PASS "+name);
    }
}
