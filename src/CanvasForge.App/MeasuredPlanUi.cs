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
    private Button measuredPlanCompare=new();
    private void RefreshMeasuredPlan()
    {
        measuredPlanDetails.IsEnabled=!Painting&&previewMeasuredPlan is not null;
        measuredPlanCompare.Visibility=previewMeasuredPlan is {UnplannedPixels:>0}&&settings.Mode==ColorMode.HexDirect?Visibility.Visible:Visibility.Collapsed;
        measuredPlanCompare.IsEnabled=!Painting&&source is not null&&numberErrors.Count==0;
        if(previewMeasuredPlan is not {} p)
        {
            measuredPlanStatus.Text=measuredPlanning?T("Обчислюю межі пензля…","Calculating brush boundaries…"):
                measuredPlanFailed?T("Розрахунок не завершений; малювання заблоковане.","Calculation failed; painting is blocked."):
                plan is not null?T("План без захисту за виміряними масками; фізичні межі кольорів не підтверджені. Для захисту потрібні Precision і актуальний відбиток.","Plan without measured-mask protection; physical color boundaries are unverified. Protection requires Precision and a current brush imprint."):"";
            measuredPlanStatus.Foreground=Warning;return;
        }
        measuredPlanStatus.Text=p.UnplannedPixels>0
            ?T($"Без команд: {p.UnplannedPixels:N0} px ({100d*p.UnplannedPixels/Math.Max(1,p.TargetPixels):F1}%). Виміряний Size 1 або менша кількість кольорів можуть зменшити пропуски. Це модель, не аудит Rust.",
                $"{p.UnplannedPixels:N0} px have no commands ({100d*p.UnplannedPixels/Math.Max(1,p.TargetPixels):F1}%). A measured Size 1 or fewer colors may reduce gaps. This is a model, not a Rust audit.")
            :T("План враховує виміряний слід і межі кольорів. Результат у грі ще потребує перевірки.","The plan accounts for measured footprints and color boundaries. The in-game result still needs verification.");
        if(plan is not null&&plan.Mode==ColorMode.RustPalette)
        {
            int scheduled=p.Groups.Count(g=>g.Value.Count>0),skipped=plan.Counts.Count-scheduled;
            measuredPlanStatus.Text+="\n"+T($"Палітра: {plan.Palette.Length} доступних; малювання: {scheduled} кольорів. Без безпечних штрихів: {skipped} кольорів.",
                $"Palette: {plan.Palette.Length} available; drawing: {scheduled} colors. No safe strokes: {skipped} colors.");
        }
        if(p.Diagnostics?.Choice is {} choice)
            measuredPlanStatus.Text+="\n"+T($"Комбінований план: {choice.SelectedStrokes:N0} штрихів; широкий варіант: {choice.MixedStrokes:N0}. Вибір за часом із тим самим покриттям.",
                $"Combined plan: {choice.SelectedStrokes:N0} strokes; wide candidate: {choice.MixedStrokes:N0}. Selected by time with the same coverage.");
        if(p.Diagnostics?.StrokeSavings is {} savings)
            measuredPlanStatus.Text+="\n"+T($"Прибрано зайвих штрихів: {savings.RemovedStrokes:N0}; скорочено: {savings.TrimmedStrokes:N0}. Покриття за суцільним ядром збережено.",
                $"Redundant strokes removed: {savings.RemovedStrokes:N0}; shortened: {savings.TrimmedStrokes:N0}. Solid-core coverage is preserved.");
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
        body.Children.Add(Text(T("Дрібні проходи додають покриття поза суцільним ядром широкого пензля. Часткове перекриття потрібне для суцільної заливки; відбитки на м'яких краях не вважаються підтвердженими.",
            "Small passes add coverage outside the wide brush's solid core. Partial overlap is needed for continuous fill; soft-edge paint is not counted as confirmed."),12,Muted));
        foreach(var part in p.Diagnostics?.StrokeSavings?.Contributions??[])
            body.Children.Add(Text(T($"Size {part.Size} · форма {part.ShapeSlot}: {part.Strokes:N0} штрихів; нове ядро: {part.AddedSolidPixels:N0} px; повторне: {part.RepeatedSolidPixels:N0} px.",
                $"Size {part.Size} · shape {part.ShapeSlot}: {part.Strokes:N0} strokes; new core: {part.AddedSolidPixels:N0} px; repeated: {part.RepeatedSolidPixels:N0} px."),12));
        body.Children.Add(new Image{Source=Images.Bitmap(CoverageAudit.GapOverlay(overlay,p.UnplannedMask)),MaxHeight=650,Stretch=Stretch.Uniform,Margin=new(0,12,0,0)});
        var window=new Window{Title=T("Межі виміряного пензля","Measured brush limits"),Width=900,Height=760,Background=Bg,Content=Scroll(body)};presentAuditDiagnostics(window);
    }
}
