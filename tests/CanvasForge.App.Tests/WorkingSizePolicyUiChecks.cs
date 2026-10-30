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
        s.Data.Remove("brush_footprints");BrushFootprints.Save(s,[SetupStamp(s,3,1)]);s.Save(Path.Combine(directory,SettingsStore.ConfigName));var w=FixtureWindow(directory);
        var live=Field<Settings>(w,"settings");Assert(PaintTimingPlan.DefaultSize(live)==2&&live.Text("brush_calibration_size")=="2"&&live.Number("probe_size")==2,"Legacy Size 1 was not migrated to the minimum");
        Assert(BrushFootprints.Read(live).Any(p=>p.Size==1),"Migration discarded Adaptive evidence");
        Assert(DrawingWorkflow.PaintingProfiles(live).Count==0&&PaintingReadiness.BrushProblem(live) is not null,"An archived Size 1 certified Size 2");
        foreach(string page in new[]{"paint","adaptive","speed"})
        {
            w.ShowPage(page);Render(w,Path.Combine(output,name+"-"+page+".png"),1280,780);
            foreach(var combo in Descendants((FrameworkElement)w.Content).OfType<ComboBox>().Where(c=>c.Tag?.ToString() is "precision_brush_size" or "brush_calibration_size" or "probe_size"))
                Assert(!combo.Items.Cast<object>().Any(item=>item.ToString() is "1" or "1/3/10/20" or "Profile")&&combo.Items.Cast<object>().Any(item=>item.ToString()=="2"),"A Size 1 option remains, or Size 2 is missing");
        }
        w.ShowPage("adaptive");Render(w,Path.Combine(output,name+"-adaptive.png"),1280,780);
        var baseButton=Descendants((FrameworkElement)w.Content).OfType<Button>().Single(b=>Equals(b.Tag,"adaptive-base-calibration"));
        Assert(baseButton.IsEnabled&&baseButton.Content.ToString()==(en?"Measure Size 2 (optional)":"Перевірити Size 2 (необов’язково)"),"Optional Size 2 calibration action missing or untranslated");
        var worker=(Painter)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Painter));
        typeof(Painter).GetField("settings",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(worker,AdaptiveBrush.CalibrationSettings(live,1));
        var controls=(double)typeof(Painter).GetMethod("DesiredControls",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(worker,[null])!;
        Assert(controls==2&&true,"Desired controls can still issue Size 1");
        Console.WriteLine("PASS "+name);
    }
    private static void CheckMinimumPaintingUi(string output)
    {
        var w=PaintingUiWindow(output,"minimum-size2-inputs",ReadySettings("English"));
        SetField(w,"source",new PixelImage(8,8));Invoke(w,"UpdateReady");
        var detail=Editors<TextBox>(w,"cell_px").Single();detail.Text="1";
        Assert(!Field<Button>(w,"startButton").IsEnabled,"One-pixel detail is still accepted for painting");
        detail.Text="2";Assert(Field<Button>(w,"startButton").IsEnabled,"Corrected minimum detail did not restore readiness");
        Console.WriteLine("PASS minimum-detail-2-inline-validation");
        w.ShowPage("adaptive");
        var brush=Editors<TextBox>(w,"brush_size_value").Single();brush.Text="1";
        Assert(!Field<Button>(w,"startButton").IsEnabled,"One-pixel manual brush is still accepted");
        brush.Text="2";Assert(Field<Button>(w,"startButton").IsEnabled,"Corrected minimum brush did not restore readiness");
        Console.WriteLine("PASS minimum-brush-2-inline-validation");
        var live=Field<Settings>(w,"settings");BrushFootprints.Save(live,[SetupStamp(live,3,2)]);
        live.Set("precision_brush_size","2");Invoke(w,"UpdateReady");
        Assert(Field<Button>(w,"startButton").IsEnabled&&PaintTimingPlan.DefaultSize(live)==2,"Verified Size 2 remains blocked");
        Console.WriteLine("PASS verified-size2-readiness");
    }
}
