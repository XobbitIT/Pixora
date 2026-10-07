using System.Windows.Controls;
using CanvasForge.Core;

namespace CanvasForge.App;
internal sealed partial class MainWindow
{
    private TextBlock workingBrushSummary=new();
    private Button useMeasuredBrush=new();
    private void AddWorkingBrush(StackPanel parent)
    {
        AddCombo(parent,"precision_brush_size",T("Робочий розмір пензля (Size)","Working brush Size"),
            new[]{"Profile","1","3","10","20","40","60","100"},true);
        workingBrushSummary=Text("",12,Muted);parent.Children.Add(workingBrushSummary);
    }
    private void AddWorkingBrushRecovery(StackPanel parent)
    {
        useMeasuredBrush=Button("",()=>
        {
            ReadSettings();
            if(SetupBrushSelection.Select(BrushFootprints.Read(settings),settings.Int("brush_shape_slot",3),settings.Number("probe_size",3)) is { } size)
            {
                settings.Set("precision_brush_size",size.ToString(System.Globalization.CultureInfo.InvariantCulture));Dirty();BuildUi();
            }
        },true);parent.Children.Add(useMeasuredBrush);
    }
    private void RefreshWorkingBrush()
    {
        double size=PaintTimingPlan.DefaultSize(settings);
        bool verified=SpeedCalibration.BrushReady(settings,size);
        workingBrushSummary.Text="Size "+size+" · "+(verified?T("слід підтверджений","footprint verified"):T("слід не підтверджений","footprint unverified"));
        if(settings.Bool("adaptive_brush"))workingBrushSummary.Text+="\n"+T("Адаптивний режим вибирає розмір кожного штриха.","Adaptive mode selects each stroke's Size.");
        else workingBrushSummary.Text+="\n"+T("Це значення в Rust. Деталізація, px — окрема сітка зображення.","This is the Rust control value. Detail, px is the separate image grid.");
        if(settings.Bool("calibrated_strokes")&&!SpeedCalibration.Use(settings))
            workingBrushSummary.Text+="\n"+T("Маршрут швидкості неактуальний: малювання піде звичайним стабільним вводом.","The speed route is stale: painting will use normal stable input.");
        workingBrushSummary.Foreground=verified?Success:Warning;
        double? replacement=SetupBrushSelection.Select(BrushFootprints.Read(settings),settings.Int("brush_shape_slot",3),settings.Number("probe_size",3));
        useMeasuredBrush.Visibility=!settings.Bool("adaptive_brush")&&!verified&&replacement is not null
            ?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;
        useMeasuredBrush.IsEnabled=!Painting;
        useMeasuredBrush.Content=T("Використати підтверджений Size ","Use verified Size ")+replacement;
    }
}
