using CanvasForge.Core;

namespace CanvasForge.App;

internal static class RecoveryMessages
{
    internal const string CursorPosition="Пауза: системний курсор не досяг заданої точки лінії. Залиш мишу нерухомою та перевір підключення. Ввід відпущено; результат незавершеної лінії не підтверджено.";
    internal const string CursorMoved="Пауза: курсор перемістився перед нанесенням штриха. Залиш мишу нерухомою, повернись у Rust і натисни F6.";
    internal static string Editor(EditorEvidenceResult? evidence)
    {
        if(evidence?.Reason.StartsWith("tool_",StringComparison.Ordinal)==true)
            return "Пауза: активний інструмент пензля не підтверджено. Вибери пензель у Rust, прибери накладки з панелі й натисни F6. Перед продовженням редактор і колір перевірятимуться повторно.";
        if(evidence?.Reason=="editor_canvas_recapture_required")
            return "Потрібне нове захоплення полотна. Обведи чисту внутрішню область і підтвердь зелені межі клавішею Enter.";
        if(evidence?.Reason=="editor_structure_recapture_required")return "Пауза: потрібен новий еталон рамки. Зупини малювання й повтори захоплення полотна у розділі «Захоплення Rust».";
        if(evidence?.Reason.StartsWith("editor_structure_",StringComparison.Ordinal)==true)return "Пауза: структура або положення рамки не збігаються з захопленим полотном. Прибери накладки; якщо рамка змінилася, зупини малювання й повтори захоплення. Координати не пересувалися автоматично.";
        if(evidence is {State:EditorEvidenceState.Ambiguous}&&evidence.FailedGroups.SequenceEqual(new[]{"canvas-frame"}))
        {
            var group=evidence.Measurements.FirstOrDefault(m=>m.Group=="canvas-frame");
            if(group is not null)return $"Пауза: рамка полотна не підтверджена ({group.Matched}/{group.Total}). Прибери вікна й підказки з рамки, відкрий те саме полотно та натисни F6. Якщо змінився інтерфейс — зупини малювання й повтори захоплення.";
        }
        if(evidence?.Reason=="editor_geometry_changed")return "Геометрія редактора змінилася. Зупини малювання та повтори захоплення областей Rust.";
        return "Редактор Rust не підтверджено. Відкрий те саме полотно; якщо змінився інтерфейс — повтори захоплення. Потім натисни F6.";
    }
    internal static string Error(Exception error)
    {
        if(error is Native.ClipboardFormatException format)
            return $"Формат буфера обміну 0x{format.Format:X4} не вдалося зберегти. Програма не замінила його. Збережи потрібний вміст буфера, самостійно скопіюй звичайний текст і повтори запуск.";
        if(error.Message=="Cannot preserve clipboard data.")
            return "Не вдалося зберегти вміст буфера обміну. Програма не замінила його. Збережи потрібний вміст буфера, самостійно скопіюй звичайний текст і повтори запуск.";
        return error.Message;
    }
}
