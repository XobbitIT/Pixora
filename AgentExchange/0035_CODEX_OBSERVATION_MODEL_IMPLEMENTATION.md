# 0035 · Codex · реалізація forward observation двох країв

23.10.2026. Пряме доручення користувача «Починай» після плану моделі/тестів/native comparison. Локальний запис; автоматично Claude не передавався.

**Реалізовано й перевірено розрахункову частину моделі: задані low/high → coverage → явний sampling kernel → чинний local50 → повні residuals. Фізичні input mapping/emitter/render параметри Rust не ідентифіковано. Коректор Standard не реалізовано і нерівність ще не виправлено.**

## Код і межі

[StandardStrokeObservation.cs](../src/CanvasForge.Core/StandardStrokeObservation.cs) — новий pure Core модуль `standard-two-edge-observation-hypothesis-v1`. Модель бере явні геометричні межі кожного перерізу; не виводить їх із команд самостійно. Exact path і контекст потрібні для прив’язки/валідації, а не є доказом калібрування. Немає train/fit, вибору фази, автоматичного kernel, translation невідомих масок чи production eligibility.

- Обидва краї, center та width зберігаються окремо; дозволені неоднакові переходи двох країв. Геометрія може явно описувати Body/StartCap/EndCap.
- Along-axis kernel діє на coverage **до** вимірювання країв. Для невідомого сусіднього перерізу немає silent padding/extrapolation. Known-empty section і відсутній section мають різну семантику.
- Path/direction, collecting binaries/context/emitter/history, geometry hypothesis, kernel, rendered-color hypothesis та before RGBA bound у IDs. Реалізація нового Core прив’язана окремо від старого collecting Core. Hash не доводить походження даних сам собою.
- Input arrays, raster і результати мають власні snapshots; Complete не можна змінити через public record `with`. Diagnostic raster — тільки необхідні рядки у cross±32, поза ними before, не прогноз усього малюнка і не Possible/Solid.
- `Compare` вимагає сумісний контекст, повну потрібну послідовність і два придатні краї на кожному рядку. Missing/ambiguous → INCOMPLETE, aggregate residuals null. Для повних даних повертає nominal max двох країв, width, center і pooled-edge RMS **прогноз−спостереження**. Це не target-quality acceptance, немає U=0/WithinLimit/dispatch/Solid.

Робочий опис: [UV_RESPONSE_MODEL.md](../docs/quality/UV_RESPONSE_MODEL.md). Чинні `StandardDiscreteBand`, `StandardUvPilot/Analysis`, guards, settings/checkpoint/profile serialization, target і звичайний START не змінювалися. Новий product CLI не додавався; native replay — окремий режим уже наявного **test host**, не інструмент нового живого збору.

## Відкат і збірка

[PLAN](../../stroke-observation-implementation-20261023/PLAN.json), [Before](../../stroke-observation-implementation-20261023/Before), [Receipt](../../stroke-observation-implementation-20261023/Receipt.json). Перед зміною збережено554 source hashes та точні попередні зачеплені файли. Старі незакомічені зміни не скидалися; commit/push не виконувалися.

Внутрішня Release-збірка `1.0.14-rc.8+local-observation-model-1`:

- Core SHA256 `089b13f3a07c5c26ce206f448fdd7e2eac75edf9503d827249df15a5ec2e0ee9`.
- App SHA256 `03c34ea2a1131ab7f071914254955f457e5971eef9379e62ea3cba9fa8ac626c`.
- Core/test/App/App-test build logs:0 warnings/errors. Core DLL у двох build roots тотожна.
- Це внутрішній framework-dependent build, не новий self-contained publish/користувацький пакет. Старий живий `local-uv-diagnostic-1` не замінювали. Новий App DLL успішно виконав лише offline `uv-verify` на snapshot, без редактора/вводу.

## Автоматичні перевірки

[13 нових сценаріїв](../tests/CanvasForge.Core.Tests/StandardStrokeObservationChecks.cs) включені в існуючі full Core і focused quality:

- H/V та reverse, independent low/high/width;
- coverage sampling між рядками, known-empty padding проти unknown support;
- двопіксельний розподілений перехід, який пропускає adjacent≥1.5, лишається2px residual у порівнянні повних меж;
- правильний центр не приховує помилкову ширину;
- explicit caps; missing geometry/first sample/empty paint;
- fragmented strong contrast лишається ambiguous;
- path/context bindings, snapshots, invalid kernels/geometry/order/alpha.

**750/750 full Core PASS;79 focused quality PASS.** [Лог](../../stroke-observation-implementation-20261023/core-full-final.log). [6 старих UV App груп](../../stroke-observation-implementation-20261023/uv-regression.log), включно з16 frozen golden evaluations і no-retry/fresh-proof cases, PASS із новим Core. Нового full WPF або живого UI-прогону не було.

## Зіставлення зі справжніми даними

[Native replay test code](../tests/CanvasForge.Core.Tests/StandardStrokeObservationReplay.cs), [підсумок384 порівнянь](../../stroke-observation-implementation-20261023/Native_Replay_Complete/summary.json), [повні прогнозовані/спостережені перерізи](../../stroke-observation-implementation-20261023/Native_Replay_Complete/predicted-observed.csv).

Взято всі8 геометричних гіпотез **із заморожених pre-live** `synthetic-inputs.csv`, усі4 фази кожного з двох старих механізмів; нічого не переобирали за новими даними. Для кожної задано два observation варіанти: sharp `[offset0:1]` і явно гіпотетичне дворядкове усереднення `[0:0.5,−1:0.5]`. Друге — не виміряний kernel Rust. Колір моделі — явно гіпотетичний RGB000000; rendered/ambient color не підганявся до native. Перед зображенням використано точні native before-буфери з перевіреними RGBA SHA.

Повна frozen областьY200…1100: **192/192 INCOMPLETE**, Y200 не домальовано/не пропущено. Окремий post-run interiorY216…1084: **189 complete +3 incomplete**, усі192 залишено в результатах; це не новий held-out або frozen trial. Три incomplete мають неоднозначний predicted strong contour:600-T2/Y971 для perspective-phase0 і vertical-phase0 з дворядковим kernel;1400-T3/Y987 для perspective-phase0.25 з тим самим kernel. Первинний replay-adapter помилково очікував повноту кожної гіпотези; це виправлено на збереження статусу й продовження решти порівнянь. Початковий failure log не видалено.

| Набір гіпотез / observation | Complete interior /48 | Nominal max-edge residual, діапазон між complete cases, px |
| --- | ---: | ---: |
|perspective / sharp|48|1.716699…5.727303|
|perspective / two-row hypothesis|46|1.716699…5.697647|
|vertical / sharp|48|1.284673…5.536605|
|vertical / two-row hypothesis|47|1.284673…5.536605|

Це опис розбіжностей старих unqualified hypotheses, а не зміна UV acceptance або вибір найкращої фази. Числовий контракт B/texture resolution/U не встановлені; жодного nominal result не перетворено на physical PASS.

Незалежний [NumPy checker](../../stroke-observation-implementation-20261023/check_native.py) повторив coverage → byte round-to-even → meanRGB → local50 для **172992 перерізів**:0 status differences, max edge difference `2.2737367544323206e-13px`. Незалежно повторено всі384 residual/missing decisions; max formula difference `3.552713678800501e-15px`. [Receipt](../../stroke-observation-implementation-20261023/independent-check.json). Так перевірено реалізацію математики, а не фізичну правильність гіпотез.

## Фактичний результат і наступна межа

Поставлений етап кодування/автотестів/native comparison виконано. Тепер невдалий прогноз видно за двома повними краями й шириною; розподілений перехід не може сховати residual за нульовим adjacent-jump count. Проте ідентифікованого відгуку Rust `command path → physical low/high` ще немає: новий модуль починає із **заданої** геометрії. Тому inverse-коректор поки не має достатнього прогнозного доказу.

Наступна зміна має визначати фізичну geometry/input/emitter частину та її незалежну перевірку, а не просто підбирати фазу/розмивання цих8 гіпотез. Нинішні12 ліній уже використані для аналізу, не untouched held-out. Чинний C3′ verdict і thresholds, RGB12, ROI16/0.985/delta0, Q/jitter closed та заборона автоматичного обміну залишаються незмінними. Звичайне малювання користувача цим внутрішнім модулем ще не вирівнюється.
