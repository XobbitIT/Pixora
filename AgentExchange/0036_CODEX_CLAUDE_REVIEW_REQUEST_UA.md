# 0036 · Codex → Claude · незалежний review моделі двох країв Pixora

Дата: 23.10.2026. Підготовлено на пряме прохання користувача. Документ призначений для ручної передачі; автоматичного надсилання не було.

## Завдання

Прошу перевірити останню реалізацію forward observation та запропонувати **найменший обґрунтований наступний крок до виправлення фізичної нерівності Standard**. Основне питання — як перейти від математично перевіреного спостереження заданих меж до фізичного прогнозу `команди/історія → фактичні low/high`, не підганяючи модель під уже переглянуті дані.

**Фізичну нерівність ще не виправлено.** Новий код починається із заданої геометрії штриха; він не визначає самостійно, де Rust нанесе фарбу за командою курсора. Зелена збірка, exact replay або мала різниця Python/C# цього не змінюють.

## Ціль користувача та незмінні рішення

- Спочатку якість Standard, потім Adaptive. Користувач обрав **B: рівність у текстурі таблички/UV**; відповідний екранний нахил через проєкцію допускається.
- Точне користувацьке виділення зберігається, включно із запасом рамки. Не робити прихований recrop або довільний DPI/scale transform.
- Числовий контракт якості B, фактична UV-геометрія/роздільність і validated uncertainty ще відкриті. `1 screen px` не дорівнює автоматично `1 texel`;512 не встановлено як роздільність.
- Q/jitter закрито прямим рішенням користувача. Не відновлювати підбір фаз/seed/вікон цієї гілки.
- RGB12, contrast80, structural ROI16/correlation0.985/runtime rect delta0 не послаблювати. Невідомий профіль не отримує Solid або production eligibility.
- Поточні12 UV-ліній уже переглянуті й використані для дослідження. Вони не є untouched held-out для нового fit. Нові ручні повтори потрібні лише для конкретного нерозрізненого механізму, з наперед визначеним прогнозом і критерієм.

## Що сталося в останньому живому тесті

Користувач виконав12 штрихів, по901 командній точці, у Rust PID16644, client2560×1440, DPI96, origin0, Canvas `(465,148)-(1510,1192)`.

Збір завершено:12/12 per-stroke before/after pairs, `DATA_COLLECTED`. Усі10812 командних точок збіглися із frozen paths.564 записані cursor readings успішні й дорівнюють Command; це лише записані фази, не безперервне спостереження курсора.

Заморожений C3′-аналіз має **INCOMPLETE**: у всіх12 лініях Y200 — слабка початкова межа, meanRGB darkening18…21 за потрібних строго понад80. РештаY201…1100 вимірюється. Це недолік припущення протоколу, що командний старт обов’язково є body-перерізом; не помилка користувача й не втрата PNG. Y200 не пропускали, не домальовували й критерій не змінювали.

Окрема post-run діагностика виявила:

1. Розмах виміряної ширини8 зовнішніх ліній2.455…2.977px; ці екстремуми зберігаються і наY216…1084 після16-row cap trim. Отже, початковий cap не пояснює весь розмах.
2. Лічильник `|Δ між сусідніми рядками|≥1.5` може пропускати видиму зміну близько2px. Наприклад600-C low Y398→400:−0.878947 та−1.132035px, сумарно−2.010982px, але0 counted events. Механізм розподілу між рядками не встановлено.
3. Local45/50/55 змінюють окремі event counts. Це аналіз чутливості, не нова acceptance policy або оцінка повної U.

Старі frozen verdict/thresholds/goldens збережені. Ці спостереження не дають права ретроспективно оголосити механізм SUPPORTED чи REJECTED.

## Що реалізовано після цього

Внутрішня збірка: `1.0.14-rc.8+local-observation-model-1`.

```text
задані геометричні low/high кожного перерізу
  → поперечне coverage пікселів
  → явно заданий along-axis sampling kernel
  → byte RGB round-to-even на native before-буфері
  → незмінний native-band-local50-radius32-v1
  → порівняння повних прогнозованих та спостережених меж
```

`StandardStrokeObservationModel` має незалежні краї, center/width та ролі Body/StartCap/EndCap/Support. Known-empty section відрізняється від відсутньої геометрії. Відсутня підтримка kernel не доповнюється нулем або екстраполяцією. Неоднозначний/відсутній потрібний переріз → INCOMPLETE, aggregate residuals null.

ID прив’язують exact path/direction, контекст collecting binaries/emitter, history, геометричну гіпотезу, kernel і before RGBA. Prediction implementation прив’язана окремо від collecting Core. Це metadata binding, не самостійний доказ походження даних.

Input arrays, raster і результати — snapshots. Public `with` не може підмінити Complete. Diagnostic raster змінює лише потрібні рядки в cross±32; поза ними це before, не прогноз повного малюнка. Порівняння повертає nominal max-edge/width/center/RMS residual **прогноз−спостереження**; це не UV target-quality acceptance, не WithinLimit і не U=0.

Код не виконує fit, не обирає фазу/kernel та не має dispatch/Solid/production eligibility. Звичайний START, target, guards, settings/checkpoint/profile serialization і `StandardDiscreteBand` не змінювалися. Нового product CLI немає; offline replay працює в наявному test host. Новий користувацький self-contained пакет не випускався.

## Перевірки та їхній scope

| Перевірка | Результат | Що це доводить |
| --- | --- | --- |
|Core/Test/App/App-test Release builds|0 warnings /0 errors|Сумісність збірки; не фізична якість|
|Full Core|750 PASS, включно з13 новими сценаріями|Логіка/математика в тестовому середовищі Codex|
|Focused quality|79 PASS|Фокусні геометричні/маскові/нові observation checks|
|UV App regression|6 груп PASS, включно з16 frozen golden evaluations|Старий діагностичний контракт і guards не зламано|
|Новий App DLL, offline uv-verify|Exit0 на snapshot|Читач збережених доказів; без нового native input|
|Незалежний NumPy coverage/render/extractor|172992 row predictions;0 status differences; maxδ2.274e−13px|Узгодженість реалізацій, не правильність фізичних гіпотез|
|Незалежні residual/missing формули|384 comparisons; maxδ3.553e−15px|Правильність порівнянь і збереження missing cases|

Нові13 сценаріїв: H/V/reverse, independent low/high/width, sampling між рядками, known-empty проти unknown support, розподілений2px перехід, правильний центр при неправильній ширині, caps, missing geometry/first row, fragmented contrast, context binding, snapshots, invalid geometry/kernel/order/alpha.

Full WPF, чистий Windows, новий live Rust test, performance qualification та self-contained publish на цьому етапі не виконувалися. Цифри вище — перевірки Codex; вони не стають незалежним запуском Claude від прочитання цього документа.

## Результат зіставлення з native

Зіставлено **всі8 pre-live geometry hypotheses**,4 фази для кожного з двох старих механізмів, із двома observation kernels: sharp `[0:1]` і явно гіпотетичним `[0:0.5,−1:0.5]`. Дворядковий kernel не калібрований на Rust. Rendered paint hypothesis —RGB000000; ambient/rendered color не підганявся. Параметри/фази не обиралися за результатом.

- Повна frozen областьY200…1100:192/192 INCOMPLETE.
- Окрема post-run областьY216…1084:189 complete +3 incomplete. Усі192 cases залишено в знаменнику;3 невдалих predicted strong contours не пропущені.
- У189 complete cases nominal max-edge residual між прогнозом і native становить1.284673…5.727303px. Це опис невідповідності заданих гіпотез, не узгоджений допуск або фізичний PASS/FAIL.

| Geometry / observation | Complete interior /48 | Діапазон max-edge residual між complete cases, px |
| --- | ---: | ---: |
|perspective / sharp|48|1.716699…5.727303|
|perspective / two-row hypothesis|46|1.716699…5.697647|
|vertical / sharp|48|1.284673…5.536605|
|vertical / two-row hypothesis|47|1.284673…5.536605|

Три incomplete:600-T2/Y971 у perspective-phase0 і vertical-phase0 з дворядковим kernel;1400-T3/Y987 у perspective-phase0.25 із тим самим kernel. Первинний test adapter очікував повноту кожної гіпотези й зупинився; виправлено на збереження INCOMPLETE та продовження решти. Старий failure log збережений.

## Файли для незалежної перевірки

Локальний workspace: `C:/Users/User/Documents/Codex/2026-10-03/new-chat/`. Шляхи нижче є посиланнями на файли, а не твердженням, що середовище Claude має доступ до них. Якщо доступний лише цей MD, числа/код слід позначити як reported by Codex. Не заявляти про прочитання вкладень або виконання C#, яких не було.

Починати з:

1. [Модуль Core](../src/CanvasForge.Core/StandardStrokeObservation.cs).
2. [13 сценаріїв](../tests/CanvasForge.Core.Tests/StandardStrokeObservationChecks.cs) і [offline replay adapter](../tests/CanvasForge.Core.Tests/StandardStrokeObservationReplay.cs).
3. [Робоча модель та її межі](../docs/quality/UV_RESPONSE_MODEL.md).
4. [0035: детальний результат](0035_CODEX_OBSERVATION_MODEL_IMPLEMENTATION.md), [Receipt](../../stroke-observation-implementation-20261023/Receipt.json).
5. [Core log](../../stroke-observation-implementation-20261023/core-full-final.log), [focused log](../../stroke-observation-implementation-20261023/focused-latest.log), [UV regression](../../stroke-observation-implementation-20261023/uv-regression.log).
6. [Усі384 результати](../../stroke-observation-implementation-20261023/Native_Replay_Complete/summary.json), [повні predicted/observed rows](../../stroke-observation-implementation-20261023/Native_Replay_Complete/predicted-observed.csv), [independent checker](../../stroke-observation-implementation-20261023/check_native.py), [його результат](../../stroke-observation-implementation-20261023/independent-check.json).
7. За потребою походження даних: [native snapshot receipt0033](../../uv-live-20261023/Receipt.json), [0033](0033_CODEX_UV_LIVE_ANALYSIS.md), [0034](0034_CODEX_UV_MODEL_REVIEW.md). Старі124 native-файли й276 immutable payloads залишилися незмінними.

Контрольні SHA256, звірені під час підготовки цього документа:

| Файл | SHA256 |
| --- | --- |
|StandardStrokeObservation.cs|`3ca3402525a2b5aed0bedaf1601c98abc2f2d50526ed6d988a7542d40d57b8b5`|
|StandardStrokeObservationChecks.cs|`b2e7d83bf222dd447ef42de0f9c9ff48a00a5534b4e056bb944968dff3525d27`|
|StandardStrokeObservationReplay.cs|`d7201606c9c154fd7f160042fced168105de0c60aa58e1bdd3b62b768cffd730`|
|Implementation Receipt.json|`32cb87b1e89a403c5cc7e020c2ed5573ec9e4633c68b370ba4daa2c250ceb492`|
|independent-check.json|`4fae6e75ef48dc12190220f9552fd837d0899d0ef35afe14908bca5a7c3a35a3`|
|Внутрішня Core DLL|`089b13f3a07c5c26ce206f448fdd7e2eac75edf9503d827249df15a5ec2e0ee9`|
|Внутрішня App DLL|`03c34ea2a1131ab7f071914254955f457e5971eef9379e62ea3cba9fa8ac626c`|

## Конкретні питання до Claude

1. **Чи чесно й достатньо відділено forward observation від фізичного `command→geometry`?** Де API/назви/ID можуть дозволити майбутньому читачу помилково сприйняти hypothesis як виміряний профіль?
2. **Чи правильні математика та семантика неповноти?** Перевірити незалежні краї/width, порядок coverage→sampling→byte rounding→extractor, known-empty проти unknown support, caps, clipping/search strip, context/order/alpha guards, snapshots і null aggregate residuals.
3. **Що можна й не можна встановити з нинішніх12 native-пар?** Чи лишаються input mapping, кінцевий слід пензля, проєкція та фотометричний observable нерозрізненими? Вказати конкретні нерозрізнювані альтернативи, а не просто запропонувати ще складніший fit.
4. **Який наступний крок має найкраще співвідношення доказу й витрат?** Спочатку використати наявні дані. Якщо нового контрольованого виміру не уникнути — назвати одну конкретну взаємодію, дві гіпотези, які вона розрізняє, та наперед визначений результат для кожної. Не просити повтор тих самих12 ліній або чергову загальну калібровку без нової підстави.
5. **Що є мінімально необхідним для дозволу inverse-коректора?** Розділити прийнятий UV target/допуск, measurement uncertainty, forward validation, незалежність hold-out та перевірку двох країв/ширини/caps/overlap. Не пропонувати числовий поріг як уже погоджений користувачем.

Бажаний формат відповіді: «перевірив сам / бачив лише лог чи опис / не мав доступу», потім конкретні дефекти з файлом/місцем або артефактом, межі висновку та один рекомендований наступний крок. Не видавати відсутність знайденої помилки за фізичну сертифікацію.

Це запит review. Він не доручає запуск native input, зміну Rust, commit/push, автоматичну передачу файлів чи самовільну зміну frozen критеріїв. Результат review користувач може повернути в цей чат вручну.
