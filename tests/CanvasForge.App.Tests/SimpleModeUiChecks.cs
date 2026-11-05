using System.IO;
using System.Windows;
using System.Windows.Controls;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    // Simple mode: one adaptive next step, Standard-only tools, nothing removed.
    private static void CheckSimpleModeUi(string output)
    {
        var s=ReadySettings("English");s.Set("release_profile","Standard");
        var window=PaintingUiWindow(output,"simple-mode",s);s=Field<Settings>(window,"settings");Invoke(window,"UpdateReady");
        string Title()=>Field<TextBlock>(window,"nextStepTitle").Text;
        Assert(Title().StartsWith("Step 1 of 3"),"No image did not point to step 1: "+Title());
        Assert(Field<List<Button>>(window,"openImageButtons").Count>=2,"Open-image actions are not tracked for highlighting");
        SetField(window,"source",new PixelImage(8,8));Invoke(window,"UpdateReady");
        Assert(Title().StartsWith("Step 3 of 3")&&Field<Button>(window,"startButton").IsEnabled,"Ready Standard did not point to Start: "+Title());
        var cal=s.Calibration;cal.SetRect("canvas",new(0,0,0,0));s.SetCalibration(cal);Invoke(window,"UpdateReady");
        Assert(Title().StartsWith("Step 2 of 3")&&Field<TextBlock>(window,"nextStepText").Text.Contains("Interval 0.01"),"Missing Canvas did not point to Rust preparation: "+Title());
        object Accent()=>typeof(MainWindow).GetField("Accent",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public)!.GetValue(null)!;
        Assert(Field<List<Button>>(window,"allSetupButtons").All(b=>b.Background==Accent())&&Field<Button>(window,"startButton").Background!=Accent(),"Rust step did not highlight only the setup action");
        SetField(window,"preparationResume",new TaskCompletionSource<bool>());Invoke(window,"UpdateReady");
        Assert(Title()=="Preparation paused"&&Field<Button>(window,"resumeButton").IsEnabled&&Field<Button>(window,"resumeButton").Background==Accent()
            &&Field<Button>(window,"startButton").Background!=Accent(),"Paused preparation does not point to Resume: "+Title());
        SetField(window,"preparationResume",null);Invoke(window,"UpdateReady");
        Assert(Title().StartsWith("Step 2 of 3")&&Field<Button>(window,"resumeButton").Background!=Accent(),"Resume stayed highlighted after the pause ended");
        Console.WriteLine("PASS simple-mode-next-step-follows-readiness");

        var navigation=Field<Dictionary<string,Button>>(window,"navigation");
        Assert(navigation["experimental"].Visibility==Visibility.Collapsed,"Simple mode keeps the duplicate Experimental entry");
        Assert(navigation["capture"].Visibility==Visibility.Visible&&navigation["settings"].Visibility==Visibility.Visible,"Simple mode hid manual capture recovery or Settings");
        var rows=Field<List<Dictionary<SetupStage,TextBlock>>>(window,"setupRows").SelectMany(r=>r).ToArray();
        Assert(rows.Where(r=>r.Key is SetupStage.Brush or SetupStage.Spatial or SetupStage.Speed).All(r=>r.Value.Visibility==Visibility.Collapsed)
            &&rows.Where(r=>r.Key is SetupStage.Capture or SetupStage.Colors or SetupStage.Controls).All(r=>r.Value.Visibility==Visibility.Visible),"Standard setup still lists stages it never runs");
        var pages=Field<Dictionary<string,FrameworkElement>>(window,"pages");
        Assert(LogicalNodes(pages["settings"]).OfType<Button>().Any(b=>Equals(b.Content,"Experimental")&&b.Visibility==Visibility.Visible),"Settings → Experimental path is hidden");
        Assert(LogicalNodes(pages["settings"]).OfType<CheckBox>().Any(c=>Equals(c.Tag,MainWindow.AdvancedUiKey)),"Advanced mode switch is missing");
        Assert(LogicalNodes(pages["paint"]).OfType<Button>().Any(b=>Equals(b.Content,"Refresh preview")),"Plan refresh was removed from simple mode");
        Render(window,Path.Combine(output,"simple-mode-paint.png"),1280,780);
        Console.WriteLine("PASS simple-mode-hides-only-unused-tools");

        var identity=PlanIdentity.Compute(new PixelImage(8,8),s,[]);
        s.Set(MainWindow.AdvancedUiKey,true);
        Assert(PlanIdentity.Compute(new PixelImage(8,8),s,[])==identity,"UI mode changed the plan identity");
        Invoke(window,"BuildUi");Invoke(window,"UpdateReady");
        navigation=Field<Dictionary<string,Button>>(window,"navigation");
        Assert(navigation["experimental"].Visibility==Visibility.Visible,"Advanced mode did not restore all tools");
        Assert(Field<List<Dictionary<SetupStage,TextBlock>>>(window,"setupRows").SelectMany(r=>r).All(r=>r.Value.Visibility==Visibility.Visible),"Advanced mode hides setup stages");
        Console.WriteLine("PASS simple-mode-advanced-restores-without-replan");
    }
}
