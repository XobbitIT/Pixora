# Pixora — постійна матриця перевірок

Оновлено: 25.10.2026. Перевіряти спостережуваний результат. Окремі вердикти: алгоритм/target, безпека вводу, готовність Release-пакета, фізична якість Rust, продуктивність. Один PASS не замінює інші.

Проєкт окремих фізичних метрик і запропонованих меж Standard: [контракт v1](docs/quality/PHYSICAL_QUALITY_CONTRACT.md). Стан `DRAFT_PROPOSED_LIMITS`: він не сертифікує старі матеріали й не замінює незаморожені observable/uncertainty/color та acceptance matrix. Q/jitter закрито як непідтверджений напрям коректора за прямим рішенням користувача 22.10; повторення незмінених обчислень не є наступною перевіркою якості.

## Автоматичний набір зображень і сценаріїв

Постійні входи вже задані детермінованими `PixelImage` fixtures і збереженими golden/raw файлами в Core tests. Це частина автоматичного прогону, а не вимога знову малювати їх вручну. Нові файлові PNG-еталони додавати до цього набору з manifest і provenance, без паралельного дублюючого runner.

| Сценарій | Реалізований еталон / перевірка | Умова регресії |
| --- | --- | --- |
| Дрібні деталі та кольорові межі | [TargetRecipeChecks.cs](tests/CanvasForge.Core.Tests/TargetRecipeChecks.cs): кольорова сітка, 128 execution combinations; [StandardQualityChecks.cs](tests/CanvasForge.Core.Tests/StandardQualityChecks.cs) | Target/recipe не змінюються через speed, route чи UI; явні зміни деталізації/кольорів мають явний результат. |
| Великі заливки | [Program.cs](tests/CanvasForge.Core.Tests/Program.cs): large noisy image, preview/stroke reproduction; [beta50 goldens](tests/CanvasForge.Core.Tests/Fixtures/beta50-measured-plan-goldens.json) | Немає пропущених потрібних клітин або виходу за дозволену область; оцінювати allocations і тривалість окремо. |
| Прозорість і чорний колір | TargetRecipeChecks: opaque checkerboard/black, реальні alpha-пікселі; Program.cs: tiny/thin/fully transparent | Непрозорий чорний лишається фарбою; прозорі клітини пропускаються за заданим alpha-контрактом. |
| HEX та палітра Rust | Program.cs: source RGB для HEX, quick colors для palette; TargetRecipeChecks | HEX не залежить від випадкової Rust palette; palette використовує захоплені точки, зміна кольору підтверджується. |
| Тонкі H/V лінії, кінці й стики | [StandardQualityChecks.cs](tests/CanvasForge.Core.Tests/StandardQualityChecks.cs), [StandardMotionChecks.cs](tests/CanvasForge.Core.Tests/StandardMotionChecks.cs), StandardBoundaryChecks / StandardBandResponseChecks | Щільність команд, bounds, точний порядок down/up, невідомі історії відхиляються. Фізичні рівність/ширина потребують native evidence. |
| Математика фізичних метрик | [StandardPhysicalQualityChecks.cs](tests/CanvasForge.Core.Tests/StandardPhysicalQualityChecks.cs): 39 математичних/граничних сценаріїв; входить у full Core і `--standard-quality-only` | Зсув/нахил/ширина/останній рядок не ховаються; pooled/row P95 розділені; `CompareLineMetric` блокує неповну лінію, невідому/несумісну U та provenance. Immutable results/зовнішній compiler-negative блокують `with` bypass; 500×7 bounded perturbations перевіряють k·e; лінійна сума U. Caps — L∞-інтервал, фізичний verdict завжди Incomplete до evidence policy. Це не Physical-quality PASS. |
| Raw RGB12-mask | [StandardPaintMaskChecks.cs](tests/CanvasForge.Core.Tests/StandardPaintMaskChecks.cs): 10 сценаріїв; full Core та focused quality | H/V/reverse, RGB12/13, contrast80/81, missing/holes/clipping, alpha, negative origin та snapshots. Native replay 10426 рядків проти незалежного Python; raw mask не отримує Solid/eligibility. |
| UV C3′ diagnostic B | [StandardUvPilotChecks.cs](tests/CanvasForge.App.Tests/StandardUvPilotChecks.cs): `App.Tests NEW_OUTPUT --uv-only FROZEN_FIXTURES` |12×901 paths exact із0027,16 golden evaluations0029, threshold/sign/mixed/missing checks;3 groups/12 pairs/24-edge report; protocol/settings/binary/control no-go; portable assets; post-paint failure/residual markers block retry; own fresh session bound to protocol/PID/DPI. Fake/model PASS не є native або quality PASS. |
| Forward observation двох країв | [StandardStrokeObservationChecks.cs](tests/CanvasForge.Core.Tests/StandardStrokeObservationChecks.cs):13 scenarios, full Core та `--standard-quality-only`; [offline replay у test host](tests/CanvasForge.Core.Tests/StandardStrokeObservationReplay.cs) | Різкі/розподілені переходи, H/V/reverse, independent edges/width, caps, known-empty vs unknown support, missing-first, fragmented contrast, context/direction binding, immutable snapshots. Повні residuals лише коли всі потрібні перерізи наявні. Native replay всіх8 frozen hypotheses ×2 явних kernels ×12 lines: full scope INCOMPLETE, interior окремо post-run; no fit/phase selection/production dispatch. Прогноз із заданих геометричних меж не є ідентифікованою фізичною моделлю I/E/T. |
| Кандидат тексельних вертикалей0039 | [StandardTexelColumnChecks.cs](tests/CanvasForge.Core.Tests/StandardTexelColumnChecks.cs), [незалежний JSON](tests/CanvasForge.Core.Tests/Fixtures/Texel39_Paths.json); full Core / `--standard-quality-only` |7 scenarios:4505 authored path points,10-path separation/active controls, context/shape/colour/timing/emitter refusal, nonfinite/singular/overflow/domain/margin, immutable IDs, round-to-even. Offline test-host `--texel-column-vectors NEW_JSON` exports9010points/8210projected sections for independent comparison; не native collection CLI чи production profile. Контрольні дані не є доказом нового малювання. |
| Runnable2D candidate | [StandardPlanePilotChecks.cs](tests/CanvasForge.App.Tests/StandardPlanePilotChecks.cs), `App.Tests NEW_OUTPUT --plane-only`; [PlaneLauncher.Tests.ps1](tests/Tools/PlaneLauncher.Tests.ps1) |7groups: independent all4538points/13cases28tracks, exact contexts/no-go/disjointreservations, rehashed policy/model/body mutation, phase020 bounds/sign/one-to-one/nonvacuity/shift>15, both-edge mechanism and incomplete-before-arithmetic, full28-pair fake pipeline/missing blockpair, failure/release/no-retry/Unicode/tamper.11launcher cases; packaged init/preflight/review-incomplete/check/portable/cancel. Fakes not native qualification; user package statusNOT_STARTED. |
| Спільна row/column математична2D-адресація | [StandardTexelPlaneChecks.cs](tests/CanvasForge.Core.Tests/StandardTexelPlaneChecks.cs), [Python oracle](tests/CanvasForge.Core.Tests/Fixtures/Texel2D_Vectors.json); full Core / `--standard-quality-only` |9groups:25independentpoints/margins,141351address roundtrips/minmargins, frozenV2 rowindices/projectededges; H/V/serpentine neighbourhood/gaps; DPI/rect/origin/Size/Shape/colour/timing/emitter/binary refusal; singular/nonfinite/overflow/domain; matching axes/common-domain/provenance; separate-axis margins; detached snapshots/IDs. Ще3groups:10повних незалежних dense path SHA/reverse, кожен along pixel, perpendicular margin/очікувані along-переходи, formula-vs-endpoint interpolation, неможливий .35 без пропусків, domain/context/immutability. Усього12groups; PASS не сертифікує Native transport, width/Solid/overlap/caps/U або новий Rust-run. |
| Runnable texel-pilot | [StandardTexelPilotChecks.cs](tests/CanvasForge.App.Tests/StandardTexelPilotChecks.cs), `App.Tests NEW_OUTPUT --texel-only FIXTURES`; [launcher cases](tests/Tools/TexelLauncher.Tests.ps1) |7App groups: vectors/forecasts,10releasedpairs,9010sections/body-cap separation, both-edge verdicts, mandatory/missing/sign/one-to-one/controlprecision-recall, context/hashes/no-go, fresh-view1962support/zero-registration/shift/missing/ambiguity, failures/no-retry/portable.7WindowsPowerShell cases cover EOF/cancel/failures and Check. Test-host `NEW_OUTPUT --texel-replay CSV FULL_FRAME` repeats detector/view from saved native data. Fake/replay≠новий Rust чи qualityPASS. |
| Texel startup saved-frame regression | [StandardTexelViewChecks.cs](tests/CanvasForge.App.Tests/StandardTexelViewChecks.cs), `App.Tests NEW_OUTPUT --texel-view-replay REF_NATIVE OBS_NATIVE` | Real no-paint failure:2near-tied argmax changes,1962intact supports;12 native-image shifts±1/2/3px onX/Y reject;local5-row deformation rejects;brightness+2 accepts. Python checks1962support+98offsets/4120numbers. Synthetic flat ambiguity/missing reject. Replay is not a physical quality/live PASS. |
| Adaptive, форми та розміри | [Rc4AdaptiveChecks.cs](tests/CanvasForge.Core.Tests/Rc4AdaptiveChecks.cs), [recorded fixtures](tests/CanvasForge.Core.Tests/Fixtures/README.md) | Асиметрія/повні маски зберігаються; stale/відхилений профіль не отримує придатність; без доказу — чинний fallback. |
| Visual Size / console failure | [VisualSizeEntryChecks.cs](tests/CanvasForge.App.Tests/VisualSizeEntryChecks.cs), `App.Tests NEW_OUTPUT --visual-size-only FAILED_ATTEMPT` | 5groups: correctSize/two reads/zero edit; no policy downgrade; numeric key payload + after-write confirmation; unknown/layout/unresponsive rejects; guarded key-up/cancel/invalid payload; console completion/originalReason without modal; saved-native Size3 parser replay без userclipboard/Rustinput. FullWPF regression потрібна після зміни спільного Painter. |
| Горизонтальна row-axis B | [StandardRowPilotChecks.cs](tests/CanvasForge.App.Tests/StandardRowPilotChecks.cs), `App.Tests NEW_OUTPUT --row-only`; [launcher](tests/Tools/RowLauncher.Tests.ps1) | 5 groups:12×911 physical H paths,8/4 immutable split, disjoint search-support reservations; context/no-go/freeze/no overwrite; both-edge width/event diagnostics; missing/NaN/order before arithmetic, held-out extraction refusal;12-pair fake collection,7288training sections, missing held-out capture blocks review; post-paint failure/release/no retry, Unicode portable/reference tamper.11 WindowsPS fixture cases: completion, Q/EOF, preflight/run/review failures, Check, retained completed/failed/unknown/startup-flat без повторного paint з originalReason. Packaged preflight/INCOMPLETE-before-live/portable with invalid DOTNET_ROOT tested separately. Saved-native `NEW_OUTPUT --row-startup-replay FAILED_ATTEMPT` відтворює archivedRGB failure й freshPASS; singlePixel/fullCanvasRGB13 та shiftedview1px лишаються negatives. Нуль D-events не є B PASS. |

Golden оновлювати лише після пояснення причини й diff очікуваної поведінки. Очікуваний результат не генерувати автоматично з того самого зміненого коду як доказ його правильності. Для native evidence зберігати raw before/after, геометрію, origin/DPI, контекст вводу, timestamps, SHA256, пропуски та незавершені спроби.

## Безпека та стійкість

| Ризик | Автоматична перевірка | Підстава для додаткового live-тесту |
| --- | --- | --- |
| Фокус, F6/ESC, tool, cursor API, неправильні координати | Core safety checks, [Rc7SafetyChecks.cs](tests/CanvasForge.Core.Tests/Rc7SafetyChecks.cs), WPF fake input boundaries | Зміна відповідного transport/guard; старі F6/Alt+Tab тести не повторювати заради кожного патча зображення. |
| Зміна rect, DPI, resolution, foreground, недоступний capture | Негативні geometry/editor fixtures, 84-layout WPF matrix, [FrontierSessionCaptureChecks.cs](tests/CanvasForge.App.Tests/FrontierSessionCaptureChecks.cs) | Нова геометрія/захоплення, яку неможливо відтворити fixture. Змінений rect/tool не дозволяє новий paint-down. |
| Зависання Rust, затримана реакція, переривання/падіння | Timeout/cancellation/failure injection, release у шляхах виходу, збережений INCOMPLETE | Поведінка ОС/гри, не відтворювана fake. Це не вже підтверджений живий PASS. |
| Застаріле збереження, одночасні екземпляри, пошкоджений config | [ConfigOwnershipChecks.cs](tests/CanvasForge.Core.Tests/ConfigOwnershipChecks.cs): compare-and-save, revision/hash, lease, import | Нова взаємодія реальних UI-процесів. Дані користувача не скидати для обходу конфлікту. |
| Збій сценарію збору | [FrontierWorkflow.Tests.ps1](tests/Tools/FrontierWorkflow.Tests.ps1), [FrontierLauncher.Tests.ps1](tests/Tools/FrontierLauncher.Tests.ps1) | Новий шлях запуску. Partial не запускається повторно; неповна перша умова блокує другу. |

## Опційний реальний capture-control

[NativeCaptureControlChecks.cs](tests/CanvasForge.App.Tests/NativeCaptureControlChecks.cs) запускається окремо через `App.Tests <new-output> --native-capture-control`, до WPF Application. Коротко показує власне nonactivating GDI вікно з відомим opaque source, викликає production Native.Screenshot; не Rust і не SendInput. Очікується exact RGBA всіх пікселів/чотирьох меж і невідповідність oracle зі зсувом1px; focus не змінюється. Вікно/resources очищуються у finally. Не включати цей desktop-dependent test до звичайного full suite. PASS при96DPI засвідчує лише записаний monitor/session/capture scope, не якість фарби або інші DPI. Source/обидва кадри, QPC та receipt зберігаються.

## Release та продуктивність

- Перевіряти саме DLL з готового runtime: source/build fingerprint, залежності, версію, запуск, settings round-trip, recovery, відсутню залежність, extraction/preflight у шляху з Unicode/пробілами. При ізольованому clean-Windows тесті записувати версію ОС/DPI і наявність SDK/runtime; звичайний ПК розробника не називати чистим середовищем.
- CI описаний у [.github/workflows/build-windows.yml](.github/workflows/build-windows.yml): локалізація, Core/WPF, self-contained publish, перевірка опублікованих DLL і пакетів. Наявність workflow не доводить успішного CI-прогону; потрібні логи конкретного запуску.
- Baseline продуктивності: точні input/recipe/settings hashes, build, ОС/CPU/RAM/DPI, число повторів; planning/capture/paint Active/Wall, Colors/Motion/Service/Finish/Pause/Countdown, CPU time, peak working set та allocations. Вкладені витрати не додавати вдруге; див. [ExclusiveTimingChecks.cs](tests/CanvasForge.Core.Tests/ExclusiveTimingChecks.cs).
- Порівнювати до/після на однакових входах і середовищі, з прогрівом і повтореннями. Показувати розкид і фактичні відмови. Оптимізація приймається лише без регресії якості/безпеки; прийнятний бюджет фіксувати до оцінювання, без вигаданого універсального CPU/RAM чи ETA-порога.
- Поточний CPU/RAM baseline та незалежна фізична кваліфікація не закриті. Статуси й останні результати — у [PROJECT_STATE.md](PROJECT_STATE.md).

## Відтворення локального автоматичного прогону

PowerShell 7, Windows, .NET 8 SDK; виконувати з кореня репозиторію. За відсутності restore спочатку відновити залежності звичайним способом. Збірки послідовні; не перезаписувати файли runtime, який зараз працює.

```powershell
$verificationRoot = Join-Path $env:TEMP ('Pixora-Checks-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $verificationRoot | Out-Null
$buildRoot = Join-Path $verificationRoot 'built'
$baseOutput = $buildRoot + [IO.Path]::DirectorySeparatorChar
dotnet build tests/CanvasForge.Core.Tests/CanvasForge.Core.Tests.csproj -c Release -m:1 -nr:false "-p:BaseOutputPath=$baseOutput"
if ($LASTEXITCODE -ne 0) { throw 'Core build failed' }
dotnet build tests/CanvasForge.App.Tests/CanvasForge.App.Tests.csproj -c Release -m:1 -nr:false "-p:BaseOutputPath=$baseOutput"
if ($LASTEXITCODE -ne 0) { throw 'App build failed' }
& (Join-Path $buildRoot 'Release/net8.0/CanvasForge.Core.Tests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed' }
& (Join-Path $buildRoot 'Release/net8.0-windows/CanvasForge.App.Tests.exe') (Join-Path $verificationRoot 'wpf')
if ($LASTEXITCODE -ne 0) { throw 'WPF tests failed' }
```

Точкові групи запускати раніше за повний набір; після PASS не повторювати широкий прогін без нової причини. CLI збору `run`/`run-session` у цей офлайн-блок **не входить**. Детальні результати й commands зберігати в receipt; тут підтримувати матрицю, а не безмежний журнал.

## Row-offgrid v1b та experimental Core

[Контракт](docs/quality/ROW_OFFGRID_PILOT_V1B.md). Core `--texel-grid-only NEW_OUTPUT` перевіряє37точнихCore-трас/1687points, контекст/provenance/gauge/family consensus, непідтриманіrows/margins, immutable resampling/alpha/логічніruns; формує лише offline24×24target/plan. App host `NEW_OUTPUT --offgrid-only`: frozenassets/manifest, симетричнаmedian(abs) зmax/P95окремо, цілийrowshift, rivals/outliers,0→2приправильномуendpointmean,1123mandatory-before-arithmetic,37pairs/failedaftercapture/disconnected/portability/tamper. Output має бути новоюfixture-папкою.

`tests/Tools/OffgridLauncher.Tests.ps1` запускає filesystem-only stub:11сценаріїв preflight/run/review/EOF/cancel/retained originalReason/Unicodepaths. Packaged CLIcheck додатково перевіряє missingruntime, preliveINCOMPLETE, EOF безattempt і readonly byte-identical replay старогоPlane1. Ці offline/fake перевірки не є новими nativeвимірами.

Для загальних тестів ізsettings/checkpoint/atomicrename у sandbox задаватиTEMP/TMP на нову тестову папку всередині дозволеногоworkspace. MSBuild `-m:1 -nr:false`; не змінювати продуктову атомарність збереження через обмеження зовнішньогоTemp. Живий сценарій37штрихів виконує користувач окремо після готовностіRust; агент не включає його в offline-тести.

## Row-calib v1 (калібрування рядків + картинка40×15)

[Контракт](docs/quality/ROW_CALIB_PILOT_V1.md). App host `NEW_OUTPUT --calib-only` (8груп): Core-траси16проб і24runs картинки проти незалежногоPython-оракула, мітки всіх1148 членів проти оракула таCore `AuditRow`, хеш таблиці уточненої родини28663; manifest/provenance/no-overwrite/tamper; правило мітки (чиста/неоднозначна/лише top/викид/MODEL_CONTRADICTED); вичерпний фільтр обох родин, Core-аудит ≥ frozen-прогнозу і40-рядковий план похідної гілки; 304 перерізи до арифметики; метрики картинки (ідеал/розрив/сходинка/поза резервацією); fake-pipeline40пар і відсутній останній штрих картинки; збій після фарби, переносність. `tests/Tools/CalibLauncher.Tests.ps1`: 11 сценаріїв launcher на filesystem-only stub. Fake3px не є моделлю Rust: його мітки чесно відхиляються як INCOMPLETE.

## Row-picture v2 (контролі похідної родини + картинки з компенсацією)

[Контракт](docs/quality/ROW_PICTURE_PILOT_V2.md). App host `NEW_OUTPUT --pic2-only` (6груп): Core-контролі/плани з компенсацією/геометрія оцінки протиPython-оракула, похідна родина1196 і model; freeze/tamper; вердикт контролів (confirmed/contradicted/ambiguous/incomplete); ідеальна texel-точна картинка, розрив, фарба в отворі, сходинка краю; fake-pipeline106пар і відсутній останній штрих; збій після фарби, переносність. `CALIB_EXPERIMENT --pic2-replay-calib NEW_FILE` — read-only replay метрики наcalib1-кадрах. `tests/Tools/PictureLauncher.Tests.ps1`: 11 сценаріїв.

## Row-picture v3

[Контракт](docs/quality/ROW_PICTURE_PILOT_V3.md). `NEW_OUTPUT --pic3-only` (3групи): 4контролі/83штрихи, кінець −1px проти pic2-плану, 14країв із зовнішніми, відмова короткого run і чужих ассетів; ідеальна картинка + відхилення перебігу 0.6стовпця під допуском 0.35; fake-pipeline і прив'язка revision. Регресія: `--pic2-only` і review реальних даних pic2 новим кодом (ідентичний result). `tests/Tools/Picture3Launcher.Tests.ps1`: 11 сценаріїв.

## Row-picture v4 і тексельний продуктовий маршрут

[Контракт v4](docs/quality/ROW_PICTURE_PILOT_V4.md). `NEW_OUTPUT --pic4-only` (4 групи):
- 4 контролі pic3 / 24 штрихи; endLR −3…+1 px і startRL −2…+2 px проти Python-оракула; offset −1 = рядок pic3;
- синтетичні краї з відомим залишком: квантований кінець → `END_NO_EXACT_STATE`, чистий offset → `END_EXACT_AT`, один чистий повтор недостатній, зворотний старт 0/зсунутий/брудний, пропущений штрих → `UNMEASURED`;
- fake-pipeline і прив'язка revision;
- причина відмови редактора називає рамку чи пензель.

`--texel-product-only`: `TexelProduct.Build` на цілі 40×40 дає рівно штрихи pic3 (trim 1) і pic2 (trim 0); колір, напівпрозорість, >40×40 відхиляються; вимкнений прапорець повертає звичайний Planner. `--texel-product-frames BEFORE AFTER` оцінює кадри правилами pic3 (на кадрах pic3 Try4 відтворює result побітово). `tests/Tools/Picture4Launcher.Tests.ps1`: 11 сценаріїв.

## Row-picture v5 (квадратний пензель)

[Контракт](docs/quality/ROW_PICTURE_PILOT_V5.md). `NEW_OUTPUT --pic5-only` (3 групи):
- 4 контролі / 24 штрихи; endLR побайтово = pic4, endLR2 на стовпцях 290…329; 4 верхні й 4 нижні зразки на штрих; pic4 без H-зразків;
- правило «кожен зразок» відкидає півтексельний рядок кепа (медіана його приховала б);
- fake-pipeline; налаштування з круглим пензлем під протоколом pic5 відхиляються саме перевіркою форми.

`--editor-proof-frame SETTINGS PNG`: офлайн-перевірка рамки й пензля на знімку (еталон і кадр pic4 → OK, браузер → відмова, квадрат ■ → OK). `tests/Tools/Picture5Launcher.Tests.ps1`: 11 сценаріїв.
- v5b: `--pic5-only` також перевіряє, що `DrawingWorkflow.Effective` скидає Standard на Round, а `Painter.ApplyResearchShape` повертає квадрат. Налаштування без `research_brush_shape_slot` (як у першій спробі) відхиляються preflight-ом саме перевіркою форми.

## Row-picture v6 (правий край «повне покриття»)

[Контракт](docs/quality/ROW_PICTURE_PILOT_V6.md). `NEW_OUTPUT --pic6-only` (3 групи):
- штрихи = старти pic2, кінець — перший піксель із φ ≥ b+1.5 (C# = Python-оракул); верхні H-краї на 1 px глибше; pic3 без змін;
- вікно V-кінця [−0.15, +0.75]: точний і «повне покриття» приймаються, «коротше» (−0.42) і перелив (+1.1) — ні;
- fake-pipeline.

`tests/Tools/Picture6Launcher.Tests.ps1`: 11 сценаріїв.

## Row-picture v7 (нові цілі й стовпці)

[Контракт](docs/quality/ROW_PICTURE_PILOT_V7.md). `NEW_OUTPUT --pic7-only` (2 групи): 151 штрих, A 40×40 на стовпцях 300…339 і B 24×24 на 365…388; кожен run за правилом pic6 (старт centre(a+2), кінець φ ≥ b+1.5); смужка 3 стовпці дає штрих ≥ 2 точок; fake-pipeline 34+32 країв. `tests/Tools/Picture7Launcher.Tests.ps1`: 11 сценаріїв.

## Row-picture v8 (кінець b+1.867, V-краї в сітці текселів)

[Контракт](docs/quality/ROW_PICTURE_PILOT_V8.md). `NEW_OUTPUT --pic8-only` (2 групи): 151 штрих на цілях pic7; кожен run — старт centre(a+2), кінець перший піксель із фазою курсора ≥ b+1.867; від pic7 відрізняються лише кінці (107/147 run +1 px), контролі й геометрія оцінки ті самі; `GridPhase` = фаза курсора без члена Nβ(x−anchor)/W; fake-pipeline 34+32 країв і правило в summary. Повторний `pic7-review`/`pic6-review` нативних даних новою збіркою дає ідентичний result. `tests/Tools/Picture8Launcher.Tests.ps1`: 11 сценаріїв.

## Row-picture v9 (правила pic8 в інших місцях полотна)

[Контракт](docs/quality/ROW_PICTURE_PILOT_V9.md). `NEW_OUTPUT --pic9-only` (2 групи): 9 кейсів/260 штрихів; R 40×40 на 420…459×203…242, L 40×40 на 75…114×493…532, C 24×24 на 240…263×452…475; кожен шлях, резервація, зразок оцінки й контроль проти незалежного Python-оракула (`row-picture9-implementation-20261029/oracle.py`, самоперевірка відтворює pic8); правило pic8; спільна адресація всіх рядків і Ctl5/Ctl6 із запасом 0.25; нижній край L у межах області рядків; fake-pipeline 34+34+32 країв. Повторний `pic8-review`/`pic7-review` нативних даних ідентичний. `tests/Tools/Picture9Launcher.Tests.ps1`: 11 сценаріїв.

## Продуктовий тексельний маршрут v2 (кінець b+1.867)

Core: `Compensated Core full-coverage end equals frozen Picture8/9 runs` — `TexelCompensatedPlanner.Plan(..., endPhase: 1.867)` побайтово дає runs pic8 (A, B) і pic9 (R, L, C); trim-маршрути pic2/pic3 без змін; кінець із trim і фази поза [1.5, 2.5) відхиляються. App `NEW_OUTPUT --texel-product-only` (2 групи): `TexelProduct.Build` дає рівно runs pic8/pic9 на їхніх стовпцях і рядках; legacy trim ігнорується; відмови для кольору, напівпрозорості, >40×40, стовпців поза 75–459, рядків поза 198–537 і рядків без спільної адресації (повідомлення називає рядок); `--texel-product-review` читає last-run у форматі Painter: INCOMPLETE без кадрів або за зміненого маршруту, без перезапису. Пакетна перевірка: нативні кадри pic8 A у форматі last-run → `Pixora.exe --texel-product-review` дає PREDICTIONS_MET з ідентичними краями.

## Калібрування сесії 1 (етапи 1+2, основний конфіг)

App `NEW_OUTPUT --session1-only` (3 групи): етап 0 (`TexelSessionAudit`) на копії конфігу з rect 465/151/1509/1192 і Max Speed; оригінал конфігу байтово незмінний; контролі 203/465/515 побайтово = pic9 Ctl1/Ctl6/Ctl5 на перенесеній сітці із запасом 0.20; Rebind не змінює коефіцієнти/родину/домени; відмови: rect 148, DPI, origin, кадр 30 мс, Rapid, увімкнений тексельний START, без структури, без чорного, змінений протокол, редагований settings. Правила: точна проєкція → CONFIRMED; інший рядок, залишок ≈0.2, зміна поза резерваціями → NOT_CONFIRMED; бракує перерізу/порівняння/збій прогону → INCOMPLETE до арифметики. Fake-pipeline 3 штрихи/6 кадрів/57 перерізів, binding-запис, відмова повтору, відсутній кадр → INCOMPLETE. `tests/Tools/Session1Launcher.Tests.ps1`: 11 сценаріїв.
