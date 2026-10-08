using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CanvasForge.Core;

namespace CanvasForge.App;
internal sealed partial class MainWindow
{
    private MeasuredColorResult? previewMeasuredPlan;
    private TextBlock measuredPlanStatus=new();
    private Button measuredPlanDetails=new();
    private void RefreshMeasuredPlan()
    {
        measuredPlanDetails.IsEnabled=!Painting&&previewMeasuredPlan is {UnplannedPixels:>0};
        if(previewMeasuredPlan is not {} p){measuredPlanStatus.Text="";return;}
        measuredPlanStatus.Text=p.UnplannedPixels>0
            ?T($"Пензель не поміщається: {p.UnplannedPixels:N0} px залишаться без команд. Виміряний Size 1 або менша кількість кольорів можуть зменшити пропуски.",
                $"Brush does not fit: {p.UnplannedPixels:N0} px have no commands. A measured Size 1 or fewer colors may reduce gaps.")
            :T("План враховує виміряний слід і межі кольорів. Результат у грі ще потребує перевірки.","The plan accounts for measured footprints and color boundaries. The in-game result still needs verification.");
        measuredPlanStatus.Foreground=p.UnplannedPixels>0?Warning:Success;
    }
    private void ShowMeasuredPlan()
    {
        if(previewMeasuredPlan is not {} p||plan is null)return;
        var rect=settings.Calibration.Rect("canvas");var overlay=new PixelImage(rect.Width,rect.Height);
        var xb=Coverage.Partition(0,rect.Width,plan.Width);var yb=Coverage.Partition(0,rect.Height,plan.Height);
        for(int y=0;y<plan.Height;y++)for(int x=0;x<plan.Width;x++)
        {
            int color=plan.Indices[y*plan.Width+x];if(color<0)continue;
            for(int py=yb[y];py<yb[y+1];py++)for(int px=xb[x];px<xb[x+1];px++)overlay.Set(py*rect.Width+px,plan.Palette[color].Color);
        }
        var body=new StackPanel{Margin=new Thickness(16)};
        body.Children.Add(Text(T("Рожевим показані ділянки без безпечних команд. Це модель плану, а не аудит знімка Rust.","Pink marks areas without safe commands. This is a plan model, not an audit of a Rust screenshot."),13));
        body.Children.Add(new Image{Source=Images.Bitmap(CoverageAudit.GapOverlay(overlay,p.UnplannedMask)),MaxHeight=650,Stretch=Stretch.Uniform,Margin=new(0,12,0,0)});
        var window=new Window{Title=T("Межі виміряного пензля","Measured brush limits"),Width=900,Height=760,Background=Bg,Content=Scroll(body)};presentAuditDiagnostics(window);
    }
}
