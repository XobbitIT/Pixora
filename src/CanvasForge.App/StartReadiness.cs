using CanvasForge.Core;

namespace CanvasForge.App;

internal sealed partial class MainWindow
{
    // Saved preparation evidence only. Reading readiness never activates Rust,
    // captures the screen, reads the clipboard or sends input.
    private string? StartPreparationProblem(Settings execution)
    {
        if(configWriteProblem is not null)return configWriteProblem;
        if(PaintingReadiness.Read(execution,source is not null).FirstIncomplete is {} step)
        {
            if(step.Key=="capture")return T("Полотно: область не захоплена. Натисни «Підготувати до малювання».","Canvas: no captured region. Press Prepare for painting.");
            if(step.Key=="controls")
            {
                if(execution.Mode==ColorMode.HexDirect&&!execution.HexControlsReady)
                    return T("HEX: не захоплені елементи керування Size для цього режиму.","HEX: Size controls have not been captured for this mode.");
                var controls=execution.PaintCalibration();
                if(!controls.Rect("size_track").Valid)return T("Size: не захоплена смуга розміру пензля.","Size: the brush-size track has not been captured.");
                if(!controls.Rect("size_value_field").Valid)return T("Size: не захоплене числове поле розміру пензля.","Size: the brush-size numeric field has not been captured.");
                if(execution.Bool("controls_validation_failed"))return T("Size: попередню перевірку керування не пройдено. Повтори підготовку.","Size: the previous control check failed. Prepare again.");
                if(execution.Mode!=ColorMode.HexDirect&&controls.Point("brush_tool") is null)
                    return T("Пензель: не знайдено інструмент малювання. Повтори підготовку областей Rust.","Brush: the painting tool was not found. Prepare Rust regions again.");
                return T("Пензель: не захоплена область форм або потрібна форма пензля.","Brush: the shape region or selected brush shape has not been captured.");
            }
            if(step.Key=="brush")return "Size "+PaintTimingPlan.DefaultSize(execution)+": "+T(step.Problem??"");
            return step.Problem is {} problem?T(problem):T("Підготовка не завершена.","Preparation is incomplete.");
        }
        if(EditorEvidence.Current(execution))return null;
        var reference=EditorEvidence.Reference(execution);
        if(reference is null)return T("Редактор: немає збереженого доказу для цього режиму кольорів. Повтори підготовку.","Editor: no saved evidence for this color mode. Prepare again.");
        if(reference.Revision!=EditorEvidence.Revision)return T("Редактор: збережений доказ застарів. Повтори захоплення областей Rust.","Editor: the saved evidence is obsolete. Capture Rust regions again.");
        var calibration=execution.PaintCalibration();var origin=calibration.SessionClient??default;
        ScreenRect Local(ScreenRect rect)=>new(rect.Left-origin.X,rect.Top-origin.Y,rect.Right-origin.X,rect.Bottom-origin.Y);
        if(Local(calibration.Rect("canvas"))!=reference.Canvas)return T("Полотно: виділення змінилося після підтвердження редактора. Повтори підготовку.","Canvas: the selection changed after editor confirmation. Prepare again.");
        if(calibration.SessionDpi!=reference.Dpi||calibration.SessionSize!=reference.ClientSize)
            return T("Редактор: DPI або геометрія збереженого захоплення не збігаються. Повтори захоплення.","Editor: saved capture DPI or geometry does not match. Capture again.");
        if(Local(calibration.Rect("size_track"))!=reference.Size)return T("Size: смуга змінилася після підтвердження редактора. Повтори підготовку.","Size: the track changed after editor confirmation. Prepare again.");
        if(Local(calibration.Rect("brush_shapes"))!=reference.Shapes)return T("Пензель: область форм змінилася після підтвердження редактора. Повтори підготовку.","Brush: the shape region changed after editor confirmation. Prepare again.");
        if(reference.Tool is not {Row.Valid:true})return T("Редактор: немає підтвердження безпечного інструмента пензля. Повтори підготовку.","Editor: safe brush-tool evidence is missing. Prepare again.");
        return T("Редактор: структурний доказ полотна відсутній або несумісний. Повтори підготовку.","Editor: Canvas structure evidence is missing or incompatible. Prepare again.");
    }

    private string? StartUnavailableReason()
    {
        if(imageLoading)return T("Зображення ще завантажується.","The image is still loading.");
        if(setupRunning||inputCheckRunning)return T("Триває підготовка. Дочекайся завершення перевірок.","Preparation is running. Wait for the checks to finish.");
        if(startPreparing&&painter is null)return T("Готую план і перевіряю редактор. Дочекайся завершення або натисни «Зупинити».","Preparing the plan and checking the editor. Wait or press Stop.");
        if(painter is not null||paintTask is {IsCompleted:false})return painter?.Paused==true
            ?T("Малювання призупинено. Натисни «Продовжити» або F6 у Rust.","Painting is paused. Press Resume or F6 in Rust.")
            :T("Малювання вже триває. Для нового запуску спочатку натисни «Зупинити».","Painting is already running. Press Stop before starting again.");
        return null;
    }
}
