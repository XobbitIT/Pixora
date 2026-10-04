using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CanvasForge.App;
using CanvasForge.Core;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var destination = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "ui-verification"));
        Directory.CreateDirectory(destination);
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        CheckWindow(destination, false, false, "Українська", "adaptive-missing", 1280);
        CheckWindow(destination, true, false, "Українська", "adaptive-ready", 1280);
        CheckWindow(destination, true, true, "Українська", "adaptive-stale", 900);
        CheckWindow(destination, true, false, "English", "adaptive-english", 1280);
        CheckWindow(destination, false, false, "Українська", "adaptive-legacy-enabled", 1280, true);
        CheckFastWindow(destination,"Українська",1280);
        CheckFastWindow(destination,"Українська",900);
        CheckFastWindow(destination,"English",1280);
        CheckSpeedWindow(destination,"Українська",1280,false);
        CheckSpeedWindow(destination,"English",900,false);
        CheckSpeedWindow(destination,"Українська",900,true);
        CheckPreflight(destination);
        CheckExperimentalWindow(destination);
        Console.WriteLine("ALL 13 WPF UI CHECKS PASSED");
        // Windows are rendered without showing or invoking game/capture/input actions.
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in Descendants(VisualTreeHelper.GetChild(root,i))) yield return child;
    }
    private static void CheckWindow(string output, bool calibrated, bool stale, string language, string name, int width, bool enabled = false)
    {
        var directory=Path.Combine(output,name);Directory.CreateDirectory(directory);
        var settings=Settings.Defaults();settings.Set("language",language);settings.Set("adaptive_brush",enabled);
        var cal=settings.Calibration;cal.SetRect("canvas",new(50,50,450,450));cal.SetSession(new(0,0),96,new(1280,720));
        cal.SetPoint("brush_tool",new(600,80));cal.SetRect("brush_shapes",new(500,100,850,140));
        foreach(var (kind,y) in new[]{("size",200),("interval",250),("opacity",300)})
        {
            cal.SetRect(kind+"_track",new(500,y,750,y+40));cal.SetRect(kind+"_value_field",new(750,y,830,y+40));
        }
        cal.SetRect("palette",new(500,350,850,670));settings.SetCalibration(cal);
        settings.SetPalette(new[]{new PaletteEntry(new(0,0,0),new(600,400),"main")});
        if(calibrated)
        {
            settings.Set("brush_calibration_points",new double[][]{[1,3,1],[3,5,3],[10,21,13],[20,35,23]});
            settings.Set("brush_calibration_context",AdaptiveBrush.Context(settings));
        }
        if(stale){cal.SetRect("canvas",new(50,50,451,450));settings.SetCalibration(cal);}
        settings.Save(Path.Combine(directory,"config-csharp.json"));
        var window=new MainWindow(directory);window.ShowPage("adaptive");
        var root=(FrameworkElement)window.Content;
        root.Measure(new Size(width,780));root.Arrange(new Rect(0,0,width,780));root.UpdateLayout();
        var checks=Descendants(root).OfType<CheckBox>().Where(x=>x.Content?.ToString()?.Contains(language=="English"?"Enable adaptive brush":"Увімкнути адаптивний пензель")==true).ToArray();
        Assert(checks.Length==1,"Adaptive toggle duplicated");
        Assert(checks[0].IsEnabled==(enabled||calibrated&&!stale),"Adaptive readiness not reflected in toggle");
        window.SetEditing(false);window.SetEditing(true);
        Assert(checks[0].IsEnabled==(enabled||calibrated&&!stale),"Returning from an operation bypassed adaptive readiness");
        var text=string.Join("\n",Descendants(root).OfType<TextBlock>().Select(x=>x.Text));
        Assert(text.Contains(language=="English"?"1. Preparation":"1. Підготовка"),"Preparation section missing");
        Assert(text.Contains(language=="English"?"2. Automatic calibration":"2. Автоматичне калібрування"),"Calibration section missing");
        Assert(text.Contains(language=="English"?"3. Adaptive painting":"3. Адаптивне малювання"),"Painting section missing");
        if(stale)Assert(text.Contains("Параметри змінилися"),"Stale calibration not explained");
        var bitmap=new RenderTargetBitmap(width,780,96,96,PixelFormats.Pbgra32);bitmap.Render(root);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using(var file=File.Create(Path.Combine(output,name+".png")))encoder.Save(file);
        window.ShowPage("capture");root.UpdateLayout();
        Assert(!Descendants(root).OfType<Button>().Any(x=>x.Content?.ToString()?.Contains("Size anchors")==true),"Manual anchors still required by UI");
        Console.WriteLine("PASS "+name);
    }

    private static void CheckFastWindow(string output,string language,int width)
    {
        string name="fast-"+(language=="English"?"en":"ua")+"-"+width;
        var directory=Path.Combine(output,name);Directory.CreateDirectory(directory);
        var settings=Settings.Defaults();settings.Set("language",language);settings.Set("cell_px",3);settings.Set("speed_profile","Rapid");
        settings.Set("brush_calibration_points",new double[][]{[1,3,1],[10,21,13]});
        string config=Path.Combine(directory,"config-csharp.json");settings.Save(config);
        var window=new MainWindow(directory);window.ShowPage("paint");var root=(FrameworkElement)window.Content;
        root.Measure(new Size(width,780));root.Arrange(new Rect(0,0,width,780));root.UpdateLayout();
        var checks=Descendants(root).OfType<CheckBox>().Where(x=>x.Content?.ToString()?.Contains(language=="English"?"Maximum transfer speed":"Максимальна швидкість перенесення")==true).ToArray();
        Assert(checks.Length==1&&checks[0].IsChecked==false,"Fast transfer toggle missing or enabled silently");
        foreach(var expander in Descendants(root).OfType<Expander>().ToArray())expander.IsExpanded=true;
        root.UpdateLayout();
        string ui=string.Join("\n",Descendants(root).OfType<TextBlock>().Select(x=>x.Text));
        Assert(ui.Contains(language=="English"?"Fast movement packet (1–16)":"Пакет швидкого руху (1–16)"),"Motion packet setting missing");
        Assert(!ui.Contains("px per step")&&!ui.Contains("px за крок"),"Legacy cursor-jump setting is still shown");
        checks[0].IsChecked=true;checks[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        var saved=Settings.Load(config);Assert(saved.Bool("fast_transfer"),"Fast setting was not persisted");
        Assert(saved.Int("cell_px")==3&&saved.Text("speed_profile")=="Rapid","Fast toggle reduced image detail");
        Assert(saved.Data["brush_calibration_points"]!.ToJsonString()==settings.Data["brush_calibration_points"]!.ToJsonString(),"Fast toggle discarded calibration");
        window.SetEditing(false);Assert(!checks[0].IsEnabled,"Fast settings remained editable during painting");window.SetEditing(true);
        root.UpdateLayout();var bitmap=new RenderTargetBitmap(width,780,96,96,PixelFormats.Pbgra32);bitmap.Render(root);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(output,name+".png")))encoder.Save(file);
        Console.WriteLine("PASS "+name);
    }

    private static T Field<T>(MainWindow window,string name) => (T)typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
    private static void SetField(MainWindow window,string name,object value) => typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,value);
    private static void Invoke(MainWindow window,string name) => typeof(MainWindow).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
    private static Settings ReadySettings(string language)
    {
        var settings=Settings.Defaults();settings.Set("language",language);
        var cal=settings.Calibration;cal.SetRect("canvas",new(10,10,1010,1010));cal.SetSession(new(0,0),96,new(1440,1080));
        cal.SetPoint("brush_tool",new(1050,80));cal.SetRect("brush_shapes",new(1050,100,1400,140));
        foreach(var (kind,y) in new[]{("size",200),("interval",250),("opacity",300)})
        {cal.SetRect(kind+"_track",new(1050,y,1300,y+40));cal.SetRect(kind+"_value_field",new(1300,y,1380,y+40));}
        cal.SetRect("palette",new(1050,350,1400,990));settings.SetCalibration(cal);
        settings.SetPalette(new[]{new PaletteEntry(new(0,0,0),new(1100,400),"main")});
        settings.Set("brush_calibration_points",new double[][]{[1,3,1],[3,5,3],[10,21,13],[20,35,23]});
        settings.Set("brush_calibration_context",AdaptiveBrush.Context(settings));
        settings.Set("speed_probe_profile",new SpeedProbeProfile(SpeedCalibration.Context(settings),DateTimeOffset.UtcNow,[new(3,StrokeMethod.Shift,false,8,12,1,40,3,1)]));
        return settings;
    }
    private static void Render(MainWindow window,string path,int width)
    {
        var root=(FrameworkElement)window.Content;root.Measure(new Size(width,780));root.Arrange(new Rect(0,0,width,780));root.UpdateLayout();
        var bitmap=new RenderTargetBitmap(width,780,96,96,PixelFormats.Pbgra32);bitmap.Render(root);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);
    }
    private static void CheckSpeedWindow(string output,string language,int width,bool stale)
    {
        string name="speed-"+(language=="English"?"en":"ua")+"-"+width+(stale?"-stale":"");
        var directory=Path.Combine(output,name);Directory.CreateDirectory(directory);var settings=ReadySettings(language);
        if(stale){settings.Set("brush_shape_slot",4);settings.Set("brush_shape","Square");settings.Set("calibrated_strokes",true);}
        settings.Save(Path.Combine(directory,"config-csharp.json"));var window=new MainWindow(directory);Invoke(window,"ShowSpeedSetup");
        Render(window,Path.Combine(output,name+".png"),width);
        Assert(Field<TabControl>(window,"brushTabs").SelectedIndex==1,"Speed setup link did not select the speed tab");
        Assert(Field<Button>(window,"probeButton").IsEnabled==!stale,"Probe did not require current brush calibration");
        Assert(Field<CheckBox>(window,"auditEnabled").IsEnabled==!stale,"Audit did not require current calibration");
        var toggle=Field<CheckBox>(window,"calibratedMotion");
        Assert(toggle.IsEnabled,"Verified route cannot be selected or stale route cannot be disabled");
        window.SetEditing(false);Assert(!Field<Button>(window,"probeButton").IsEnabled,"Probe still enabled during an operation");
        window.SetEditing(true);Assert(!Field<Button>(window,"startButton").IsEnabled,"Returning from a probe enabled START without an image");
        Assert(Field<Button>(window,"probeButton").IsEnabled==!stale,"Returning from an operation bypassed probe readiness");
        var text=Field<TextBlock>(window,"speedStatus").Text;
        Assert(stale?text.Contains("застарів"):text.Contains(language=="English"?"Verified routes":"підтверджені"),"Speed profile state missing");
        Console.WriteLine("PASS "+name);
    }
    private static void CheckPreflight(string output)
    {
        string name="preflight";var directory=Path.Combine(output,name);Directory.CreateDirectory(directory);var settings=ReadySettings("Українська");
        settings.Save(Path.Combine(directory,"config-csharp.json"));var window=new MainWindow(directory);
        SetField(window,"source",new PixelImage(16,16));Invoke(window,"UpdateReady");
        Assert(Field<Button>(window,"startButton").IsEnabled,"Fully captured input was not ready");
        var live=Field<Settings>(window,"settings");var cal=live.Calibration;cal.SetRect("opacity_value_field",default);live.SetCalibration(cal);Invoke(window,"UpdateReady");
        Assert(!Field<Button>(window,"startButton").IsEnabled,"START ignored a missing numeric field");
        cal.SetRect("opacity_value_field",new(1300,300,1380,340));live.SetCalibration(cal);Invoke(window,"UpdateReady");
        Render(window,Path.Combine(output,name+".png"),900);
        var detail=Descendants((FrameworkElement)window.Content).OfType<TextBox>().Single(x=>x.ToolTip?.ToString()=="Деталізація, px");
        detail.Text="not a number";Assert(!Field<Button>(window,"startButton").IsEnabled,"Invalid numeric input did not block START");
        Assert(Descendants((FrameworkElement)window.Content).OfType<TextBlock>().Any(x=>x.Text.StartsWith("Перевір значення:")&&x.Visibility==Visibility.Visible),"Inline numeric error missing");
        detail.Text="3";Assert(Field<Button>(window,"startButton").IsEnabled,"Corrected input did not restore readiness");
        window.SetEditing(false);window.SetEditing(true);Assert(Field<Button>(window,"startButton").IsEnabled,"Valid readiness was lost after an operation");
        Console.WriteLine("PASS "+name);
    }

    private static void CheckExperimentalWindow(string output)
    {
        string name="experimental-input";var directory=Path.Combine(output,name);Directory.CreateDirectory(directory);
        var settings=ReadySettings("Українська");settings.Set("input_frame_delay_ms",16);settings.Save(Path.Combine(directory,"config-csharp.json"));
        var window=new MainWindow(directory);SetField(window,"source",new PixelImage(16,16));Invoke(window,"UpdateReady");
        var field=Field<TextBox>(window,"experimentalDelay");Assert(field.Text=="12"&&!field.IsEnabled,"Stable exposed inactive experimental timing");
        Render(window,Path.Combine(output,name+".png"),900);
        foreach(var expander in Descendants((FrameworkElement)window.Content).OfType<Expander>().ToArray())expander.IsExpanded=true;
        ((FrameworkElement)window.Content).UpdateLayout();
        var engine=Descendants((FrameworkElement)window.Content).OfType<ComboBox>().Single(x=>x.Items.Cast<object>().Any(v=>v.ToString()=="Experimental (8–16 ms)"));
        engine.SelectedIndex=1;
        Assert(field.IsEnabled&&StrokeTiming.Frame(Field<Settings>(window,"settings"))==.012,"Experimental did not use the visible 12 ms setting");
        Assert(!Field<TextBox>(window,"stableDelay").IsEnabled,"Experimental kept its inactive Stable field editable");
        field.Text="7";Assert(!Field<Button>(window,"startButton").IsEnabled,"Invalid experimental timing did not block START");
        field.Text="8";Invoke(window,"ReadSettings");Invoke(window,"Save");
        Assert(StrokeTiming.Frame(Settings.Load(Path.Combine(directory,"config-csharp.json")))==.008,"Experimental timing was not persisted");
        window.SetEditing(false);Assert(!field.IsEnabled,"Timing remained editable during input");window.SetEditing(true);Assert(field.IsEnabled,"Experimental timing was not restored");
        engine.SelectedIndex=0;Assert(!field.IsEnabled&&StrokeTiming.Frame(Field<Settings>(window,"settings"))==.016,"Experimental changes altered Stable timing");
        engine.SelectedIndex=1;
        foreach(var scroll in Descendants((FrameworkElement)window.Content).OfType<ScrollViewer>().Where(x=>Descendants(x).Any(v=>ReferenceEquals(v,field))).ToArray())
            scroll.ScrollToVerticalOffset(Math.Max(0,scroll.VerticalOffset+field.TransformToAncestor(scroll).Transform(new Point()).Y-240));
        ((FrameworkElement)window.Content).UpdateLayout();Render(window,Path.Combine(output,name+".png"),900);
        Console.WriteLine("PASS "+name);
    }
}
