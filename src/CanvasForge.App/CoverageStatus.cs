using System.Windows.Controls;

namespace CanvasForge.App;

internal enum CoverageState { Pending, Checking, Verified, NeedsReview, Interrupted, Unchecked }

internal sealed partial class MainWindow
{
    private CoverageState coverageState;
    private bool coverageHasGaps;
    private StatusChip coverageChip = new();
    private TextBlock coverageExplanation = new();

    private void ResetCoverageState()
    {
        coverageState = CoverageState.Pending; RefreshCoverageStatus();
    }

    private void BeginCoverageCheck(bool audited)
    {
        coverageState = audited ? CoverageState.Checking : CoverageState.Unchecked; RefreshCoverageStatus();
    }

    private void CompleteCoverageCheck(bool audited)
    {
        coverageState = audited ? CoverageState.Verified : CoverageState.Unchecked; RefreshCoverageStatus();
    }

    private void SetCoverageChip(StatusChip chip)
    {
        var (text, tone) = coverageState switch
        {
            CoverageState.Checking => (T("Перевіряється", "Checking"), Accent),
            CoverageState.Verified => (T("Перевірено", "Verified"), Success),
            CoverageState.NeedsReview => (T("Потребує уваги", "Needs review"), coverageHasGaps ? Danger : Warning),
            CoverageState.Interrupted => (T("Перервано", "Interrupted"), Warning),
            CoverageState.Unchecked => (T("Без аудиту", "Not audited"), Muted),
            _ => settings.Bool("coverage_audit") ? (T("Очікує", "Pending"), Warning) : (T("Вимкнено", "Disabled"), Muted)
        };
        chip.Set(text, tone);
    }

    private void RefreshCoverageStatus()
    {
        SetCoverageChip(coverageChip);
        if (workflowChips.TryGetValue("coverage", out var chip)) SetCoverageChip(chip);
        coverageExplanation.Text = coverageState switch
        {
            CoverageState.Checking => T("Перевіряється поточний START. Часткове покриття ще не підтверджує весь малюнок.", "The current START is being audited. Partial coverage does not verify the entire painting."),
            CoverageState.Verified => T("Останній START завершив усі перевірки покриття. Перевір вигляд у Rust.", "The last START passed all coverage checks. Review its appearance in Rust."),
            CoverageState.NeedsReview => T("Останній START не пройшов аудит. Діагностика зберігається локально; наступний тест почни новим START.", "The last START failed its audit. Diagnostics are stored locally; retry with a fresh START."),
            CoverageState.Interrupted => T("Перевірку перервано. Покриття всього малюнка не підтверджене.", "The audit was interrupted. Coverage of the entire painting is unverified."),
            CoverageState.Unchecked => T("Останній START був без аудиту. Виконання команд не підтверджує покриття.", "The last START ran without an audit. Completed commands do not verify coverage."),
            _ => settings.Bool("coverage_audit")
                ? T("Очікує нового START. Speed Probe перевіряє швидкість вводу; покриття малюнка перевіряється окремо.", "Waiting for a fresh START. Speed Probe checks input timing; painting coverage is audited separately.")
                : T("Увімкни аудит нижче, щоб перевіряти покриття після кожного кольору. Speed Probe не підтверджує покриття малюнка.", "Enable auditing below to check coverage after each color. Speed Probe does not verify painting coverage.")
        };
        coverageChip.ToolTip = coverageExplanation.Text;
        if (workflowChips.TryGetValue("coverage", out var workflow)) workflow.ToolTip = coverageExplanation.Text;
    }
}
