using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void ExportInternationalCatalog(string output)
    {
        string data = Path.Combine(output, "international-catalog"); Directory.CreateDirectory(data);
        ReadySettings("English").Save(Path.Combine(data, "config-csharp.json"));
        var window = new MainWindow(data);
        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (var page in Field<Dictionary<string, FrameworkElement>>(window, "pages").Values)
        {
            foreach (var item in InternationalElements(page))
            {
                if (item is TextBlock t) values.Add(t.Text);
                if (item is ContentControl control && control.Content is string caption) values.Add(caption);
                if (item is Expander expander && expander.Header is string header) values.Add(header);
                if (item is ComboBox combo) foreach (var option in combo.Items) values.Add(option is ComboBoxItem c ? c.Content?.ToString() ?? "" : option.ToString() ?? "");
            }
        }
        File.WriteAllText(Path.Combine(output,"international-catalog.json"), JsonSerializer.Serialize(values.Order().ToArray(),new JsonSerializerOptions { WriteIndented=true, Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }
    private static IEnumerable<DependencyObject> InternationalElements(DependencyObject root)
    {
        yield return root;
        foreach (object child in LogicalTreeHelper.GetChildren(root)) if(child is DependencyObject dependency)
            foreach (var element in InternationalElements(dependency)) yield return element;
    }
    private static void CheckInternationalCatalog()
    {
        Assert(Translations.InternationalStringCount >= 300,"International catalog missing");
        Assert(LanguageCatalog.FromCulture(CultureInfo.GetCultureInfo("es-MX"))=="Español" && LanguageCatalog.FromCulture(CultureInfo.GetCultureInfo("ja-JP"))=="English","Culture fallback incorrect");
        foreach(string language in LanguageCatalog.Names)
        {
            string path=@"C:\Users\Family\test-image.png";
            Assert(Translations.ForLanguage(path,language)==path,"User path was translated");
            Assert(Translations.ForLanguage("#12ABEF",language)=="#12ABEF","HEX data changed");
            Assert(Translations.Option("unknown-setting","My custom value",language)=="My custom value","Unknown canonical option changed");
            if(language is "Українська" or "English")continue;
            string dynamic=Translations.ForLanguage("Apply 128",language);
            Assert(dynamic.Contains("128")&&dynamic!="Apply 128","Template substitution missing");
            string failure=Translations.ForLanguage("Could not measure Size 3: Pending",language);
            Assert(failure.Contains("3")&&!failure.Contains("Could not measure Size"),"Nested failure untranslated");
            string translated=Translations.ForLanguage("Open image",language);
            Assert(Translations.ForLanguage(translated,"English")=="Open image","Cached label did not return to English");
            Assert(Translations.ForLanguage("Обведи область мишею. ESC — скасувати.",language)!= "Drag to select an area. ESC — cancel.","Capture instructions fell back to English");
        }
        Assert(Translations.ForLanguage("Open image","unsupported-language")=="Open image","Unknown language fallback broken");
        Console.WriteLine("PASS international catalog, placeholders, cached text and culture fallback");
    }
    private static void CheckInternationalWindow(string output,string language)
    {
        string code=LanguageCatalog.Code(language),data=Path.Combine(output,"international-"+code);Directory.CreateDirectory(data);
        var settings=Settings.Defaults(); settings.Set("language",language);settings.Save(Path.Combine(data,"config-csharp.json"));
        var window=new MainWindow(data);Assert(window.InterfaceLanguage==language,"Language not restored");
        Assert(!Field<Button>(window,"startButton").IsEnabled,"Welcome bypassed start validation");
        Assert(Field<Border>(window,"firstRunGuide").Visibility==Visibility.Visible,"First launch guide missing");
        var buttons=InternationalElements(window).OfType<Button>().Where(b=>b.Content is string).ToArray();
        Assert(buttons.Any(b=>b.Content!.ToString()==Translations.ForLanguage("Open image",language)),"Import action missing");
        if(language is not ("Українська" or "English"))
        {
            string[] missing=buttons.Select(b=>System.Text.RegularExpressions.Regex.Replace(b.Content!.ToString()!,@"^\d+\.\s*","")).Where(t=>t.Any(char.IsLetter)&&t!="HEX" && !t.StartsWith("Size ") &&
                Translations.ForLanguage(t,"English")==t && !Translations.HasInternationalEntry(t)).Distinct().ToArray();
            Assert(missing.Length==0,"Untranslated buttons in "+language+": "+string.Join(" | ",missing));
        }
        Render(window,Path.Combine(output,"first-launch-"+code+".png"),language=="Deutsch"?900:1280,780,expandPaintingControls:false);
        Invoke(window,"OpenFirstRunGuide");Assert(Field<Border>(window,"firstRunGuide").Visibility==Visibility.Visible,"Help not accessible");
        window.SetEditing(false);Assert(!Field<ComboBox>(window,"languageSelector").IsEnabled,"Language can change during input");window.SetEditing(true);
        var saved=Settings.Load(Path.Combine(data,"config-csharp.json"));Assert(saved.Text("color_mode")==settings.Text("color_mode")&&saved.Number("precision_brush_size")==settings.Number("precision_brush_size"),"Language changed painting parameters");
        Console.WriteLine("PASS international window "+code);
    }
    private static void CheckInternationalSwitch(string output)
    {
        string data=Path.Combine(output,"international-switch");Directory.CreateDirectory(data);ReadySettings("Українська").Save(Path.Combine(data,"config-csharp.json"));
        var window=new MainWindow(data);var settings=Field<Settings>(window,"settings");string mode=settings.Text("color_mode"),shape=settings.Text("brush_shape");double detail=settings.Number("cell_px");
        foreach(string language in LanguageCatalog.Names)
        {
            Field<ComboBox>(window,"languageSelector").SelectedItem=language;
            Assert(window.InterfaceLanguage==language && settings.Text("color_mode")==mode && settings.Text("brush_shape")==shape && settings.Number("cell_px")==detail,"Switch changed canonical settings");
            Assert(Settings.Load(Path.Combine(data,"config-csharp.json")).Text("language")==language,"Language selection not persisted");
        }
        Console.WriteLine("PASS seven-language switching and canonical settings");
    }
    private static void CheckFirstRunLifecycle(string output)
    {
        string data=Path.Combine(output,"first-run-lifecycle");Directory.CreateDirectory(data);var window=new MainWindow(data);
        var guide=Field<Border>(window,"firstRunGuide");
        var prepare=InternationalElements(guide).OfType<Button>().Single(b=>b.Content!.ToString()==Translations.ForLanguage("Перейти до підготовки",false,"Go to preparation"));
        prepare.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert(Field<string>(window,"currentPage")=="paint"&&guide.Visibility==Visibility.Collapsed&&!Field<bool>(window,"setupRunning"),"Guide did not reveal the main preparation or unexpectedly started game input");
        Invoke(window,"OpenFirstRunGuide");Assert(guide.Visibility==Visibility.Visible,"Guide cannot reopen");
        SetField(window,"source",new PixelImage(8,8));Invoke(window,"RenderPlan");Assert(guide.Visibility==Visibility.Collapsed,"Guide hides image after import");
        Invoke(window,"OpenFirstRunGuide");Assert(guide.Visibility==Visibility.Visible,"Help not available after import");
        Console.WriteLine("PASS first-run guide navigation and image lifecycle without input");
    }
}
