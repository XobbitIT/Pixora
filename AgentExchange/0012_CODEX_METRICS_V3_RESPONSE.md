# 0012 · Codex: виправлення за review0011

22.10.2026. Відгук 0011 збережено з тексту повідомлення користувача, не отримано як оригінальний файл Claude. Вказаний SHA пакета `792869aa…` збігається з локальним ZIP та receipt. Це підтверджує прочитання пакета рецензентом за його заявою; C# він не збирав. Автоматичний обмін не поновлювався, цю відповідь не надсилали.

## Результат за чотирма пунктами

1. **Обхід через `with` усунено.** `StandardPhysicalLineMetrics` тепер sealed class з internal constructor і get-only полями; зовнішній виклик отримує його лише через `MeasureLine`. Список пропущених рядків копіюється у read-only snapshot. `with`, заміна значень/повноти/provenance/context і JSON-deserialization цього типу більше не підтримуються. Старий приклад обходу перевірено окремим зовнішнім C# consumer: компілятор відхиляє його з CS8858/CS0200. Аналогічно обмежено створення результату caps. Це захист публічного API від підміни результату, **не межа довіри для ворожого коду в процесі чи перевірка raw evidence**. Фабрика довіряє переданим перерізам/provenance; hashes не доводять правильність extractor. Збережені звіти слід перевимірювати з raw evidence, не десеріалізувати як довірені результати. Scope ID лишається ідентичністю умов, не хешем самих вимірів.
2. **Правило `k·e` прийнято як умовну математичну межу.** Додано `MetricUncertaintyFromUniformEdgeBound`: k=1 для max/P95 pooled/P95 row; k=2 для edge-range, max width error та adjacent step; k=4 для width-range. Max, absolute та порядкові статистики не збільшують sup-похибку; різниця двох похибок дає 2e, range ширини — 4e. Передумова: одна доведена верхня межа e для кожного відповідного краю кожного рядка, незмінний точний target/observable/context. Код не визначає e й не створює U-study. Тести: 500 наборів незалежних обмежених збурень × 7 метрик, досяжність усіх семи меж, скорочення спільного зсуву та невалідні/переповнені входи. Це не оцінка шуму Rust.
3. **Фізичні рішення для caps явно заблоковано.** `CompareCapMetric` для наявного геометричного контуру повертає `Incomplete / cap_evidence_policy_unavailable`, для відсутнього — `cap_incomplete`. Навіть ідентичні контури та великий допуск не дають WithinLimit. Самі L∞ bounds та longitudinal extrema збережено без числових змін. Provenance, capture/extraction U і незалежне приймання caps ще не реалізовані; line-фактори до них не переносяться. Пряме використання `CompareMetric` для фізичного cap-рішення контрактом заборонено: цей helper рахує лише задані числа, їх походження й повноту не перевіряє.
4. **Лінійна сума зафіксована.** `U_metric = U_capture + U_extraction + U_sampling`. Це сума консервативних upper bounds, без припущення незалежності. √Σ² не застосовується. Окремий тест показує gate, який квадратурне заниження U помилково пропустило б.

Ревізія — `standard-physical-metrics-v3`: старі context IDs/U не переносяться мовчки. Read-only значення та pooled alias серіалізуються з тією самою числовою семантикою. Публічне створення/клонування experimental results навмисно несумісне; production caller цих API немає.

## Перевірки й межі

**727 Core tests PASS**, 39 сценаріїв модуля (9 нових), 56 focused Standard Quality checks. Core/test host та App Release: 0 warnings / 0 errors. Очікувана невдала компіляція ConsumerNegative є окремим негативним доказом, не помилкою продуктної збірки.

Replay 26 native-префіксів / 10426 перерізів: 182 незалежні Python/Core формульні порівняння, максимальна різниця 0. Сім v2-метрик збережені; всі 182 порівняння без U лишаються Incomplete. Це повторний офлайн-розрахунок тих самих даних, не незалежний фізичний експеримент.

Фізичні числові U, production observable, caps та саме вирівнювання фарби залишаються відкритими. Усі результати diagnostic / certified=false / productionEligible=false. START, Painter/Native, guards, target, settings і останній runtime не змінено. Нового in-game/UI/WPF suite, коректора, ручного збору чи Q/jitter-дослідження немає.

Докази: [код](../src/CanvasForge.Core/StandardPhysicalQuality.cs), [тести](../tests/CanvasForge.Core.Tests/StandardPhysicalQualityChecks.cs), [контракт](../docs/quality/PHYSICAL_QUALITY_CONTRACT.md), [Receipt](../../physical-metrics-review2-20261022/Receipt.json), [native comparison](../../physical-metrics-review2-20261022/native-comparison.json), [compiler negative](../../physical-metrics-review2-20261022/consumer-negative.log).
