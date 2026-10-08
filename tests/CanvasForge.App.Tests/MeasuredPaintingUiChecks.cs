using System.IO;
using System.Windows;
using System.Windows.Controls;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static MainWindow PaintingUiWindow(string output,string name,Settings s,Action<Window>? present=null)
    {
        var folder=Path.Combine(output,name);Directory.CreateDirectory(folder);s.Save(Path.Combine(folder,"config-csharp.json"));return new MainWindow(folder,present);
    }
    private static IEnumerable<T> Editors<T>(MainWindow w,string key) where T:FrameworkElement
        =>Field<Dictionary<string,FrameworkElement>>(w,"pages").Values.SelectMany(LogicalNodes).OfType<T>().Where(e=>Equals(e.Tag,key)).Distinct();
    private static IEnumerable<DependencyObject> LogicalNodes(DependencyObject root)
    {
        yield return root;
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())foreach(var item in LogicalNodes(child))yield return item;
    }
    private static void CheckMixedReadiness(string output,string language)
    {
        bool en=language=="English";string name="mixed-readiness-"+(en?"en":"ua");var s=ReadySettings(language);
        s.Set("precision_brush_size","3");s.Set("probe_size",3);s.Set("adaptive_brush",false);
        BrushFootprints.Save(s,new[]{3d,10,20}.Select(size=>SetupStamp(s,3,size)).ToArray());
        BrushSpan[] support=[new(0,0,1)];var samples=Enumerable.Range(1,3).Select(i=>new BrushSignalSample(i,new(53,4,80,new(140,140,140),new(87,87,87)),1,support)).ToArray();
        BrushSignalDiagnostics.Save(s,[BrushSignalDiagnostics.Summarize(s,3,1,samples,null)]);
        var w=PaintingUiWindow(output,name,s);SetField(w,"adaptiveFailure","Size 1 unavailable");Invoke(w,"UpdateReady");
        var brush=(Border)Field<System.Collections.IDictionary>(w,"workflowChips")["brush"]!;
        Assert(Captions(brush).Contains("Size 3"),"Rejected Size 1 contaminated the working Size 3 chip");
        var combo=Editors<ComboBox>(w,"probe_size").Single();combo.SelectedIndex=0;
        Assert(!Field<Button>(w,"spatialButton").IsEnabled&&Field<Button>(w,"spatialButton").ToolTip is not null,"Unmeasured Size 1 enabled a spatial probe or lost its explanation");
        combo.SelectedIndex=1;Assert(Field<Button>(w,"spatialButton").IsEnabled&&Captions(brush).Contains("Size 3"),"Probe Size changes altered the working brush chip");
        var toggle=Field<CheckBox>(w,"adaptiveEnabled");toggle.IsChecked=true;toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        var live=Field<Settings>(w,"settings");Assert(live.Bool("adaptive_brush")&&PaintTimingPlan.DefaultSize(live)==3&&AdaptiveBrush.CalibrationCurrent(live),"Verified Size 3 cannot activate mixed painting without Size 1");
        w.ShowPage("adaptive");Render(w,Path.Combine(output,name+".png"),1280,780);
        // A Size 3 route must not turn the Size 10 test chip green.
        var routeWindow=PaintingUiWindow(output,name+"-routes",ReadySettings(language));routeWindow.ShowPage("speed");Render(routeWindow,Path.Combine(output,name+"-routes.png"),900,780);
        var sizes=Editors<ComboBox>(routeWindow,"probe_size").Single();sizes.SelectedIndex=2;
        Assert(Captions(Field<Border>(routeWindow,"speedChip")).Contains(en?"Pending":"Очікує"),"A route for a different Size is presented as verified");
        sizes.SelectedIndex=1;Assert(Captions(Field<Border>(routeWindow,"speedChip")).Contains(en?"Verified":"Перевірено"),"Returning to verified Size 3 lost its route");
        Console.WriteLine("PASS "+name);
    }
    private static void CheckSynchronizedEditors(string output,string language)
    {
        bool en=language=="English";string name="synchronized-editors-"+(en?"en":"ua");var w=PaintingUiWindow(output,name,ReadySettings(language));
        SetField(w,"source",new PixelImage(8,8));Invoke(w,"UpdateReady");
        // Add a second settings surface to regress the old last-reader-wins
        // behavior, while keeping the actual production UI concise.
        var peers=new StackPanel();((Panel)((ScrollViewer)Field<Dictionary<string,FrameworkElement>>(w,"pages")["settings"]).Content).Children.Add(peers);
        Invoke(w,"AddNumber",peers,"paint_opacity_value","Opacity replica fixture",false);
        Invoke(w,"AddCheck",peers,"use_fixed_opacity","Fixed opacity replica fixture",false);
        var boxes=Editors<TextBox>(w,"paint_opacity_value").ToArray();Assert(boxes.Length>=2,"No duplicate editor fixture");
        boxes[0].Text="0.75";Assert(Field<Settings>(w,"settings").Number("paint_opacity_value")==.75&&boxes.All(b=>b.Text=="0.75"),"The last duplicate editor discarded the first editor's change");
        boxes[0].Text="invalid";Invoke(w,"RefreshAdaptiveStatus");
        Assert(Field<Settings>(w,"settings").Number("paint_opacity_value")==.75&&!Field<Button>(w,"startButton").IsEnabled,"Invalid input persisted or left START active");
        boxes[0].Text="1";Assert(boxes.All(b=>b.Text=="1")&&Field<Settings>(w,"settings").Number("paint_opacity_value")==1&&Field<Button>(w,"startButton").IsEnabled,"Correcting the first editor did not restore readiness");
        var checks=Editors<CheckBox>(w,"use_fixed_opacity").ToArray();Assert(checks.Length>=2,"No duplicate checkbox fixture");
        checks[0].IsChecked=false;checks[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert(!Field<Settings>(w,"settings").Bool("use_fixed_opacity")&&checks.All(c=>c.IsChecked==false),"Duplicate checkboxes lost or hid a preference change");
        w.ShowPage("settings");Render(w,Path.Combine(output,name+".png"),900,780);Console.WriteLine("PASS "+name);
    }
    private static void CheckColorChoice(string output,string language)
    {
        bool en=language=="English";string name="color-choice-"+(en?"en":"ua");var s=CompactHexSettings(language);
        var w=PaintingUiWindow(output,name,s);SetField(w,"source",new PixelImage(8,8));Invoke(w,"UpdateReady");
        Assert(Field<Button>(w,"startButton").IsEnabled,"HEX fixture is not ready");
        void Pick(string mode)=>LogicalNodes(Field<Dictionary<string,FrameworkElement>>(w,"pages")["paint"]).OfType<Button>().Single(b=>Equals(b.Tag,"color-choice:"+mode)).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Pick("Rust Palette");Assert(Field<Settings>(w,"settings").Mode==ColorMode.RustPalette,"Palette button did not change canonical mode");
        Assert(!Field<Button>(w,"startButton").IsEnabled,"Palette mode reused HEX readiness without its brush-tool capture");
        var live=Field<Settings>(w,"settings");var c=live.Calibration;c.SetPoint("brush_tool",new(1050,80));live.SetCalibration(c);Invoke(w,"UpdateReady");
        Assert(Field<Button>(w,"startButton").IsEnabled,"Captured Rust palette mode remains blocked");
        Render(w,Path.Combine(output,name+"-palette.png"),1280,780);
        Pick("HEX Direct");Assert(Field<Settings>(w,"settings").Mode==ColorMode.HexDirect&&Field<Button>(w,"startButton").IsEnabled,"HEX button lost its independent captured controls");
        Assert(Settings.Load(Path.Combine(output,name,"config-csharp.json")).Mode==ColorMode.HexDirect,"Mode choice was not saved");
        Render(w,Path.Combine(output,name+"-hex.png"),1280,780);Console.WriteLine("PASS "+name);
    }
    private static void CheckMeasuredPlanOverlay(string output)
    {
        Window? shown=null;var w=PaintingUiWindow(output,"measured-plan-overlay",ReadySettings("English"),dialog=>shown=dialog);
        var image=new PixelImage(8,8);for(int i=0;i<64;i++)image.Set(i,new(0,0,0));var plan=Planner.Build(image,Field<Settings>(w,"settings"));
        SetField(w,"source",image);SetField(w,"plan",plan);var mask=new bool[1_000_000];mask[20_020]=true;
        SetField(w,"previewMeasuredPlan",new MeasuredColorResult([],1_000_000,999_999,mask));Invoke(w,"UpdateReady");
        Assert(Field<Button>(w,"measuredPlanDetails").IsEnabled&&Field<TextBlock>(w,"measuredPlanStatus").Text.Contains("no commands"),"Model gaps have no visible action");
        Field<Button>(w,"measuredPlanDetails").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert(shown is not null,"Model overlay is inaccessible");var root=(FrameworkElement)shown!.Content;root.Measure(new Size(900,760));root.Arrange(new Rect(0,0,900,760));root.UpdateLayout();
        Assert(Captions(root).Any(t=>t.Contains("not an audit")),"Model overlay pretends to be a live audit");
        SetField(w,"previewMeasuredPlan",new MeasuredColorResult([],1_000_000,0,Enumerable.Repeat(true,1_000_000).ToArray()));Invoke(w,"UpdateReady");
        Assert(!Field<Button>(w,"startButton").IsEnabled,"Empty safe plan left START enabled");
        SetField(w,"previewMeasuredPlan",new MeasuredColorResult([],1_000_000,1_000_000,new bool[1_000_000]));Invoke(w,"UpdateReady");
        Assert(!Field<Button>(w,"measuredPlanDetails").IsEnabled&&Field<Button>(w,"startButton").IsEnabled,"Covered plan did not restore readiness");
        Console.WriteLine("PASS measured-plan-overlay");
    }
}
