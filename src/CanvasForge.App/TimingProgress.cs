using CanvasForge.Core;

namespace CanvasForge.App;

internal sealed partial class MainWindow
{
    private PaintProgress? lastPaintProgress;
    private string retainedStatus="";
    private System.Windows.Controls.TextBlock footerProgress=new();
    private System.Windows.Controls.TextBlock preparationSelection=new();
    private System.Windows.Controls.ProgressBar footerProgressBar=new();
    private void ResetPaintProgress()
    {
        lastPaintProgress=null;progressBar.Value=0;progressLabel.Text="0% · "+T("Залишилось —","ETA —");
        footerProgress.Text="";
        footerProgress.Visibility=System.Windows.Visibility.Collapsed;
        footerProgressBar.Value=0;footerProgressBar.Visibility=System.Windows.Visibility.Collapsed;
    }
    private void ApplyPaintProgress(PaintProgress p)
    {
        if(closing)return;
        if(p.Total>0)lastPaintProgress=p;
        RenderPaintProgress(p);
        SetStatus(p.Status);
    }
    private void RenderPaintProgress(PaintProgress p)
    {
        if(p.Total>0)
        {
            progressBar.Value=p.Done*100.0/p.Total;
            string remaining=p.Phase==PaintPhase.Completed?Duration(0):p.Eta<=0
                ?T("триває","in progress"):"≈ "+Duration(Math.Ceiling(p.Eta));
            string phase=p.Phase switch
            {
                PaintPhase.Countdown=>T("Відлік","Countdown"),PaintPhase.Preparing=>T("Підготовка","Preparing"),
                PaintPhase.Auditing=>T("Аудит","Audit"),PaintPhase.Finishing=>T("Завершення","Finishing"),
                PaintPhase.Completed=>T("Завершено","Complete"),_=>T("Малювання","Painting")
            };
            progressLabel.Text=$"{progressBar.Value:F1}% • {p.Done:N0}/{p.Total:N0} • {phase} • {T("Залишилось","ETA")} {remaining}";
            footerProgress.Text=progressLabel.Text;
            footerProgress.Visibility=System.Windows.Visibility.Visible;
            footerProgressBar.Value=progressBar.Value;footerProgressBar.Visibility=System.Windows.Visibility.Visible;
            if(p.Estimate is { } estimate)
            {
                string basis=estimate.Basis switch
                {
                    EtaBasis.Complete=>T("Перенесення завершене","Transfer complete"),
                    EtaBasis.Measured=>T("За виміряним темпом","Based on measured pace"),
                    EtaBasis.Mixed=>T("Частково виміряно","Partly measured"),
                    _=>T("Попередня оцінка","Preliminary estimate")
                };
                eta.Text=p.Phase==PaintPhase.Completed
                    ?T("Завершено за ","Completed in ")+Duration(Math.Ceiling(p.Elapsed))
                    :basis+": "+remaining;
                if(estimate.Basis==EtaBasis.Planned&&p.Phase!=PaintPhase.Countdown)
                    eta.Text+=$" · {Math.Min(estimate.MotionSamples,RemainingTime.Warmup)}/{RemainingTime.Warmup} "+T("операцій","operations");
                else if(estimate.Basis is EtaBasis.Measured or EtaBasis.Mixed)
                    eta.Text+=$" · {T("Зразків","Samples")}: {estimate.WindowSamples}";
                eta.Foreground=estimate.Basis is EtaBasis.Measured or EtaBasis.Complete?Success:Warning;
                eta.ToolTip=T("Після 20 завершених операцій оцінка враховує рух та виміряні витрати між штрихами. Маршрути й розміри вимірюються окремо. Пауза, зміни кольорів і керування не додаються вдруге. Повторні спроби й дофарбування можуть змінити час.",
                    "After 20 completed operations, ETA includes motion and measured work between strokes. Routes and Sizes are measured separately. Pauses, color changes and controls are not counted twice. Retries and repairs can change the duration.");
            }
        }
    }
}
