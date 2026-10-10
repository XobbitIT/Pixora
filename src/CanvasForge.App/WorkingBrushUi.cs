using System.Windows.Controls;
using CanvasForge.Core;

namespace CanvasForge.App;
internal sealed partial class MainWindow
{
    private TextBlock workingBrushSummary=new();
    private Button useMeasuredBrush=new();
    private void AddWorkingBrush(StackPanel parent)
    {
        AddCombo(parent,"precision_brush_size",T("Розмір пензля в Rust (Size)","Rust brush Size"),
            SetupBrushSelection.SizeOptions,true);
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
        bool verified=PaintingReadiness.BrushProblem(settings) is null;
        workingBrushSummary.Text="Size "+size+" · "+(verified?T("слід підтверджений","footprint verified"):T("слід не підтверджений","footprint unverified"));
        if(BrushFootprints.Find(settings,size,settings.Int("brush_shape_slot",3)) is {} measured)
            workingBrushSummary.Text+="\n"+T("Можливий слід: ","Possible footprint: ")+measured.SafetyBounds.Width+" × "+measured.SafetyBounds.Height+
                " px · "+T("стабільне ядро: ","solid core: ")+measured.SolidCore.Width+" × "+measured.SolidCore.Height+" px";
        if(settings.Bool("adaptive_brush"))workingBrushSummary.Text+="\n"+T("Адаптивний режим вибирає розмір кожного штриха.","Adaptive mode selects each stroke's Size.");
        else workingBrushSummary.Text+="\n"+T("Це значення в Rust. Деталізація, px — окрема сітка зображення.","This is the Rust control value. Detail, px is the separate image grid.");
        if(settings.Bool("calibrated_strokes")&&!SpeedCalibration.Use(settings))
            workingBrushSummary.Text+="\n"+T("Маршрут швидкості неактуальний: малювання піде звичайним стабільним вводом.","The speed route is stale: painting will use normal stable input.");
        if(SpeedCalibration.Use(settings))
        {
            var routes=(SpeedCalibration.Read(settings)?.Samples??[]).Where(p=>p.Size==size).Select(p=>$"{(p.Vertical?"V":"H")} {Option("stroke_method",p.Method.ToString())} · {p.SafeMs:0} {T("мс","ms")} · {p.MaxLength} px").ToArray();
            if(routes.Length>0)workingBrushSummary.Text+="\n"+T("Перевірені маршрути: ","Verified routes: ")+string.Join("; ",routes);
            workingBrushSummary.ToolTip=T("Перевірений маршрут використовується, коли він швидший за звичайний стабільний рух. Довжина відрізків і запас затримки не змінюються.","A verified route is used when faster than normal stable movement. Tested spans and delay margins remain unchanged.");
        }
        else workingBrushSummary.ToolTip=null;
        workingBrushSummary.Foreground=verified?Success:Warning;
        double? replacement=SetupBrushSelection.Select(BrushFootprints.Read(settings),settings.Int("brush_shape_slot",3),settings.Number("probe_size",3));
        useMeasuredBrush.Visibility=!settings.Bool("adaptive_brush")&&!verified&&replacement is not null
            ?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;
        useMeasuredBrush.IsEnabled=!Painting;
        useMeasuredBrush.Content=T("Використати підтверджений Size ","Use verified Size ")+replacement;
    }
}
