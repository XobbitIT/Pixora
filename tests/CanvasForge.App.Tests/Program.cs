using System.IO;
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
        Console.WriteLine("ALL 5 WPF UI CHECKS PASSED");
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
}
