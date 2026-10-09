using System.IO;
using System.Windows;
using System.Windows.Controls;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static Settings CompactHexSettings(string language)
    {
        var s=ReadySettings(language);s.Set("color_mode","HEX Direct");s.Set("adaptive_brush",false);s.Set("coverage_audit",false);
        s.Set("precision_brush_size","3");var c=s.Calibration;c.Data.Remove("brush_tool_x");c.Data.Remove("brush_tool_y");c.SetPoint("hex_field",new(1350,900));c.Set("hex_verified",1);
        s.SetCalibration(c);s.SetPaintCalibration(c);BrushFootprints.Save(s,new[]{1d,3d,10d,20d}.Select(size=>SetupStamp(s,3,size)).ToArray());return s;
    }
    private static void CheckCompactCapture(string output,string language)
    {
        bool en=language=="English";string name="compact-capture-"+(en?"en":"ua"),directory=Path.Combine(output,name);Directory.CreateDirectory(directory);
        var s=CompactHexSettings(language);s.Save(Path.Combine(directory,"config-csharp.json"));var w=new MainWindow(directory);
        SetField(w,"source",new PixelImage(8,8));Invoke(w,"UpdateReady");
        Assert(SetupSequence.CaptureReady(s)&&Field<Button>(w,"startButton").IsEnabled,"HEX still depends on an unused Tools capture");
        w.ShowPage("capture");Render(w,Path.Combine(output,name+".png"),en?900:1280,780);
        var page=Field<Dictionary<string,FrameworkElement>>(w,"pages")["capture"];
        var advanced=Descendants(page).OfType<Expander>().Single();Assert(!advanced.IsExpanded,"Individual capture actions are not collapsed");
        advanced.IsExpanded=true;Render(w,Path.Combine(output,name+"-expanded.png"),1280,780);
        string text=string.Join("\n",Captions(page));
        Assert(text.Contains(en?"Go to painting preparation":"До підготовки малювання")&&text.Contains(en?"Required preparation only":"Лише обов'язкова підготовка"),"Main/advanced actions are missing");
        foreach(string unused in new[]{"SAVE / CANCEL","ЗНАРЯДДЯ (3)","TOOLS (3)","TOP BAR","ВЕРХНЯ ПАНЕЛЬ","FULL UI","Палітра і прев’ю","Palette and preview"})
            Assert(!text.Contains(unused),"Unused HEX action remains: "+unused);
        var rows=Field<List<Dictionary<SetupStage,TextBlock>>>(w,"setupRows");Assert(rows.Count==1&&rows.All(r=>r.Keys.SequenceEqual(SetupSequence.Stages)),"Main setup still requires speed or spatial proof");
        if(en)Assert(!System.Text.RegularExpressions.Regex.IsMatch(text,@"[\u0400-\u04FF]"),"English compact capture is not localized");
        Console.WriteLine("PASS "+name);
    }
    private static void CheckVisualConfirmationPreset(string output,string language)
    {
        bool en=language=="English";string name="rust-compatibility-"+(en?"en":"ua"),directory=Path.Combine(output,name);Directory.CreateDirectory(directory);
        var s=CompactHexSettings(language);s.Set("fast_transfer",true);s.Set("input_engine","Experimental 1 ms");s.Save(Path.Combine(directory,"config-csharp.json"));
        var w=new MainWindow(directory);w.ShowPage("settings");Render(w,Path.Combine(output,name+"-before.png"),900,780);
        var page=Field<Dictionary<string,FrameworkElement>>(w,"pages")["settings"];
        var compatibility=LogicalNodes(page).OfType<Expander>().Single(e=>Equals(e.Tag,"rust-compatibility"));
        Assert(!compatibility.IsExpanded,"Compatibility controls clutter the default Settings page");
        Assert(Field<Settings>(w,"settings").Bool("fast_transfer")&&Field<Settings>(w,"settings").Text("input_engine")=="Experimental 1 ms","Opening Settings changed saved input preferences");
        compatibility.IsExpanded=true;Render(w,Path.Combine(output,name+"-before-expanded.png"),900,780);
        LogicalNodes(page).OfType<Button>().Single(b=>Equals(b.Tag,"confirmation-preset:Visual")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        var live=Field<Settings>(w,"settings");Assert(CalibrationReliability.Visual(live)&&!live.Bool("fast_transfer")&&live.Text("input_engine")=="Stable","Visual preset did not change the execution settings");
        Assert(CalibrationReliability.Readback(live)==new ReadbackOptions(54,3,.2)&&CalibrationReliability.StableAttempts(live)==10,"Visual preset did not raise the calibration budgets");
        Render(w,Path.Combine(output,name+".png"),900,780);
        page=Field<Dictionary<string,FrameworkElement>>(w,"pages")["settings"];
        Assert(Descendants(page).OfType<Expander>().All(e=>!e.IsExpanded),"Expert settings are expanded by default");
        LogicalNodes(page).OfType<Expander>().Single(e=>Equals(e.Tag,"rust-compatibility")).IsExpanded=true;
        Render(w,Path.Combine(output,name+".png"),900,780);var text=string.Join("\n",Captions(page));
        Assert(text.Contains(en?"An imprint alone does not prove":"Відбиток сам по собі не доводить"),"Visual-mode limitations are hidden");
        if(en)Assert(!System.Text.RegularExpressions.Regex.IsMatch(text,@"[\u0400-\u04FF]"),"Visual verification warning or budgets not localized");
        LogicalNodes(page).OfType<Button>().Single(b=>Equals(b.Tag,"confirmation-preset:Clipboard")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        live=Field<Settings>(w,"settings");Assert(!CalibrationReliability.Visual(live)&&CalibrationReliability.Readback(live)==new ReadbackOptions(24,3,.05)&&CalibrationReliability.StableAttempts(live)==5,"Numeric preset did not restore normal verification budgets");
        Console.WriteLine("PASS "+name);
    }
    private static void CheckVisualStartGate(string output,string language)
    {
        bool en=language=="English";string name="visual-start-"+(en?"en":"ua"),directory=Path.Combine(output,name);Directory.CreateDirectory(directory);
        var s=CompactHexSettings(language);s.Data.Remove("brush_footprints");CalibrationReliability.ApplyPreset(s,true);s.Save(Path.Combine(directory,"config-csharp.json"));
        var w=new MainWindow(directory);SetField(w,"source",new PixelImage(8,8));Invoke(w,"UpdateReady");
        Assert(!Field<Button>(w,"startButton").IsEnabled,"Visual controls without a measured imprint authorized START");
        var live=Field<Settings>(w,"settings");var missing=Field<Button>(w,"startButton").ToolTip.ToString()!;
        Assert(missing.Contains(en?"Measure the working brush":"виміряй робочий пензель"),"Imprint prerequisite is not explained");
        BrushFootprints.Save(live,[SetupStamp(live,3,3)]);Invoke(w,"UpdateReady");
        Assert(Field<Button>(w,"startButton").IsEnabled,"Size 1/speed proof still blocks verified Size 3 in visual mode");
        live.Set("precision_brush_size","10");Invoke(w,"UpdateReady");Assert(!Field<Button>(w,"startButton").IsEnabled,"Size 3 imprint certified unmeasured Size 10");
        if(en)Assert(!System.Text.RegularExpressions.Regex.IsMatch(missing,@"[\u0400-\u04FF]"),"Visual preflight error not localized");
        Console.WriteLine("PASS "+name);
    }
    private static void CheckQuickBrushIsolation(string output)
    {
        var w=SetupSelectionWindow(output,"quick-brush-isolation");var s=Field<Settings>(w,"settings");int measurements=0;
        SetField(w,"setupQuick",true);CalibrationReliability.ApplyPreset(s,true);s.Set("precision_brush_size","3");
        BrushFootprints.Save(s,[SetupStamp(s,3,1)]);s.Set("adaptive_brush",true);
        w.SetupBrushCalibrator=()=>{measurements++;var p=SetupStamp(s,3,3);BrushFootprints.Save(s,[p]);SetField(w,"lastBrushProfiles",new[]{p});return Task.CompletedTask;};
        CompleteUiTask(()=>SetupSelectionTask(w));Assert(measurements==1&&!s.Bool("adaptive_brush")&&PaintTimingPlan.DefaultSize(s)==3,"Quick setup enabled Adaptive from old Size 1 proof");
        w.SetupBrushCalibrator=()=>{measurements++;SetField(w,"lastBrushProfiles",Array.Empty<BrushFootprint>());return Task.CompletedTask;};
        bool failed=false;CompleteUiTask(async()=>{try{await SetupSelectionTask(w);}catch(InvalidOperationException){failed=true;}});
        Assert(failed&&measurements==2&&s.Int("brush_shape_slot")==3&&CalibrationReliability.PaintingProblem(s) is not null,"Quick setup changed brush shape or reused an older imprint after failure");
        Console.WriteLine("PASS quick-brush-isolation");
    }
}
