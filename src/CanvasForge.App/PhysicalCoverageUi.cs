using System.Windows.Controls;
using CanvasForge.Core;

namespace CanvasForge.App;

internal sealed partial class MainWindow
{
    private TextBlock physicalCoverageLabel=new();
    private void BuildPhysicalCoverage(StackPanel controls)
    {
        physicalCoverageLabel=Text(T("Фінальне покриття: очікує малювання.","Final coverage: waiting for painting."),12,Muted);
        physicalCoverageLabel.Margin=new(0,8,0,4);controls.Children.Add(physicalCoverageLabel);
        if(lastPaintProgress?.PhysicalCoverage is {} saved)RenderPhysicalCoverage(saved,lastPaintProgress.CoverageDirectory);
    }
    private void RenderPhysicalCoverage(PhysicalCoverageResult result,string? directory)
    {
        auditHistorical=false;
        physicalCoverageLabel.Text=T($"Покриття: підтверджено {result.ConfirmedPercent:F1}% · пропуски {result.MissingPercent:F1}% · непевно {result.UnknownPercent:F1}%",
            $"Coverage: confirmed {result.ConfirmedPercent:F1}% · missing {result.MissingPercent:F1}% · uncertain {result.UnknownPercent:F1}%");
        physicalCoverageLabel.Foreground=result.Passed?Success:result.Missing>0?Danger:Warning;
        physicalCoverageLabel.ToolTip=result.UnavailableReason is {} reason?T(reason):T("Фізичне покриття перевіряється окремо від виконання команд. Непевні пікселі не є цілями дофарбування.",
            "Physical coverage is checked separately from command progress. Uncertain pixels are not repair targets.");
        coverageState=result.Passed?CoverageState.Verified:CoverageState.NeedsReview;coverageHasGaps=result.Missing>0;
        if(directory is not null&&(auditNotice?.DirectoryPath!=directory||auditNotice.Group!=-1
            ||auditNotice.Missing!=result.Missing||auditNotice.Unknown!=result.Unknown||Math.Abs(auditNotice.Coverage-result.ConfirmedPercent/100)>1e-9))
        {
            auditNotice=new(-1,new(result.Target,result.Confirmed,result.Missing,result.Unknown,result.MissingMask,null),directory);
            auditBannerDismissed=result.Passed;
        }
        RefreshCoverageStatus();RefreshAuditBanner();
    }
}
