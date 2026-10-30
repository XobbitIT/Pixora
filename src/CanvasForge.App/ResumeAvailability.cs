using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

internal sealed partial class MainWindow
{
    private string? ResumeProblem()
    {
        var execution = EffectiveSettings;
        if(execution.Bool("coverage_audit"))return T("Аудит потребує нового запуску.","Audit requires a fresh START.");
        if(!File.Exists(ResumePath))return T("Немає збереженого прогресу.","No saved progress.");
        if(plan is null || source is null || resumeSchedule is not { } schedule || schedule.Plan!=plan)
            return T("Дочекайся побудови плану для перевірки прогресу.","Wait for the plan to check saved progress.");
        if(plan.Identity!=PlanIdentity.Compute(source,execution,plan.Palette))
            return T("План змінився. Потрібен новий запуск.","The plan changed. A fresh START is required.");
        try
        {
            if(new FileInfo(ResumePath).Length>4096)throw new JsonException();
            var checkpoint=JsonSerializer.Deserialize<ResumeCheckpoint>(File.ReadAllText(ResumePath));
            return checkpoint?.Compatibility(plan.Identity,schedule.GroupCounts) switch
            {
                ResumeCompatibility.Compatible=>null,
                ResumeCompatibility.IdentityVersionChanged=>T("Формат ідентичності прогресу застарів. Продовжуй попередньою збіркою або почни новий запуск.","The progress identity format is obsolete. Continue with the previous build or start a new run."),
                ResumeCompatibility.PlanChanged=>T("Збережений план має іншу ідентичність: зображення, налаштування або збірка змінилися.","The saved plan has a different identity: the image, settings or build changed."),
                ResumeCompatibility.InvalidPosition=>T("Позиція збереженого прогресу виходить за межі плану. Потрібен новий запуск.","The saved progress position is outside the plan. A fresh START is required."),
                ResumeCompatibility.ProgressMismatch=>T("Лічильник прогресу не відповідає завершеним операціям. Потрібен новий запуск.","The progress counter does not match completed operations. A fresh START is required."),
                _=>T("Не вдалося прочитати збережений прогрес. Почни нове малювання.","Cannot read saved progress. Start a new painting.")
            };
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException)
        {
            return T("Не вдалося прочитати збережений прогрес. Почни нове малювання.","Cannot read saved progress. Start a new painting.");
        }
    }
}
