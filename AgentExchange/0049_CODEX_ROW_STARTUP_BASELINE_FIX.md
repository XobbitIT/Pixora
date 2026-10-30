# 0049 · Codex · 23.10.2026 · первинна причина row-startup і виправлення2

Користувач надав `IOException: This attempt has retained data; retry/resume disabled` з Logs_Row_20261023-162147-7541b7d7. Це вторинна відмова повторного запуску. Перша спроба з Logs_Row_20261023-155925-5dc0ec9c має INCOMPLETE, inputAttempted=false, emittedStrokes=0, captures=0: помилка порівняння початкового baseline. UI-ввід налаштуванняSize був, paint не було.

[Збережений snapshot і аналіз](../../row-startup-failure-20261023/Receipt.json), [точні різниці](../../row-startup-failure-20261023/baseline-differences.json). Увесь Failed_Attempt скопійовано з перевіркоюSHA; вихідна папка й старий runtime не змінювалися.

## Встановлено

- Fresh session-editor crop→baseline: maxRGB2, жодного >12 на1090980пікселях.
- Baseline→confirmation: maxRGB3, жодного >12.
- ІсторичнийUV baseline→новийbaseline:1090880/1090980(99.9908339%)>12, медіанаmaxRGB16, максимум36. Відмінність тону по всьому полотну; причину самої зміни тону не встановлено.
- Історичний view-v2 gate пройшов. На переглянутих blank кадрах немає нових тестових ліній; це візуальна діагностика, не автоматичний empty-canvas classifier.

Отже v1 помилково змішував current clean baseline з RGB іншої сесії. При цьому live frozen v1INCOMPLETE зберігається; не оголошуємо його успішним.

## Зміна рівно в row-підготовці

Нові `standard-row-characterization-v2` і `BaselinePolicy=explicit-fresh-confirmed-canvas-rgb12-v1`, policy bound у manifest. Авторитет чистого кадру — нова **явна** user-confirmed CaptureFresh перед Painter, immutable protocol/settings/build/PID/DPI/origin/client/frame binding. На запуску й у Verify лишається fullCanvasRGB12 проти саме цього кадру. Archive/geometry — незмінний fixed-view witness, а не RGB-еталон чистоти іншої сесії. [Повний scope v2](../docs/quality/ROW_AXIS_CHARACTERIZATION_V2.md).

Це не підміна reference після runtime failure: старий кадр відмови не імпортується в новий протокол; поточні нові baseline/proof ще треба явно захопити. Від користувача лишається підтвердження справді чистого полотна; автоматичний доказ відсутності старої фарби цим не заявляється. Відмова під час малювання не викликає CaptureFresh.16ROI/0.985/zero-displacement, tool/cursor/focus/SendInput, contrast80/RGB12 не послаблені.

Усі12paths/10932points, порядок8training/4held-out, reservations, body/caps, requested000000, mode/timing/emitter й observable/D точно збігаються зv1. Старі UV/texel protocols, Core sources, звичайний START/planner/target/settings не змінені.

Launcher при retained-data показує початкові Status/Reason й виконує лише read-only review; не preflight→run, не видалення markers і не прихований новий каталог. Failed/unknown зберігають exit1. Звичайний новий запуск усе ще потребуєEnter; EOF/Q не дозволяють ввід.

## Перевірено

- Release0warnings/0errors;5row groups,7texel regression groups,7frontier session groups на фінальних DLL.
- Saved-native C# replay відтворив стару відмову й прийняв fresh→baseline/confirm; single pixel поза Hрезервами, uniformRGB13, RGB13 boundary й physically shifted1px view відхилені; рівноRGB12 прийнято.
- 10WindowsPS5.1launcher cases, включно completed/failed/unknown retained status і показом originalReason. Productless fixture не може керуватиRust.
- Actual oldEXE+failed native snapshot+newlauncher: лише review, жодного run/preflight log, джерело байт-незмінне. Packaged fresh preflight/INCOMPLETE-before-live/launcherCheck й Unicodeportable із invalidDOTNET_ROOT пройшли.
- Нового повногоWPF/Core suite не заявляємо:244WPF було виконано дляпопередньоїзбірки0048; зараз цільові регресії для зміненого row-only шляху. Нового native input або physical quality test не було.

Пакет: [Pixora_Row_Axis_Pilot_2_20261023](../../../outputs/Pixora_Row_Axis_Pilot_2_20261023/READ_FIRST_UA.md), version `1.0.14-rc.8+local-row-axis-2`, [manifest](../../../outputs/Pixora_Row_Axis_Pilot_2_20261023/Build_Manifest.json). Новий протокол NOT_STARTED; runtime8.0.31reuse, не freshRIDpublish/cleanWindows. Стара спроба лишається INCOMPLETE0/12.

Наступне — один явний запуск нового Start_Row_Test.cmd, коли користувач готовий доRust; потім training-only аналіз, freeze моделі/U до held-out. МетаB вже прийнята. START/inverse/qualification ще не увімкнені. Нових fit, commit/push чи передачіClaude немає;0049 локальний.
