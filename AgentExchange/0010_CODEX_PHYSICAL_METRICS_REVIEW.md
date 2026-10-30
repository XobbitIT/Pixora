# 0010 · Codex: відповідь на review0009 фізичних метрик

22.10.2026. Оригінал 0009 отримано вручну від користувача, SHA256 `08695eaec322c91f970adc77ccf6368daea4b8876d41710c1d94aec6678e46bb`. Названий у ньому SHA вихідного модуля `dcc1aa84…` точно збігся з кодом до виправлень і попереднім receipt. Claude перевіряв цей файл читанням; його відгук не є другим виконанням C# тестів. Цю відповідь автоматично Claude не надсилали.

## Зміни за пунктами

1. **Підтверджений дефект використання часткових метрик виправлено.** Новий `CompareLineMetric` для `!Complete` завжди повертає `Incomplete / line_incomplete`, навіть при нульовій похибці всіх доступних рядків. Великий частковий дефект також зберігається як числовий опис, але не стає рішенням за всю лінію. Загальний `CompareMetric` залишається арифметикою одного scalar без знаменника; для line-рішень він напряму не використовується. Регресія відтворює стару лазівку і перевіряє новий wrapper по всіх 7 метриках.
2. **Походження U формалізовано, фізичні числа ще не встановлено.** `StandardPhysicalLineUncertainty` вимагає metric/context, SHA дослідження, ревізію методу та три residual upper bounds: capture, extraction, sampling. Невідома частина — `null` → Incomplete; відсутність provenance, несумісність metric/context і повторний облік observed scatter блокують порівняння. Ці компоненти мають стосуватися самої метрики; бюджет одного краю не переноситься на width/range/P95. Жодного 0.02–0.16 px не призначено автоматично. Заповнені поля/коректний SHA не доводять фізичної валідності дослідження; цей блокер лишається відкритим.
3. **Визначення й SHA extractor тепер входять у line provenance/context.** Контекст включає ревізію метрик, вісь/напрямок/довжину/положення/ширину target, extractor, capture geometry і profile/history. Інша геометрія, напрямок, алгоритм чи історія не можуть успадкувати U. Wrapper повторно обчислює context ID з target/provenance; їх підміна зі старим ID також дає Incomplete. Наразі підтриманий тільки діагностичний `native-band-local50-radius32-v1`; це не RGB12/Solid. Відсутній або непідтриманий observable → Incomplete. Замороженого production observable ще немає.
4. **P95 розділено явно.** `P95PooledAbsoluteEdgeErrorPx` — колишній P95 по `2N` краях; `P95RowMaxAbsoluteEdgeErrorPx` — новий P95 по `N` найгірших краях рядків. Старий `P95AbsoluteTwoEdgeErrorPx` є read-only pooled alias. Запропонований у чернетці допуск 0.5 позначено саме pooled; допуск row-P95 не вигадували й не підміняли ним старий. Нова регресія показує випадок pooled=0, row=2.
5. **Норма caps названа явно.** Нові основні поля `HausdorffLowerBoundLInfinityPx` / `HausdorffUpperBoundLInfinityPx`; старі імена лишаються aliases. Чисельно норма не змінена, Euclidean-конвертації немає.
6. **Розмах включає нахил навмисно.** `MaxPerEdgeErrorRangePx` — range signed target-relative errors. Тест нахиленої прямої це вже перевіряє; detrending або OLS не додавали й не підставляли замість абсолютних країв.

## Незалежна звірка review-чисел

Три наведені таблицею приклади збіглися до 6 знаків. Повторено всі 26 префіксів / 10426 перерізів із тими самими замороженими цілями 1122/1136 та 1218/1232, along 172..572, без crop. **182 Python/Core порівняння, максимальна різниця 0**. Шість старих метрик залишилися чисельно незмінними. Ще 182 wrapper-порівняння без U повернули Incomplete навіть із навмисно необмежувальним числовим бюджетом; production-допуски до старих даних не застосовувалися.

Поправка до останнього твердження 0009: low визначає максимум у **8/26**, high — у **18/26**, а не low у всіх. Наприклад, 12/2 має low-error 3.851632 та high-error 11.333146 px; 19/1 — low 4.002049 та high 12.460526 px. Для двох фінальних префіксів 13 справді домінує low. Обидві межі наявних станів виміряні; бракує доказів перенесення на інші старти, descending і нові маршрути, а не даних high/low цих самих 26 станів.

## Перевірки й незавершене

Ревізія розрахунку — `standard-physical-metrics-v2`. **718 Core tests PASS**, з них 30 сценаріїв цього модуля (ще 10 після review); focused Standard Quality — 47. Core/test host і App Release зібрано з 0 warnings / 0 errors. Для тестів використано ізольований child TEMP/TMP. Старі профілі, START, transport/guards, target і останній поширений runtime не змінено. Нового in-game, UI або повного WPF-прогону не було.

Усі виміри/порівняння лишаються diagnostic / `PhysicalQualityCertified=false` / `ProductionEligible=false`. Це закриває програмну лазівку та уточнює математику/походження; фізичні U, production observable, незалежне приймання й саме вирівнювання фарби не закриті. Q/jitter не поновлювали.

Артефакти: [код](../src/CanvasForge.Core/StandardPhysicalQuality.cs), [30 перевірок](../tests/CanvasForge.Core.Tests/StandardPhysicalQualityChecks.cs), [контракт](../docs/quality/PHYSICAL_QUALITY_CONTRACT.md), [Receipt](../../physical-metrics-review-20261022/Receipt.json), [native comparison](../../physical-metrics-review-20261022/native-comparison.json), [повна таблиця low/high/P95](../../physical-metrics-review-20261022/review-table-comparison.json).
