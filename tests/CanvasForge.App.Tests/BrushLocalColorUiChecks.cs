using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void CheckBrushLocalColorUi(string output,string language)
    {
        bool english=language=="English";string name="brush-local-color-"+(english?"en":"ua");
        string directory=Path.Combine(output,name),run=Path.Combine(directory,"brush-calibration","run-20261007-000000-local");Directory.CreateDirectory(run);
        var settings=ReadySettings(language);settings.Save(Path.Combine(directory,"config-csharp.json"));
        var before=new PixelImage(96,96);for(int i=0;i<96*96;i++)before.Set(i,new(180,180,180));
        var after=before.Clone();for(int y=50;y<54;y++)for(int x=47;x<51;x++)after.Set(y*96+x,new(28,28,28));
        var stamp=BrushLocalColor.Measure(before,after,after.Clone(),new(48,48),3,new(0,0,0));
        string stem="shape-3-size-3-repeat-1";
        Images.Save(before,Path.Combine(run,stem+"-before.png"));Images.Save(after,Path.Combine(run,stem+"-after.png"));
        Images.Save(after,Path.Combine(run,stem+"-saturation.png"));
        File.WriteAllText(Path.Combine(run,stem+"-metrics.json"),JsonSerializer.Serialize(new{shape=3,size=3,repeat=1,localColor=stamp.LocalColor}));
        var window=new MainWindow(directory);var dialog=(Window)Invoke(window,"CreateBrushDiagnosticWindow",run)!;
        var root=(FrameworkElement)dialog.Content;root.Measure(new(860,650));root.Arrange(new(0,0,860,650));root.UpdateLayout();
        string text=Descendants(root).OfType<TextBlock>().Single(x=>Equals(x.Tag,"brush-repeat-metrics")).Text;
        Assert(text.Contains("1/1")&&text.Contains(english?"Saturation: confirmed":"Насичення: підтверджено"),"Local saturation evidence hidden or marked failed");
        Assert(Descendants(root).OfType<Image>().Count(x=>x.Source is not null)==3,"Saturation screenshot is unavailable");
        if(english)Assert(!System.Text.RegularExpressions.Regex.IsMatch(string.Join("\n",Captions(root)),@"[\u0400-\u04FF]"),"Local color diagnostics untranslated");
        // Older two-frame recordings have no saturation evidence. Do not invent it.
        File.Delete(Path.Combine(run,stem+"-saturation.png"));File.WriteAllText(Path.Combine(run,stem+"-metrics.json"),"{\"shape\":3,\"size\":3,\"repeat\":1}");
        dialog=(Window)Invoke(window,"CreateBrushDiagnosticWindow",run)!;root=(FrameworkElement)dialog.Content;
        root.Measure(new(860,650));root.Arrange(new(0,0,860,650));root.UpdateLayout();
        text=Descendants(root).OfType<TextBlock>().Single(x=>Equals(x.Tag,"brush-repeat-metrics")).Text;
        Assert(text.Contains(english?"Saturation: not checked":"Насичення: не перевірено")&&Descendants(root).OfType<Image>().Count(x=>x.Source is not null)==2,"Legacy evidence promoted");
        Console.WriteLine("PASS "+name);
    }
}
