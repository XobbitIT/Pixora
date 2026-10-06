using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void CheckUnmeasuredAdaptiveClick(MainWindow window)
    {
        var toggle=Field<CheckBox>(window,"adaptiveEnabled");
        Assert(toggle.IsEnabled,"Unavailable adaptive has no explanation action");
        toggle.IsChecked=true;toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert(!Field<Settings>(window,"settings").Bool("adaptive_brush")&&toggle.IsChecked==false,"Missing Size 1 allowed adaptive execution");
    }
    private static Settings SimpleCaptured(string language, bool hex=false)
    {
        var s=Settings.Defaults();s.Set("language",language);s.Set("color_mode",hex?"HEX Direct":"Rust Palette");
        var c=s.Calibration;c.SetRect("canvas",new(10,10,74,74));c.SetSession(new(0,0),96,new(160,120));
        c.SetRect("palette",new(80,10,120,74));if(hex)c.SetPoint("hex_field",new(130,90));s.SetCalibration(c);
        s.SetPalette([new(new(0,0,0),new(85,15),"main")]);return s;
    }
    private static void CheckSimpleWorkflow(string output,string language,int width)
    {
        bool en=language=="English";string name="simple-"+(en?"en":"ua"),directory=Path.Combine(output,name);Directory.CreateDirectory(directory);
        var s=SimpleCaptured(language);s.Set("adaptive_brush",true);s.Set("coverage_audit",true);s.Set("calibrated_strokes",true);s.Save(Path.Combine(directory,"config-csharp.json"));
        var w=new MainWindow(directory);SetField(w,"source",new PixelImage(8,8));Invoke(w,"UpdateReady");Render(w,Path.Combine(output,name+".png"),width,620);
        Assert(Field<Button>(w,"startButton").IsEnabled,"Old advanced failures block simple START");
        CheckSimpleStartVisible(w, width, 620);
        var pages=Field<Dictionary<string,FrameworkElement>>(w,"pages");Assert(pages.Count==2&&pages.ContainsKey("paint")&&pages.ContainsKey("capture"),"Simple mode exposes calibration navigation");
        Assert(!Field<Dictionary<string,Func<object>>>(w,"readers").ContainsKey("coverage_audit"),"Hidden audit controls leak into simple form");
        if(en)Assert(!System.Text.RegularExpressions.Regex.IsMatch(string.Join("\n",Captions(pages["paint"])),@"[\u0400-\u04FF]"),"Simple screen is not translated");
        Field<Button>(w,"modeButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert(Field<Dictionary<string,FrameworkElement>>(w,"pages").ContainsKey("adaptive"),"Advanced brush page disappeared");
        Assert(Field<Settings>(w,"settings").Bool("coverage_audit"),"Mode change erased advanced preferences");
        w.ShowPage("adaptive");Render(w,Path.Combine(output,name+"-advanced.png"),width,620);
        var live=Field<Settings>(w,"settings");live.Set("adaptive_brush",false);Invoke(w,"BuildUi");
        var adaptive=Field<CheckBox>(w,"adaptiveEnabled");adaptive.IsChecked=true;adaptive.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert(!Field<Settings>(w,"settings").Bool("adaptive_brush"),"Unmeasured adaptive brush was enabled");
        Assert(Field<TextBlock>(w,"status").Text.Contains(en?"measured Size 1":"вимірювання Size 1"),"Unmeasured brush click has no explanation");
        Field<Button>(w,"modeButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert(Field<Button>(w,"startButton").IsEnabled,"Returning to simple mode lost start readiness");
        Console.WriteLine("PASS "+name);
    }
    private static void CheckSimpleFreshWindow(string output)
    {
        string directory=Path.Combine(output,"simple-fresh");Directory.CreateDirectory(directory);var w=new MainWindow(directory);
        Assert(Field<Dictionary<string,FrameworkElement>>(w,"pages").Count==2,"Fresh default is not simple");
        Assert(!Field<Button>(w,"startButton").IsEnabled&&Field<Button>(w,"simpleSetupButton").IsEnabled,"Fresh setup is inaccessible");
        Render(w,Path.Combine(output,"simple-fresh.png"),900,620);Console.WriteLine("PASS simple-fresh");
        CheckSimpleStartVisible(w, 900, 620);
    }
    private static void CheckSimpleHexWindow(string output)
    {
        string directory=Path.Combine(output,"simple-hex");Directory.CreateDirectory(directory);SimpleCaptured("English",true).Save(Path.Combine(directory,"config-csharp.json"));
        var w=new MainWindow(directory);SetField(w,"source",new PixelImage(8,8));Invoke(w,"UpdateReady");
        Assert(Field<Button>(w,"startButton").IsEnabled,"Simple HEX demands advanced controls");
        Render(w,Path.Combine(output,"simple-hex.png"),1060,620);Console.WriteLine("PASS simple-hex");
    }
    private static void CheckSimplePainter(string output)
    {
        var painter=(Painter)RuntimeHelpers.GetUninitializedObject(typeof(Painter));
        typeof(Painter).GetField("settings",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(painter,DrawingWorkflow.Effective(SimpleCaptured("English",true)));
        typeof(Painter).GetField("logPath",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(painter,Path.Combine(output,"simple-painter.jsonl"));
        // Zero native window: any attempt to click/verify an uncaptured control fails.
        painter.ApplyControls();
        typeof(Painter).GetMethod("ReprimeControls",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(painter,[null,null]);
        Assert((int)typeof(Painter).GetField("activeShape",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(painter)! == 4,"Manual brush not primed");
        Console.WriteLine("PASS simple-painter-manual-controls");
    }
    private static void CheckSimpleStartVisible(MainWindow window, int width, int height)
    {
        var start = Field<Button>(window,"startButton");
        var position = start.TranslatePoint(new Point(), (FrameworkElement)window.Content);
        Assert(position.X >= 0 && position.Y >= 0 && position.X + start.ActualWidth <= width && position.Y + start.ActualHeight <= height,
            "Simple Paint action is hidden below the fold");
        Assert(!Ancestors(start).OfType<ScrollViewer>().Any(), "Paint action scrolls away with setup");
        var setup=Field<Button>(window,"simpleSetupButton");
        var scroll=Ancestors(setup).OfType<ScrollViewer>().First();
        var setupPosition=setup.TranslatePoint(new Point(),(FrameworkElement)window.Content);
        var scrollPosition=scroll.TranslatePoint(new Point(),(FrameworkElement)window.Content);
        Assert(setupPosition.Y>=scrollPosition.Y && setupPosition.Y+setup.ActualHeight<=scrollPosition.Y+scroll.ActualHeight,
            "Simple Rust setup action is hidden below the fold");
    }
    private static IEnumerable<DependencyObject> Ancestors(DependencyObject element)
    {
        while ((element = System.Windows.Media.VisualTreeHelper.GetParent(element)) is not null) yield return element;
    }
    private static void CompleteUiTask(Func<Task> action)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        Task task;
        var frame = new DispatcherFrame(); var deadline = DateTime.UtcNow.AddSeconds(20);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        try { task = action(); } catch { SynchronizationContext.SetSynchronizationContext(previous); throw; }
        timer.Tick += (_, _) => { if (task.IsCompleted || DateTime.UtcNow >= deadline) frame.Continue = false; };
        timer.Start(); try { if (!task.IsCompleted) Dispatcher.PushFrame(frame); } finally { timer.Stop(); SynchronizationContext.SetSynchronizationContext(previous); }
        Assert(task.IsCompleted, "Simple import/plan did not complete"); task.GetAwaiter().GetResult();
    }
    private static void CheckSimpleImageImport(string output)
    {
        string directory=Path.Combine(output,"simple-import");Directory.CreateDirectory(directory);
        var s=Settings.Defaults();s.Set("language","English");s.Save(Path.Combine(directory,"config-csharp.json"));
        var w=new MainWindow(directory); var image=new PixelImage(32,32);
        for(int i=0;i<32*32;i++) image.Set(i, i%32<16 ? new Rgb(255,0,0) : new Rgb(0,255,0));
        string path=Path.Combine(directory,"two-colors.png");Images.Save(image,path);
        Task Load() => (Task)typeof(MainWindow).GetMethod("LoadImageFile",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(w,[path])!;
        CompleteUiTask(Load);
        Assert(Field<PixelImage>(w,"source").Width==32&&Field<System.Windows.Controls.Image>(w,"originalImage").Source is not null,"Fresh image import lost the original");
        Assert(!Field<Button>(w,"startButton").IsEnabled&&Field<TextBlock>(w,"status").Text.Contains("Select Canvas and colors"),"Fresh import requires an advanced calibration or has no next action");
        var captured=SimpleCaptured("English");captured.SetPalette([new(new(255,0,0),new(85,15),"main"),new(new(0,255,0),new(95,15),"main")]);
        SetField(w,"settings",captured);Invoke(w,"BuildUi");CompleteUiTask(Load);
        var plan=Field<PaintPlan>(w,"plan");
        Assert(plan.Counts.Count==2,"Simple two-color image lost a color");
        Assert(plan.Identity==PlanIdentity.Compute(Field<PixelImage>(w,"source"),DrawingWorkflow.Effective(Field<Settings>(w,"settings")),plan.Palette),"Simple import/planner used hidden advanced settings");
        Assert(Field<Button>(w,"startButton").IsEnabled,"Image and simple captured setup cannot start");
        Render(w,Path.Combine(output,"simple-import.png"),900,620);CheckSimpleStartVisible(w,900,620);
        Console.WriteLine("PASS simple-image-import-and-plan");
    }
}
