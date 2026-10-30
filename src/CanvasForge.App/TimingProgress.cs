using CanvasForge.Core;

namespace CanvasForge.App;

internal sealed partial class MainWindow
{
    private PaintProgress? lastPaintProgress;
    private System.Windows.Controls.TextBlock runTimeLabel=new();
    private string retainedStatus="";
    private System.Windows.Controls.TextBlock footerProgress=new();
    private System.Windows.Controls.TextBlock preparationSelection=new();
    private System.Windows.Controls.ProgressBar footerProgressBar=new();
    private void ResetPaintProgress()
    {
        lastPaintProgress=null;progressBar.Value=0;progressLabel.Text="0% · "+T("Залишилось —","ETA —");
        runTimeLabel.Text="";
        physicalCoverageLabel.Text=ReleaseProfile.Standard(settings)?T("Standard: перевір вигляд і покриття у Rust.","Standard: inspect appearance and coverage in Rust."):T("Фінальне покриття: очікує малювання.","Final coverage: waiting for painting.");
        footerProgress.Text="";
        footerProgress.Visibility=System.Windows.Visibility.Collapsed;
        footerProgressBar.Value=0;footerProgressBar.Visibility=System.Windows.Visibility.Collapsed;
    }
    private void ApplyPaintProgress(PaintProgress p)
    {
        if(closing)return;
        if(p.TimesOnly)
        {
            if(lastPaintProgress is {} last)lastPaintProgress=last with{Times=p.Times};
            RenderPaintProgress(p with{Total=0});return;
        }
        if(p.Total>0)lastPaintProgress=p;
        RenderPaintProgress(p);
        SetStatus(p.Status);
        RefreshRuntimeReadiness();
    }
    private void RefreshRuntimeReadiness()
    {
        if(imageLoading)
        {
            ready.Text=T("Зображення ще завантажується.","The image is still loading.");ready.Foreground=Muted;
            badge.Text=T("ВИКОНУЄТЬСЯ","RUNNING");badge.Foreground=Warning;badge.ToolTip=ready.Text;return;
        }
        if(preparationResume is not null||setupRunning||inputCheckRunning)
        {
            bool setupPaused=preparationResume is not null;
            ready.Text=setupPaused?T("Підготовку призупинено. Натисни «Продовжити».","Preparation paused. Press Resume."):
                T("Триває підготовка. Дочекайся завершення перевірок.","Preparation is running. Wait for the checks to finish.");
            badge.Text=setupPaused?T("ПАУЗА","PAUSED"):T("ВИКОНУЄТЬСЯ","RUNNING");
            ready.Foreground=setupPaused?Warning:Muted;badge.Foreground=Warning;badge.ToolTip=ready.Text;return;
        }
        if(painter is null&&!startPreparing&&(setupCompletion is SetupStageState.Cancelled or SetupStageState.Failed))
        {
            ready.Text=T(setupOutcome);ready.Foreground=Warning;
            badge.Text=T("ПОТРІБНА ПІДГОТОВКА","SETUP REQUIRED");badge.Foreground=Warning;badge.ToolTip=ready.Text;return;
        }
        if(startPreparing&&painter is null)
        {
            ready.Text=T("Перевіряю редактор перед малюванням…","Checking the editor before painting…");ready.Foreground=Muted;
            badge.Text=T("ВИКОНУЄТЬСЯ","RUNNING");badge.Foreground=Warning;badge.ToolTip=ready.Text;return;
        }
        if(painter is null)return;
        bool paused=painter.Paused;
        ready.Text=paused?T("Малювання призупинено. Причина й дія відновлення показані внизу вікна.","Painting is paused. See the reason and recovery action at the bottom of the window."):
            startPreparing&&paintTask is null?T("Перевіряю редактор перед малюванням…","Checking the editor before painting…"):
            T("Малювання триває.","Painting is in progress.");
        badge.Text=paused?T("ПАУЗА","PAUSED"):T("ВИКОНУЄТЬСЯ","RUNNING");
        ready.Foreground=paused?Warning:Muted;badge.Foreground=paused?Warning:Success;badge.ToolTip=ready.Text;
    }
    private void RenderPaintProgress(PaintProgress p)
    {
        if(p.Times is {} times)runTimeLabel.Text=T("Кольори","Colors")+": "+Duration(times.Colors)+" · "+T("Штрихи","Strokes")+": "+Duration(times.Motion)+"\n"+T("Службовий час","Service time")+": "+Duration(times.Service)+" · "+T("Завершення","Finish")+": "+Duration(times.Finish)+"\n"+T("Пауза","Pause")+": "+Duration(times.Pause);
        if(p.PhysicalCoverage is {} coverage)RenderPhysicalCoverage(coverage,p.CoverageDirectory);
        if(p.Total>0)
        {
            progressBar.Value=p.Done*100.0/p.Total;
            string remaining=p.Phase==PaintPhase.Completed?Duration(0):p.Eta<=0
                ?T("триває","in progress"):"≈ "+Duration(Math.Ceiling(p.Eta));
            if(p.Phase!=PaintPhase.Completed&&p.Estimate is {UpperSeconds:>0} range)
                remaining=range.MotionSamples<RemainingTime.Warmup
                    ?"≈ "+Duration(Math.Ceiling(range.Seconds))+" · "+T("уточнюється","learning pace")
                    :Duration(Math.Ceiling(range.LowerSeconds))+"–"+Duration(Math.Ceiling(range.UpperSeconds));
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
