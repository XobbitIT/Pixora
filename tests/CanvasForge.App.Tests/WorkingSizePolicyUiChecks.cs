using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void CheckWorkingSizePolicy(string output,string language)
    {
        bool en=language=="English";string name="size3-policy-"+(en?"en":"ua"),directory=Path.Combine(output,name);Directory.CreateDirectory(directory);
        var s=CompactHexSettings(language);s.Set("precision_brush_size","1");s.Set("brush_size_value",1);s.Set("brush_calibration_size","1");s.Set("probe_size",1);
        BrushFootprints.Save(s,[SetupStamp(s,3,1)]);s.Save(Path.Combine(directory,"config-csharp.json"));var w=new MainWindow(directory);
        var live=Field<Settings>(w,"settings");Assert(PaintTimingPlan.DefaultSize(live)==1&&live.Text("brush_calibration_size")=="1"&&live.Number("probe_size")==1,"Explicit measured Size 1 was lost");
        Assert(BrushFootprints.Read(live).Any(p=>p.Size==1),"Migration discarded Adaptive evidence");
        Assert(Field<TextBlock>(w,"workingCalibrationStatus").Text.Contains(en?"footprint verified":"слід підтверджений"),"Measured Size 1 was not reflected in the normal working state");
        foreach(string page in new[]{"paint","adaptive","speed"})
        {
            w.ShowPage(page);Render(w,Path.Combine(output,name+"-"+page+".png"),1280,780);
            foreach(var combo in Descendants((FrameworkElement)w.Content).OfType<ComboBox>().Where(c=>c.Tag?.ToString() is "precision_brush_size" or "brush_calibration_size" or "probe_size"))
                Assert(combo.Items.Cast<object>().Any(item=>item.ToString() is "1" or "1/3/10/20")&&!combo.Items.Cast<object>().Any(item=>item.ToString()=="Profile"),"Optional Size 1 is missing");
        }
        w.ShowPage("adaptive");Render(w,Path.Combine(output,name+"-adaptive.png"),1280,780);
        var baseButton=Descendants((FrameworkElement)w.Content).OfType<Button>().Single(b=>Equals(b.Tag,"adaptive-base-calibration"));
        Assert(baseButton.IsEnabled&&baseButton.Content.ToString()==(en?"Measure Size 1 for Adaptive":"Перевірити Size 1 для Adaptive"),"Dedicated Adaptive base action missing or untranslated");
        var worker=(Painter)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Painter));
        typeof(Painter).GetField("settings",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(worker,AdaptiveBrush.CalibrationSettings(live,1));
        var controls=((double Size,double Interval,double Opacity))typeof(Painter).GetMethod("DesiredControls",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(worker,[null])!;
        Assert(controls.Size==1&&controls.Interval==.01&&controls.Opacity==1,"Policy altered the internal Size 1 calibration command");
        Console.WriteLine("PASS "+name);
    }
}
