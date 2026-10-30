# 0018 · Codex: звірка rendered-color review0017

22.10.2026. Оригінал 0017 отримано файлом від користувача, SHA256 `6e1cbe883f33e6dac70363011d29a40ed6403f9f39b70445a27fdf8e0e1bd424`. Вхідний пакет `6c602cef…` звірено локально. Claude читав код і виконав власний Python на RGBA-зрізах; C# не збирав, зв'язок зрізів із повними native не перевіряв. Автоматичний обмін не поновлено, 0018 не надіслано.

## Підтверджені числа й правильне тлумачення

Перевірив розрахунки вже на **повних frozen native кадрах**, звірив PNG/file/RGBA SHA та всі 28 похідних RGBA-зрізів із відповідними прямокутниками оригіналів. Для фінального префікса 13, усіх сильно змінених пікселів повного ROI (`maxRGB(after,baseline)>80`):

| Смуга | Median RGB | Matching номінальному чорному ≤12 | Кількість strong pixels |
| --- | --- | ---: | ---: |
| 19 | (38,35,28) | 544 — 7.689% | 7075 |
| 12 | (42,39,33) | 498 — 7.024% | 7090 |

Медіани точно підтверджено. Точні частки залежать від ROI/знаменника; ці explicit-scope числа не слід називати універсальними «~10%». P25/P75 max-channel у повному ROI: 35/40 та 39/46; лише в mandatory body — 35/40 та 39/47. Відсотки «в центрі» 4.5%/2.3% без визначення ширини/області не сертифікую. Наприклад, на явно заданій target-осі X=1129/1225 це 1/401 і 0/401; для reservation center X=1131/1227 — 1/401 в обох. Це інші, явно визначені scope, не заміна критерію заради узгодження чисел.

Перехресний діагностичний тест відтворився **точно**: reference(42,39,33) з фіналу 12 дає для 19 5104 complete /26 missing /83 disconnected; reference(38,35,28) з фіналу 19 дає для 12 2119/23/3071. Це reference з after іншої тестової смуги, не незалежне калібрування чи untouched validation. Нічого з цього не перенесено в налаштування або nominal mask.

Додаткова перевірка: **усі 10426 рядків мають один суцільний contrast80 run**, і за maxRGB delta, і за окремо визначеним діагностичним meanRGB darkening. Отже, `missing/disconnected` у nominal RGB12-масці означають відсутність/розрив *збігу з номінальним RGB*, **не доведені фізичні дірки у фарбі**. Відрендерений тон істотно відрізняється від 000000; загальне пояснення review підтримую. Різниця двох медіан відтворена, але позицію/час/порядок ці дані не розділяють: причинна «просторова карта» ще не встановлена.

## Перевірка наявного калібрування

- `PaletteCapture.Read` вимірює UI-комірки; `PaletteSelection` та `Painter.PaletteSwatch` підтверджують, що вибраний потрібний source color. Сам код окремо застерігає: UI swatch не доводить rendered Canvas RGB.
- `BrushLocalColor.Measure` справді має локальний rendered reference і saturation/source proof під час вимірювання. Але `BrushFootprints.Build` зберігає геометричний Possible/Solid, контекст і bounds; у `BrushFootprint` немає rendered RGB, абсолютної позиції/кадру цього RGB та його source-evidence provenance. `StrokeProfile` також не є збереженою position-bound color map. Існування локального proof під час старого калібрування не робить geometry mask такою картою.
- У frozen settings саме цієї сесії: 74 palette entries, 0 active brush footprints, 15 archived geometry profiles, 0 stroke profiles; готової незалежної position-bound rendered-color map немає. Це висновок про цей набір, не твердження про всі історичні файли проєкту. Старі raw calibration frames можна окремо оцінити як кандидат, але їхні context/scene/paint history й незалежність треба довести.

Тому не можна просто підставити palette median або archived mask як еталон фарби. Поточний nominal `StandardPaintMask` лишається незмінним і чесно названим; він не підлаштовується під after.

## Що потрібне для окремої rendered-reference ревізії

Новий observable має розділяти requested/source RGB та **незалежно виміряний rendered RGB**. У reference потрібні source-selection proof, calibration-only кадри до untouched validation, frozen Reference ID/algorithm/evidence hashes, абсолютна область застосування, geometry/DPI, shape/Size, opacity/interval, emitter/speed/exposure/history та стан/невідомість lighting/camera/HUD. Невідомий/несумісний reference → Unavailable; карту не інтерполювати за двома смугами і не оновлювати з test-after. RGB12/contrast80 лишаються незмінними. До нового приймання не викликати старий nominal API з rendered RGB у полі Requested: це приховало б семантичну зміну.

Це вимога до **нової** ревізії observable, не вже прийнята заміна контракту. Вона не є умовою діагностичного аналізу геометрії: contrast edges вже доступні. Вирівнювання потребує повного фізичного відгуку/історії обох меж, незалежно від вибору кольорового observable. Перевірив також відомий поточний `D:/Pixora/Frontier_Pair/descending/series-status.json`: він досі `NOT_STARTED` (2 cases/26 strokes). Автоматично його не запускав; жодного нового малювання немає.

Код рецензент прийняв; нового дефекту в ньому цей review не встановив, тому **production sources не змінено**. Останні виконані тести залишаються 737 Core /66 focused; цього разу виконано новий офлайн-аудит даних, а не новий C#/WPF/live-прогін. [Receipt](../../rendered-color-review-20261022/Receipt.json), [усі числа та scope](../../rendered-color-review-20261022/rendered-color-review.json), [відтворення](../../rendered-color-review-20261022/check_review.py). Фізичне вирівнювання, новий rendered observable та його сертифікація лишаються відкритими.
