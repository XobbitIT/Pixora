using System.IO;
using System.Windows.Controls;
using CanvasForge.Core;
using CanvasForge.App;

internal static partial class Program
{
    private static void CheckExecutionTimingUi(string output,string language)
    {
        bool en=language=="English";string name="execution-timing-"+(en?"en":"ua"),directory=Path.Combine(output,name);Directory.CreateDirectory(directory);
        var s=ReadySettings(language);s.Set("calibrated_strokes",true);s.Set("precision_brush_size","3");
        var p=SpeedCalibration.Read(s)!;s.Set("speed_probe_profile",p with{Samples=p.Samples.Select(sample=>sample with{MaxLength=15}).ToList()});
        s.Save(Path.Combine(directory,"config-csharp.json"));var w=new MainWindow(directory);SetField(w,"source",new PixelImage(8,8));Invoke(w,"UpdateReady");
        var summary=Field<TextBlock>(w,"workingBrushSummary");Assert(summary.Text.Contains("15 px")&&summary.Text.Contains("12"),"Actual tested span and delay are hidden");
        Assert(summary.ToolTip?.ToString()?.Contains(en?"batch joining is disabled":"об'єднання штрихів у пакети вимкнене")==true,"Fast Transfer limitation is not explained");
        var t=new RemainingTime(Enumerable.Range(0,30).Select(i=>new TimedWork($"m{i}","3:H",.08,true)));
        for(int i=0;i<20;i++){t.Complete($"m{i}",.092373);t.RecordOperationOverhead($"m{i}",.00989);}
        var e=t.Estimate(0);Invoke(w,"ApplyPaintProgress",new PaintProgress(20,30,2,e.Seconds,"#000000",e));
        Assert(Field<TextBlock>(w,"eta").ToolTip?.ToString()?.Contains(en?"work between strokes":"витрати між штрихами")==true,"ETA still describes only Draw timing");
        Assert(Math.Abs(Field<PaintProgress>(w,"lastPaintProgress").Eta-10*(.092373+.00989))<1e-9,"UI lost the complete-cycle forecast");
        Render(w,Path.Combine(output,name+".png"),1280,780);Console.WriteLine("PASS "+name);
    }
}
