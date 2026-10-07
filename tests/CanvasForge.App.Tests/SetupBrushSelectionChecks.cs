using System.IO;
using System.Windows.Controls;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static BrushFootprint SetupStamp(Settings settings,int shape,double size)
    {
        BrushSpan[] rows=[new(-1,-1,2),new(0,-1,2),new(1,-1,2)];var stamp=new BrushStamp(rows,rows,new(20,20,20));
        return BrushFootprints.Build(settings,shape,size,[stamp,stamp,stamp]);
    }
    private static MainWindow SetupSelectionWindow(string output,string name)
    {
        string directory=Path.Combine(output,name);Directory.CreateDirectory(directory);var settings=ReadySettings("English");
        settings.Set("probe_size",3);settings.Set("brush_shape_slot",3);settings.Set("brush_shape","Round");settings.Save(Path.Combine(directory,"config-csharp.json"));
        return new MainWindow(directory);
    }
    private static Task SetupSelectionTask(MainWindow window)=>(Task)Invoke(window,"CalibrateSetupBrush")!;
    private static void CheckSetupSize3WithoutBase(string output)
    {
        var window=SetupSelectionWindow(output,"setup-size3-without-base");var settings=Field<Settings>(window,"settings");int measurements=0;
        window.SetupBrushCalibrator=()=>
        {
            measurements++;Assert(settings.Int("brush_shape_slot",3)==3,"Usable Round was replaced by fallback");
            var p=SetupStamp(settings,3,3);BrushFootprints.Save(settings,[p]);SetField(window,"lastBrushProfiles",new[]{p});SetField(window,"adaptiveFailure","Size 1 unavailable");return Task.CompletedTask;
        };
        CompleteUiTask(()=>SetupSelectionTask(window));
        Assert(measurements==1&&settings.Number("probe_size")==3&&settings.Int("brush_shape_slot")==3&&PaintTimingPlan.DefaultSize(settings)==3,"Size 1 blocked Size 3 setup or painting returned to Size 1");
        Assert(!AdaptiveBrush.CalibrationCurrent(settings)&&!settings.Bool("adaptive_brush"),"Speed-only Size 3 authorized Adaptive");
        Assert(Field<Button>(window,"spatialButton").IsEnabled,"Measured Size 3 cannot start spatial checks");
        var brush=(Border)Field<System.Collections.IDictionary>(window,"workflowChips")["brush"]!;
        Assert(Captions(brush).Contains("Size 3")&&brush.ToolTip?.ToString()?.Contains("separate Size 1 proof")==true,"Usable Size 3 still shown as a generic brush error");
        Console.WriteLine("PASS setup-size3-without-base");
    }
    private static void CheckSetupFallbackSize3(string output)
    {
        var window=SetupSelectionWindow(output,"setup-square-fallback-size3");var settings=Field<Settings>(window,"settings");int measurements=0;
        window.SetupBrushCalibrator=()=>
        {
            measurements++;SetField(window,"lastBrushProfiles",Array.Empty<BrushFootprint>());
            if(measurements==2){var p=SetupStamp(settings,4,3);BrushFootprints.Save(settings,[p]);SetField(window,"lastBrushProfiles",new[]{p});}
            return Task.CompletedTask;
        };
        CompleteUiTask(()=>SetupSelectionTask(window));
        Assert(measurements==2&&settings.Int("brush_shape_slot")==4&&settings.Number("probe_size")==3,"Verified fallback Size 3 was rejected");
        Assert(!AdaptiveBrush.CalibrationCurrent(settings)&&!settings.Bool("adaptive_brush"),"Fallback bypassed Size 1 proof");
        Console.WriteLine("PASS setup-square-fallback-size3");
    }
    private static void CheckSetupFailedFallback(string output)
    {
        foreach(bool cancel in new[]{false,true})
        {
            var window=SetupSelectionWindow(output,"setup-fallback-"+(cancel?"cancel":"reject"));var settings=Field<Settings>(window,"settings");int measurements=0;
            string? originalPoints=settings.Data["brush_calibration_points"]?.ToJsonString();
            string? originalContext=settings.Data["brush_calibration_context"]?.ToJsonString();
            // An older valid Size must not certify a new failed transaction.
            BrushFootprints.Save(settings,[SetupStamp(settings,3,3)]);
            window.SetupBrushCalibrator=()=>
            {
                measurements++;SetField(window,"lastBrushProfiles",Array.Empty<BrushFootprint>());
                if(measurements==2&&cancel)throw new OperationCanceledException();return Task.CompletedTask;
            };
            bool refused=false;
            CompleteUiTask(async()=>
            {
                try{await SetupSelectionTask(window);}
                catch(OperationCanceledException){refused=cancel;}
                catch(InvalidOperationException e){refused=!cancel&&e.Message.StartsWith("No selected Size");}
            });
            Assert(refused&&measurements==2&&settings.Int("brush_shape_slot")==3&&settings.Text("brush_shape")=="Round","Failed/cancelled fallback changed the selected shape or reused old proof");
            Assert(settings.Data["brush_calibration_points"]?.ToJsonString()==originalPoints&&settings.Data["brush_calibration_context"]?.ToJsonString()==originalContext,"Fallback lost original context");
            Assert(!settings.Bool("adaptive_brush"),"Failed setup enabled Adaptive");
        }
        Console.WriteLine("PASS setup-failed-fallback");
    }
}
