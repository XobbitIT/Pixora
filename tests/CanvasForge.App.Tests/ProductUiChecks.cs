using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void CheckProductUi(string output)
    {
        string data=Path.Combine(output,"product-beta57");Directory.CreateDirectory(data);ReadySettings("English").Save(Path.Combine(data,"config-csharp.json"));
        Window? displayed=null;var window=new MainWindow(data,w=>displayed=w);
        var missing=new PhysicalCoverageResult(100,70,20,10,new bool[100],new bool[100]);
        typeof(MainWindow).GetMethod("RenderPhysicalCoverage",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[missing,data]);
        Assert(Field<TextBlock>(window,"physicalCoverageLabel").Text.Contains(70d.ToString("F1")+"%")&&Field<TextBlock>(window,"physicalCoverageLabel").Text.Contains(20d.ToString("F1")+"%"),"Physical percentages missing");
        Assert(Field<CoverageState>(window,"coverageState")==CoverageState.NeedsReview,"100% commands could verify incomplete coverage");
        Console.WriteLine("PASS final coverage has separate confirmed missing and uncertain percentages");
        var pending=new PhysicalCoverageResult(100,0,0,100,new bool[100],Enumerable.Repeat(true,100).ToArray(),"No initial frame");
        typeof(MainWindow).GetMethod("RenderPhysicalCoverage",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[pending,data]);
        Assert(Field<CoverageState>(window,"coverageState")==CoverageState.NeedsReview&&Field<TextBlock>(window,"physicalCoverageLabel").Text.Contains(100d.ToString("F1")+"%"),"Absent baseline called verified");
        Assert(Field<AuditFailureException>(window,"auditNotice").Unknown==100,"Final banner retained a previous result in the same directory");
        Console.WriteLine("PASS unavailable and white on white final audit stays uncertain");
        typeof(MainWindow).GetMethod("RenderPhysicalCoverage",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[new PhysicalCoverageResult(100,100,0,0,new bool[100],new bool[100]),data]);
        Assert(Field<CoverageState>(window,"coverageState")==CoverageState.Verified,"Physical PASS not shown");
        Console.WriteLine("PASS only physical coverage PASS verifies coverage chip");
        var settings=Field<Settings>(window,"settings");bool prior=settings.Bool("adaptive_brush");
        var a=new PaintingModeCandidate(false,100,90,100,10,new bool[100]);var b=new PaintingModeCandidate(true,50,90,100,8,new bool[100]);
        var comparison=new PaintingModeComparison(a,b,true);
        var dialog=(Window)typeof(MainWindow).GetMethod("CreateModeComparisonWindow",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[comparison])!;
        Assert(settings.Bool("adaptive_brush")==prior,"Comparison silently applied suggestion");
        Assert(LogicalNodes((DependencyObject)dialog.Content).OfType<Button>().Any(x=>x.Content?.ToString()=="Apply recommendation"),"Explicit apply missing");
        Console.WriteLine("PASS comparison preserves settings until explicit application");
        var tradeoff=(Window)typeof(MainWindow).GetMethod("CreateModeComparisonWindow",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[comparison with{RecommendedAdaptive=null}])!;
        Assert(!LogicalNodes((DependencyObject)tradeoff.Content).OfType<Button>().Any(x=>x.Content?.ToString()=="Apply recommendation"),"Tradeoff automatically recommended");
        Console.WriteLine("PASS coverage tradeoffs do not offer an automatic winner");
        var image=new PixelImage(16,16);for(int i=0;i<256;i++)image.Set(i,new(10,10,10));settings.Set("edge_merge",true);var plan=Planner.Build(image,settings);
        var preview=(Window)typeof(MainWindow).GetMethod("CreateEdgePreviewWindow",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[plan])!;
        Assert(LogicalNodes((DependencyObject)preview.Content).OfType<Image>().Count()==2,"Before/after target preview missing");
        Console.WriteLine("PASS edge preprocessing exposes both target previews");
        var progress=new PaintProgress(20,100,2,8,"test",new EtaEstimate(8,EtaBasis.Planned,10,10,80,80,100,1){LowerSeconds=7.2,UpperSeconds=12});
        typeof(MainWindow).GetMethod("RenderPaintProgress",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[progress]);
        Assert(Field<TextBlock>(window,"progressLabel").Text.Contains("learning pace")&&!Field<TextBlock>(window,"progressLabel").Text.Contains("up to"),"Warmup presented as an unsupported upper bound");
        typeof(MainWindow).GetMethod("RenderPaintProgress",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[progress with{Estimate=progress.Estimate! with{MotionSamples=20}}]);
        Assert(Field<TextBlock>(window,"progressLabel").Text.Contains("–"),"Post warmup range not visible");
        Console.WriteLine("PASS ETA shows uncertain warmup and measured range");
    }
    private static void CheckProductRenderMatrix(string output)
    {
        var manifest=new List<object>();
        foreach(string language in LanguageCatalog.Names)foreach(int dpi in new[]{96,120,144})foreach(string state in new[]{"initial","ready","paused","audit"})
        {
            string name=$"matrix-{LanguageCatalog.Code(language)}-{dpi}-{state}",data=Path.Combine(output,name);Directory.CreateDirectory(data);
            var settings=state=="initial"?Settings.Defaults():ReadySettings(language);settings.Set("language",language);settings.Save(Path.Combine(data,"config-csharp.json"));
            var window=new MainWindow(data,_=>{});
            if(state!="initial")
            {
                var source=new PixelImage(32,32);for(int i=0;i<1024;i++)source.Set(i,new(0,0,0));
                var plan=Planner.Build(source,Field<Settings>(window,"settings"));SetField(window,"source",source);SetField(window,"plan",plan);
                Field<Image>(window,"originalImage").Source=Images.Bitmap(source);Field<Image>(window,"previewImage").Source=Images.Bitmap(plan.Preview);
                Invoke(window,"RefreshFirstRunGuide");
                Invoke(window,"UpdateReady");Assert(Field<Button>(window,"startButton").IsEnabled,"Ready matrix fixture is blocked: "+name);
            }
            if(state=="paused")
            {
                // No native constructor, window activation or SendInput. Only
                // the UI state of an existing, paused worker is represented.
                var worker=(Painter)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Painter));worker.Paused=true;
                SetField(window,"painter",worker);SetField(window,"paintTask",new TaskCompletionSource().Task);Invoke(window,"UpdateReady");
                Assert(Field<Button>(window,"stopButton").IsEnabled&&!Field<Button>(window,"startButton").IsEnabled,"Paused actions lost their worker state");
                Invoke(window,"ApplyPaintProgress",new PaintProgress(50,100,20,30,"Paused",null,PaintPhase.Drawing));
            }
            if(state=="audit")typeof(MainWindow).GetMethod("RenderPhysicalCoverage",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[new PhysicalCoverageResult(100,70,20,10,new bool[100],new bool[100]),data]);
            const int width=900,height=780;var root=(FrameworkElement)window.Content;root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();
            double scale=dpi/96d;var bitmap=new RenderTargetBitmap((int)(width*scale),(int)(height*scale),dpi,dpi,PixelFormats.Pbgra32);bitmap.Render(root);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(output,name+".png")))encoder.Save(file);
            // Check the actual arranged, rendered controls. Overflow in a
            // scrollable viewport is allowed; START and STOP must stay visible.
            foreach(string field in new[]{"startButton","stopButton"})
            {
                var action=Field<Button>(window,field);var point=action.TransformToAncestor(root).Transform(new Point());
                Assert(action.ActualWidth>=70&&action.ActualHeight>=26&&point.X>=0&&point.Y>=0&&point.X+action.ActualWidth<=width+1&&point.Y+action.ActualHeight<=height+1,$"Clipped action {field}: {name}");
                if(action.Content is string caption)
                {
                    var text=new FormattedText(caption,System.Globalization.CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface(action.FontFamily,action.FontStyle,action.FontWeight,action.FontStretch),action.FontSize,Brushes.White,scale);
                    Assert(text.Width<=action.ActualWidth-action.Padding.Left-action.Padding.Right+2,"Clipped action label: "+name+" "+caption);
                }
            }
            manifest.Add(new{language,dpi,state,png=name+".png",width=bitmap.PixelWidth,height=bitmap.PixelHeight,layoutChecked=true});
        }
        File.WriteAllText(Path.Combine(output,"render-matrix.json"),System.Text.Json.JsonSerializer.Serialize(manifest,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS 84 rendered layouts at 100/125/150 percent DPI in seven languages and four workflow states");
    }
}
