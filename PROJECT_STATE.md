# Pixora — поточний стан

Оновлено **26.10.2026** локальним Claude (Codex на паузі за рішенням користувача) після реалізації пакета Row_Calib_1. Спочатку читати [AGENTS.md](AGENTS.md); за потреби — [архітектуру](ARCHITECTURE.md), [матрицю](TEST_MATRIX.md), [Steam](STEAM_RELEASE.md). Попередній стан збережено в [Before](../row-calib-implementation-20261025/Before/PROJECT_STATE.md); подробиці — у тематичних звітах.

## Мета й готовність

Користувач прийняв B: **0 неочікуваних H/V-переходів у сітці Rust; ширина точно за ціллю в текселях**; екранний нахил проєкції допускається. [Пряме погодження23.10](docs/quality/texel-quality-b-goal.draft.json) не перепогоджувати. Standard ще не має повної фізичної сертифікації ширини/Solid/U/caps/overlap/color. Adaptive — після Standard. **26.10: правий край прийнято в стані «повне покриття» (+≈0.5 текселя), [0090](AgentExchange/0090_USER_DECISION_RIGHT_EDGE_FULL_COVERAGE.md).**

## Попередній крок: Row_Calib_1 (виконано, див. 0078)

[0077](AgentExchange/0077_CLAUDE_LOCAL_CALIBRATION_PACKAGE.md), [контракт](docs/quality/ROW_CALIB_PILOT_V1.md), [Receipt](../row-calib-implementation-20261025/Receipt.json). Пакет [Pixora_Row_Calib_1_20261026](../../outputs/Pixora_Row_Calib_1_20261026/READ_FIRST_UA.md), запуск `Start_Calib_Test.cmd`. Стан **NOT_STARTED**; агент Rust-ввід не виконував.

- Збірка `1.0.14-rc.8+local-row-calib-1`; Protocol `8fdd511f…902680`, App `8fe20e78…5595`, Core `27409b6d…70e2` (код Core не змінений; інший SHA через рядок версії).
- 17 cases / 40 штрихів: 16 калібрувальних H (13 з0074 + Y962/316/753) і чорна картинка40×15 (24 runs) у рядках313…327/Y646…674 на незмінній1148-родині, запаси0.25.
- Уточнена родина (28663, крок5×) показала, що13 проб0074 лишають3 гілки <40 рядків; з трьома доданими пробами прогноз — мінімум **44** supported-рядки в кожній гілці обох родин. Умовний офлайн-прогноз, не калібрування.
- Правило мітки (19 перерізів, j/j+2, median≤0.15, INCOMPLETE/MODEL_CONTRADICTED), derived-family record і Core-аудит заморожені до збору.
- Перевірено: Release0warnings/0errors; Core **779 PASS**; WPF **244 PASS**; calib App **8/8 груп**; offgrid App **8/8** (регресія); launcher calib/offgrid **11/11** кожен; **7 packaged checks**.

## Остання перевірка

[0073](AgentExchange/0073_CLAUDE_ACK_0072_MAPPING_CALIBRATION.md) прийняв поправки0072; [0074](AgentExchange/0074_CODEX_MAPPING_DESIGN_13_PROBES.md), [Receipt](../mapping-review73-20261025/Receipt.json): повністю відтворено24кроки авторськогоbisect2. Найбільший за cardinality клас не дорівнює найгіршій доступності: після23/24проб одна з73гілок дає39рядків. Заміна24-їY240 наY359 усуває прогалину; backward deletion скорочує набір до **13проб**, зберігаючи40-row window у кожній гілці при margin≥0.25 й projected edges у заявленому домені.

[13-probe draft](../mapping-review73-20261025/Analysis/design-functional-pruned-draft.json): **Y986,491,493,258,489,357,231,233,235,237,256,487,359**.43гіпотетичні класи, максимум107моделей; кожен має≥42послідовні адресовні рядки. Це умовно щодо належності справжньогоmapping до finite1148 та правильності майбутніхlabels, не калібрування чи фізичнийPASS. Не глобально мінімальний набір. Незмінний Core підтвердив14924labels/43гілки/opaque40×40логічні плани;13disjoint guardedpaths/403points/247bodysections. ТимчасовийAPIhost: Release0warnings/0errors, PASS. Старийruntime/продуктнийкод не змінені; новихlabels і runnable-пакета немає.

Single-member аудит дав430…432адресовніlabels, а не430…431. Нові21centeredY0070 не звужують родину. Натомість4ізольованіH0063 були центровані застаримV2 й уже дали570сумісних моделей (post-hoc training,245labels/18послідовних). Для нового дизайну збережені **всі1148**, без довільного вибору member, переносу prefix0/2 уштамп чи зниження0.25. **Фактично поточна повна родина досі обмежена15рядками**;42 — прогноз для майбутніх повних валіднихcalibration-результатів.

## Повторюваність і envelope0072

[0071](AgentExchange/0071_CLAUDE_REVIEW_OFFGRID_LIVE.md) отримано побайтовим оригіналом; [0072](AgentExchange/0072_CODEX_REPEATABILITY_AND_ENVELOPE.md), [Receipt](../offgrid-review71-20261025/Receipt.json): **усі32 before/after кропи16 контрольних префіксів0063/0070 побітово однакові**, цілі PNG різні. У root baseline98.500706% RGB-пікселів однакові, maxΔ3; у внутрішній смузі36 змінених пікселів, обидві блокові резервації тотожні. Обидва live-context/session-editor мають **Rust PID30588**; висновок про незалежний restart не підтверджений. Це finite exact-repeat в записаному контексті, не універсальний детермінізм і не виключення будь-якого випадкового шуму.0/1148 відкидає конкретну floor-родину; залежність від history окремо не встановлена.

На наявних native-кадрах виділено **37 exact-history observed envelopes** та47 prefix-differences (старий/новий контроль і centered). За zero-приростів8/11/13/15 немає newly-strong пікселів у19body-колонках; прихований footprint на вже темному фоні не відновлюється з дельти. У короткихH при commandX580 старт observed RGB-change envelopeX575, правий half-open bound701…703 при endX700. Це виміряні контури для цих трас, не глобальний−5px або Solid. Центрування endpoint-комірок у логічному Planner ще не контролює фізичні кінці.

**Важливе обмеження першої картинки:** за всі1148 mapping і margin≥0.25 доступні166 row-labels, максимум **15 послідовних**, зокрема313…327/Y646…674.40 послідовних немає. Python і **незмінна Release Core DLL** збіглися на881Y/166inverse routes, margin difference0. `TexelPlanner.Plan` прийняв opaque40×15 як15логічнихruns і коректно відхилив opaque40×40. Тимчасовий offline API audit: build0warnings/0errors, PASS; не новий nativeCLI або продуктна збірка. Продуктний код не змінений; automatic exchange зупинений.

## Живий результат0070

[0070 — живий аналіз](AgentExchange/0070_CODEX_OFFGRID_LIVE_ANALYSIS.md), [Receipt](../row-offgrid-live-20261025/Receipt.json): користувач завершив **8 cases / 37 released strokes / 1687 points** у пакеті [row-offgrid1](../../outputs/Pixora_Row_Offgrid_1_20261025/READ_FIRST_UA.md). Поточний стан `DATA_COLLECTED`; історичний `NOT_STARTED` у Build_Manifest не є актуальним статусом спроби.

Збірка `1.0.14-rc.8+local-row-offgrid-1`; Protocol `4df794f0…c3bff`, App `9a0517a2…50b2`, Core `d17036ea…6f05`. **311 файлів** збережені в [Evidence](../row-offgrid-live-20261025/Evidence) та звірені за SHA до/після; 270 immutable payloads і104 RGBA-хеші збігаються. Markers не скидали; оригінали не змінені.

Frozen-v1b результати:

- **Grid CONSISTENT:** усі6H ×2краї мають median(abs(unwrapped residual))≤0.15; найбільша медіана0.085064. Усі7 registered rivals відхилені на4…6H за правилом обидва краї>0.15 на≥3H. Окремий max0.164730 описовий, не прихований і не критерій медіани.
- **Step WITHIN_RESEARCH_BAND:** 2.046919574343px проти2.038497177807px, +0.413167% у frozen±1%. Знаменник14 командних приростів, не незалежно пораховані фізичні рядки; U не встановлена.
- **Increments CONSISTENT_WITH_PHASE_DEPENDENCE:** Centered15 має14/14 `one` (2.0084…2.0895px), без0/2. BoundaryControl16 відтворив4 `zero`,3 `two`,8 `one`; OUT_OF_CLASS0. Це підтримує центрування в межах поточного контексту; **UniqueCauseEstablished=false**, блоки на різнихY і послідовні в часі.

Усі **1123/1123 mandatory sections** валідні до арифметики. Два missing у caps H1/H4 приX700 збережені окремо, не є body. Кожен cumulative-prefix порівнювався з blank stage-before; incremental masks не об'єднувалися. Незалежна Python extraction/math дала max difference **0** від C#. Packaged readonly verify/review exit0; обидва звіти byte-identical із launcher.

**431/431 sampled Cursor readings** дорівнюють Command; issued/actual paths точні, але continuousCursorPathProven=false. DPI96/client2560×1440/origin0/rect465,148…1510,1192/Shape3/Size3/paletteblack, instrumented contour emitter. View1962points/missing0/mismatched14/fixed_view_present, обидва side-registration0px. Fresh/confirmation maxRGB2. Thresholds не змінені.

Описово: шістьH мають0D на обохкраях; товщина mean5.218…5.410px і внутрішній range0.392…0.589px. Фінальні блоки суцільні за обов'язковими перерізами. Це не контракт exact-width/Solid/caps або перенос на інші кольори. Повний статус **MEASURED_NOT_QUALIFIED**, productionEligible=false. У цьому кроці продуктний код/збірка/звичайнийSTART не змінювалися; Native-вводу від агента не було.

## Що вже реалізовано

[0069](AgentExchange/0069_CODEX_OFFGRID_IMPLEMENTED.md), [implementation Receipt](../row-offgrid-implementation68-20261025/Receipt.json): [TexelGrid/Target/Planner](src/CanvasForge.Core/TexelResearch.cs), явний experimental API. Контекст, provenance, domain і вся1148-родина mappings; inverse спільного індексу/margin. **Той самий Core генерує центровані траси досліду й рядки логічного плану.** Всі21 центровані траси нового прогону підтвердили consensus/margin≥0.25 без підбору mapping за after.

Target immutable nearest/alpha; логічні H color-runs із кінцями в центрах стовпців; відмова unsupported/partialalpha. Row edge-label-span2 не є фізичною висотою штампа. EstablishedSolid0/dispatchfalse; звичайнийSTART/UI/defaulttarget не інтегровані. [Приклад24×24](../../outputs/Pixora_Row_Offgrid_1_20261025/Offline_Only_Example24/target24-enlarged.png) і plan — офлайн логічні, не результат фарби Rust.

Попередні перевірки0069: Release0warnings/0errors; **779 Core PASS,244 WPF PASS** із84-layoutDPI;8App groups/11launcher/8package checks. У цьому analysis-кроці їх не запускали повторно. Fake3px — синтетичний, detector не послаблений. Dependencyclosure8.0.31reuse не є cleanWindows certification. Dirty Git state збережений; commit/push не виконані.

## Важливі попередні висновки

- [0063](AgentExchange/0063_CODEX_2D_LIVE_ANALYSIS.md): live2D28/28, mechanismCONSISTENT; дві довгі centeredV мають767body/0D,8/8 фазовихконтрольнихподій matched. Це вузький вертикальний доказ; widthrange≈0.85/0.90px не кваліфікований. Старий16pass-блок мав0/≈2/≈4px; новий контроль0070 повторив цей клас аномалії.
- [0065](AgentExchange/0065_CODEX_ROW_ALIAS_RECONCILIATION.md): rowV2≈525 відкликаний як масштаб через Y≡60(mod80). Frozen observation-кандидатdc3c5681…2e77: S0.479309/pole57612.9645/psiTop−0.427568/psiBottom+0.150375. PhysicalStampRows/cursorMapping/U не доведені. Нові6H0070 підтримали кандидата проти зареєстрованих rivals без ретюнінгу, але не довели native texture resolution або єдину модель.
- [0067](AgentExchange/0067_CODEX_ROW_JOINT_DESIGN.md): joint6 замість неможливого all-rival0.25 на кожнійY; margin0.25 збережений. Frozen-v1b median-пропозиція0068 прийнята **до** нового збору. Старі використані training/held-out не називати untouched; 37 нових штрихів тепер теж consumed.
- Старі rowV2 GRID INCONCLUSIVE і texel2 INCOMPLETE зберігаються. Жоден новий результат не переписує історичний вердикт.

## Наступна дія

Row_Picture_3 виконано в копії [Try4](../../outputs/Pixora_Row_Picture_3_20261026_Try4/READ_FIRST_UA.md) ([0082](AgentExchange/0082_CLAUDE_LOCAL_PICTURE3_RESULT_AND_SPLIT.md)): DATA_COLLECTED 83/83.
- Контролі 4/4 CONFIRMED, зокрема рядок 227.
- Тіло й отвори 0/0, D-подій 0; H ≤ 0.10, V-старт ≤ 0.04.
- **Кінець перекомпенсовано:** −1 px зсунув край на ≈1 стовпець, медіани −0.34…−0.42, 2/3 поза 0.35.
- top-r227 знову insufficient_contrast, тому картинка UNMEASURED.
- Три попередні спроби зупинилися до фарби: вікно таймера користувача перекривало панель «ЗНАРЯДДЯ».

Роботу поділено ([0082](AgentExchange/0082_CLAUDE_LOCAL_PICTURE3_RESULT_AND_SPLIT.md)), кожен агент редагує лише свій рядок:
- **Claude:** pic9 **PREDICTIONS_MET** ([0098](AgentExchange/0098_CLAUDE_LOCAL_PICTURE9_RESULT.md)). Продуктовий маршрут v2 за рішенням користувача реалізував Claude ([0099](AgentExchange/0099_CLAUDE_LOCAL_TEXEL_PRODUCT_V2.md)): кінець b+1.867, межі стовпців 75–459 / рядків 198–537 зі спільною адресацією, `--texel-product-review`, `--data-folder`. Готовий живий тест [Pixora_Texel_Product_1_20261030](../../outputs/Pixora_Texel_Product_1_20261030/READ_FIRST_UA.md), NOT_STARTED.
- **Codex:** продуктовий трек перевірено й доведено Claude ([0084](AgentExchange/0084_CLAUDE_LOCAL_CODEX_TRACK_REVIEW_AND_PICTURE4_PACKAGE.md)): Core-планувальник і прапорець OFF, `TexelProduct` дає рівно штрихи pic3/pic2, виправлено WPF-локалізацію, додано App-тест. Живий тест продукту відкладено до результату pic4 (спільний конфіг, `Max Speed`≠`Rapid`). **Наступне для Codex (0092):** у `TexelCompensatedPlanner` замінити `endTrimPx` правилом φ ≥ b+1.5, оракул `RowPicture6/protocol-pic6.json`.

## Постійні межі

Точний user-selected rect із дозволеним запасом; RGB12/contrast80 та16ROI/correlation0.985/runtime delta0 не послаблювати. Runtime-відмова не навчаєreference. Невідомийfocus/tool/cursor забороняєdown; утримуване відпускається. Target незалежний відspeed/route. Q/jitter закрито22.10; сліпі0.996/1.0037, dot-sweep широкихпензлів, ±2інтерполяція й статичнікольоровіoffset не є виправленням.

Автоматичний обмін зClaude зупинений22.10.0071 підтвердив ручне прочитання0070;0073 —0072; exact documentSHA не передані.0069/0074 локальні, доставка не заявлена. [INDEX](AgentExchange/INDEX.json) зберігає авторство й межі перевірки. Вкладенийreview не є дозволом на додатковий Native-ввід. Фонового моніторингу або автоматичного продовження немає.
