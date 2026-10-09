using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void CheckCalibrationReasonsUi(string output,string language)
    {
        bool english=language=="English";string name="brush-reasons-"+(english?"en":"ua");
        string directory=Path.Combine(output,name);Directory.CreateDirectory(directory);
        var s=ReadySettings(language);s.Data.Remove("brush_footprints");BrushSpan[] possible=[new(0,-2,3),new(1,-2,3)];var empty=new BrushStamp(possible,[],new(6,6,5));
        var p=BrushFootprints.Build(s,3,1,[empty,empty,empty]);BrushFootprints.Save(s,[p]);
        var samples=Enumerable.Range(1,3).Select(n=>new BrushSignalSample(n,new(n==1?137:n==2?70:2,n==1?23:n==2?10:0,80,new(140,140,140),new(6,6,5)),0,possible)).ToArray();
        BrushSignalDiagnostics.Save(s,[BrushSignalDiagnostics.Summarize(s,3,1,samples,p)]);
        s.Save(Path.Combine(directory,"config-csharp.json"));
        var window=new MainWindow(directory);window.ShowPage("adaptive");Render(window,Path.Combine(output,name+".png"),1280);
        var captions=string.Join("\n",Captions(Field<Dictionary<string,FrameworkElement>>(window,"pages")["adaptive"]));
        var page=(ScrollViewer)Field<Dictionary<string,FrameworkElement>>(window,"pages")["adaptive"];
        page.ScrollToVerticalOffset(540);Render(window,Path.Combine(output,name+"-details.png"),1280);
        Assert(captions.Contains(english?"no stable core":"немає стабільного ядра"),"Empty core mislabeled as unmeasured or generic refusal");
        Assert(captions.Contains("137/80")&&captions.Contains("70/80")&&captions.Contains("2/80")&&captions.Contains(english?"no new trace":"немає нового сліду"),"One rejection hid the remaining two observations");
        Assert(!AdaptiveBrush.CalibrationCurrent(Field<Settings>(window,"settings")),"Empty core enabled Adaptive");
        window.ShowPage("speed");var text=Field<TextBlock>(window,"speedStatus").Text;
        Assert(text.Contains(english?"no current stable core":"немає актуального стабільного ядра"),"Stale speed status asks for another probe before brush recovery");
        if(english)Assert(!System.Text.RegularExpressions.Regex.IsMatch(captions+text,@"[\u0400-\u04FF]"),"New calibration reasons untranslated");
        string cached=english?"Size 1: trace measured, but no stable core. The opaque parts of the three dots do not overlap; these Sizes do not enable adaptive acceleration.":
            "Size 1: слід виміряно, але стабільного ядра немає. Непрозорі частини трьох крапок не збігаються; ці Size не вмикають адаптивне прискорення.";
        Assert(Translations.ForLanguage(cached,!english).Contains(english?"стабільного ядра":"stable core"),"Cached no-core message failed language switching");
        Console.WriteLine("PASS "+name);
    }
    private static void CheckBrushSnapshotsUi(string output,string language)
    {
        bool english=language=="English";string name="brush-snapshots-"+(english?"en":"ua");
        string directory=Path.Combine(output,name),run=Path.Combine(directory,"brush-calibration","run-20261007-000000-test");Directory.CreateDirectory(run);
        var settings=ReadySettings(language);settings.Save(Path.Combine(directory,"config-csharp.json"));
        var before=new PixelImage(96,96);for(int i=0;i<96*96;i++)before.Set(i,new(140,140,140));var after=before.Clone();after.Set(48*96+48,new(0,0,0));
        for(int repeat=1;repeat<=3;repeat++)
        {
            string stem=$"shape-4-size-1-repeat-{repeat}";
            Images.Save(before,Path.Combine(run,stem+"-before.png"));Images.Save(after,Path.Combine(run,stem+"-after.png"));
            File.WriteAllText(Path.Combine(run,stem+"-metrics.json"),JsonSerializer.Serialize(new{shape=4,size=1,repeat,
                contrast=new{PeakDelta=137,RequiredDelta=80,ChangedPixels=23},geometry=new{SolidPixels=6,CenterX=0,CenterY=2.5},
                motion=new BrushDotTrace(BrushDotMotion.Revision,new(515,196),[new("settled",.064,new(515,196)),new("held",.128,new(515,196))])}));
        }
        Window? shown=null;var window=new MainWindow(directory,w=>shown=w);window.ShowPage("adaptive");
        Assert(Field<Button>(window,"brushDiagnosticButton").IsEnabled,"Persisted brush snapshots inaccessible after restart");
        Invoke(window,"ShowBrushDiagnostics");Assert(shown is not null,"Brush diagnostic window did not open");
        var root=(FrameworkElement)shown!.Content;root.Measure(new(860,650));root.Arrange(new(0,0,860,650));root.UpdateLayout();
        var choice=Descendants(root).OfType<ComboBox>().Single(x=>Equals(x.Tag,"brush-repeat"));Assert(choice.Items.Count==3,"Not all dot repeats selectable");
        choice.SelectedIndex=2;string captions=string.Join("\n",Captions(root));
        Assert(captions.Contains("137/80")&&captions.Contains("515")&&captions.Contains(english?"Before press":"Перед натисканням"),"Snapshot coordinates or input phase hidden");
        if(english)Assert(!System.Text.RegularExpressions.Regex.IsMatch(captions,@"[\u0400-\u04FF]"),"Brush snapshot window untranslated");
        var bitmap=new RenderTargetBitmap(860,650,96,96,PixelFormats.Pbgra32);bitmap.Render(root);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using(var file=File.Create(Path.Combine(output,name+"-viewer.png")))encoder.Save(file);
        shown.Close();Console.WriteLine("PASS "+name);
    }
}
