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

        // Adaptive edge advice: a plan with many small edge shades offers one-click merging.
        var emblem=new PixelImage(120,120);
        for(int y=0;y<120;y++)for(int x=0;x<120;x++)
        {
            int inside=0;for(int sy=0;sy<4;sy++)for(int sx=0;sx<4;sx++){double px=x+(sx+.5)/4-60,py=y+(sy+.5)/4-60,r=px*px+py*py;if(r<42*42&&r>=27*27)inside++;}
            double t=inside/16.0;emblem.Set(y*120+x,new((byte)Math.Round(51+204*t),(byte)Math.Round(133+81*t),(byte)Math.Round(255-204*t)));
        }
        var adviceSettings=Field<Settings>(window,"settings");adviceSettings.Set("edge_merge",false);
        var hexPlanSettings=adviceSettings.Clone();hexPlanSettings.Set("color_mode","HEX Direct");hexPlanSettings.Set("hex_max_colors","Auto");
        SetField(window,"plan",Planner.Build(emblem,hexPlanSettings));Invoke(window,"UpdateReady");
        var merge=Field<Button>(window,"nextStepMerge");
        Assert(PlanAdvice.SuggestEdgeMerge(Field<PaintPlan>(window,"plan"),adviceSettings),"Fixture has no small edge shades");
        Assert(merge.Visibility==Visibility.Visible&&Field<TextBlock>(window,"nextStepText").Text.Contains("Merge edges"),"Edge advice is not offered in the Next step card");
        Assert(LogicalNodes(pages["paint"]).OfType<Expander>().Any(e=>Equals(e.Header,"Plan optimization")&&e.Visibility==Visibility.Visible),"Plan optimization is hidden in simple mode");
        // No plan stays in the window: a rebuild would otherwise start an asynchronous render.
        SetField(window,"plan",null);
        Invoke(window,"EnableEdgeMerge");
        Assert(Field<Settings>(window,"settings").Bool("edge_merge"),"Merge edges did not enable edge merging");
        Field<Settings>(window,"settings").Set("edge_merge",false);Invoke(window,"UpdateReady");
        Assert(Field<Button>(window,"nextStepMerge").Visibility==Visibility.Collapsed,"Edge advice stays without a plan");
        Console.WriteLine("PASS simple-mode-edge-advice");

        s=Field<Settings>(window,"settings");// EnableEdgeMerge rebuilt the window settings.
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
