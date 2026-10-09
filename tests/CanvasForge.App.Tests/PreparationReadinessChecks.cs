using System.IO;
using System.Windows;
using System.Windows.Controls;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void CheckCanonicalPreparation(string output,string language)
    {
        bool en=language=="English";var directory=Path.Combine(output,"canonical-preparation-"+(en?"en":"ua"));Directory.CreateDirectory(directory);
        var s=ReadySettings(language);s.Set("precision_brush_size","3");s.Data.Remove("brush_footprints");s.Set("control_confirmation","Clipboard");s.Save(Path.Combine(directory,"config-csharp.json"));
        var w=new MainWindow(directory);SetField(w,"source",new PixelImage(32,32));Invoke(w,"UpdateReady");
        Assert(!Field<Button>(w,"startButton").IsEnabled&&Field<TextBlock>(w,"ready").Text.Contains(en?"Brush is unverified":"Пензель не підтверджено"),"Clipboard path still permits an unmeasured brush");
        Assert(!Field<TextBlock>(w,"ready").Text.Contains(en?"Everything is ready":"Усе готово"),"Ready header contradicts brush prerequisite");
        var rows=Field<List<Dictionary<SetupStage,TextBlock>>>(w,"setupRows");
        Assert(rows.Count==1&&rows[0].Keys.SequenceEqual(SetupSequence.Stages)&&!rows[0][SetupStage.Brush].Text.Contains(en?"passed":"готово"),"Stepper contradicts START or omits optional stages");
        Assert(Field<List<Button>>(w,"allSetupButtons").Count==1,"Main preparation is duplicated on another page");
        var live=Field<Settings>(w,"settings");BrushFootprints.Save(live,[SetupStamp(live,3,3)]);Invoke(w,"UpdateReady");
        SetField(w,"setupOutcome","old brush failure");Invoke(w,"RefreshSetupStatus");
        Assert(Field<List<TextBlock>>(w,"setupSummaries").All(t=>!t.Text.Contains("old brush failure")),"Manual brush correction retained the contradictory failure summary");
        Assert(Field<Button>(w,"startButton").IsEnabled&&rows[0][SetupStage.Brush].Text.Contains(en?"passed":"готово"),"Measured Size 3 is blocked by optional speed/Size 1");
        live.Set("palette_target_failed",true);Invoke(w,"UpdateReady");Assert(!Field<Button>(w,"startButton").IsEnabled,"Failed palette validation still permits START");
        live.Set("palette_target_failed",false);live.Set("controls_validation_failed",true);Invoke(w,"UpdateReady");Assert(!Field<Button>(w,"startButton").IsEnabled,"Failed controls still permit START");
        live.Set("controls_validation_failed",false);live.Set("precision_brush_size","10");Invoke(w,"UpdateReady");Assert(!Field<Button>(w,"startButton").IsEnabled,"Size 3 evidence authorizes Size 10");
        Render(w,Path.Combine(output,"canonical-preparation-"+(en?"en":"ua")+".png"),1280,780,expandPaintingControls:false);
        Console.WriteLine("PASS canonical-preparation-"+language);
    }
    private static void CheckPreparationHistory(string output)
    {
        var directory=Path.Combine(output,"preparation-history");Directory.CreateDirectory(directory);
        var s=ReadySettings("English");s.Save(Path.Combine(directory,"config-csharp.json"));var w=new MainWindow(directory);
        var results=Field<Dictionary<SetupStage,(SetupStageState State,string Detail)>>(w,"setupResults");
        results[SetupStage.Speed]=(SetupStageState.Failed,"No verified route");Invoke(w,"SaveSetupHistory");Field<Settings>(w,"settings").Save(Path.Combine(directory,"config-csharp.json"));
        var reopened=new MainWindow(directory);Assert(Field<Dictionary<SetupStage,(SetupStageState State,string Detail)>>(reopened,"setupResults")[SetupStage.Speed].State==SetupStageState.Failed,"Restart discarded optional-stage results");
        var changed=Field<Settings>(reopened,"settings");changed.Set("precision_brush_size","10");changed.Save(Path.Combine(directory,"config-csharp.json"));
        var stale=new MainWindow(directory);Assert(Field<Dictionary<SetupStage,(SetupStageState State,string Detail)>>(stale,"setupResults").Count==0,"Different brush reused preparation history");
        var invalid=Field<Settings>(stale,"settings");invalid.Data["preparation_history"]=System.Text.Json.Nodes.JsonNode.Parse("{\"context\":\"\",\"stages\":{\"Brush\":null}}");
        invalid.Data["preparation_history"]!["context"]=SpeedCalibration.Context(invalid)+":"+PaintTimingPlan.DefaultSize(invalid);
        Invoke(stale,"RestoreSetupHistory");Assert(Field<Dictionary<SetupStage,(SetupStageState State,string Detail)>>(stale,"setupResults").Count==0,"Null saved stage became valid preparation proof");
        Console.WriteLine("PASS preparation-history-bound-to-current-proof");
    }
    private static void CheckPreparationControlsAndFooter(string output)
    {
        var directory=Path.Combine(output,"preparation-controls");Directory.CreateDirectory(directory);ReadySettings("English").Save(Path.Combine(directory,"config-csharp.json"));var w=new MainWindow(directory);
        var page=Field<Dictionary<string,FrameworkElement>>(w,"pages")["paint"];
        var colors=LogicalNodes(page).OfType<Expander>().Single(e=>Equals(e.Tag,"painting-colors"));
        var quality=LogicalNodes(page).OfType<Expander>().Single(e=>Equals(e.Tag,"painting-quality"));
        Assert(!colors.IsExpanded&&!quality.IsExpanded,"Advanced color/speed controls clutter preparation");
        colors.IsExpanded=true;
        var count=LogicalNodes(page).OfType<ComboBox>().Single(c=>Equals(c.Tag,"max_colors"));
        Assert(count.Items.Contains("3")&&count.Items.Contains("4")&&count.Items.Contains("8"),"Logo color limits are missing");
        Invoke(w,"ApplyPaintProgress",new PaintProgress(50,100,10,12,"Painting"));w.ShowPage("settings");
        Assert(Field<TextBlock>(w,"footerProgress").Text==Field<TextBlock>(w,"progressLabel").Text&&Field<TextBlock>(w,"footerProgress").Text.Contains("ETA")&&Field<ProgressBar>(w,"footerProgressBar").Value==50&&Field<ProgressBar>(w,"footerProgressBar").Visibility==Visibility.Visible,"Progress disappears outside Painting page");
        Render(w,Path.Combine(output,"persistent-progress.png"),1280,780,expandPaintingControls:false);
        Console.WriteLine("PASS preparation-collapsed-controls-and-persistent-progress");
    }
}
