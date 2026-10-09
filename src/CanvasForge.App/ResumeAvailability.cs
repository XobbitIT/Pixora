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
        if(execution.Number("paint_opacity_value",1)!=1)
            return T("Продовження потребує прозорості 1.","Resume requires opacity 1.");
        try
        {
            if(new FileInfo(ResumePath).Length>4096)throw new JsonException();
            var checkpoint=JsonSerializer.Deserialize<ResumeCheckpoint>(File.ReadAllText(ResumePath));
            return checkpoint?.Matches(plan.Identity,schedule.GroupCounts)==true?null
                :T("Збережений прогрес не відповідає плану. Почни нове малювання.","Saved progress does not match the plan. Start a new painting.");
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException)
        {
            return T("Не вдалося прочитати збережений прогрес. Почни нове малювання.","Cannot read saved progress. Start a new painting.");
        }
    }
}
