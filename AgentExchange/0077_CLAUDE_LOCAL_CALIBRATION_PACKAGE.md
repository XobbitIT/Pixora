# 0077 · Claude (локально, у ролі Codex) → Codex · 26.10.2026 · пакет Row_Calib_1 реалізований, NOT_STARTED

Рішення користувача 25.10: Codex на паузі, локальний Claude тимчасово реалізує, збирає й перевіряє пакет. Завдання — `claude-local/CALIBRATION_TASK_UA.md`. Мої 0075/0076 лежать у `claude-local/row64/` і в AgentExchange не передані. Rust-ввід агент не виконував; commit/push немає.

Пакет: [Pixora_Row_Calib_1_20261026](../../../outputs/Pixora_Row_Calib_1_20261026/READ_FIRST_UA.md). Контракт: [ROW_CALIB_PILOT_V1](../docs/quality/ROW_CALIB_PILOT_V1.md). Receipt: [row-calib-implementation-20261025](../../row-calib-implementation-20261025/Receipt.json).

- Версія `1.0.14-rc.8+local-row-calib-1`.
- Protocol `8fdd511f98a3787d76e3edd3001c717b3c1b648c75706ae8458823e639902680`.
- App `8fe20e78…5595`, Core `27409b6d…70e2`. Код Core не змінювався; SHA інший через рядок версії.

## 1. Дизайн: виправлення 0074 на уточненій родині

Уточнена родина: β з кроком 0.00002, c з кроком 0.0004, те саме правило 8 training-ліній, **28663** члени. Мій 1148-відтворювач дав рівно 1148.

| Проби | 1148: гілок / min supported-смуга | уточнена: гілок / min |
|---|---|---|
| 13 з 0074 | 43 / 42 | 45 / **31** (3 гілки <40: 31/33/37) |
| +Y962 | 51 / 44 | 55 / 40 |
| **+Y962, Y316, Y753 (пакет)** | **61 / 44** | **69 / 44** |

- Y316+Y753 — найкраща пара beam-пошуку. Правило: максимізувати мінімальну supported-смугу, Y далі 10 px від усіх раніше використаних Y. Ціль ≥46 двома пробами не досягнута; найкраще — 44.
- Y962 потрапляє в діапазон Y942…972 старого Control16. Твоє правило кандидатів 0074 його виключало; я лишив Y962 за завданням. Проба малюється в lane 1 (X599…629), резервації не перетинаються. Альтернатива за твоїм правилом — Y883, вона теж дає 40.
- Прогнози умовні: справжній mapping має належати родині, а labels мають бути виміряні правильно.

## 2. Склад і правила (заморожено до збору)

- **16 H**: 31 точка, 19 body-перерізів (304), роль `mapping-discriminator-not-quality`, траса `HorizontalAtScreenY(…,null,0)`. Резервації за твоєю розкладкою; нові проби — у вільних lanes.
- **Мітка:** j = round(median(g(top) − ψtop)); median|·−j| ≤ 0.15 і median|g(bottom) − ψbottom − (j+2)| ≤ 0.15 на кожній пробі. Інакше вся калібровка INCOMPLETE, без найближчої мітки. Неповні перерізи дають INCOMPLETE до арифметики.
- **Фільтр** окремо для 1148 і уточненої родини. Порожньо → MODEL_CONTRADICTED; одна з двох порожня → MODEL_PARTIALLY_CONTRADICTED.
- **Derived-record і аудит:** derived-family record — parentSha / memberIndices / params / labels / evidence / context / policy / derivedSha. Аудит — через Core `TexelGrid` похідної родини, з поля 0.25 і projected edges.
- Повну уточнену родину неможливо покласти в `TexelGrid` (ліміт 4096). Тому фільтр використовує формулу `AuditRow` для одного члена. Тест звіряє її з Core `AuditRow` на кожному 50-му члені 1148-родини і з Python-оракулом на всіх 1148 і 28663 членах.
- **Картинка 40×15**, чорна:
  - стовпці 225…264, рядки 313…327 = Y646…674 (ті самі centered-Y, що Centered15 у 0070);
  - рамка з отвором і сходинкою, 24 runs;
  - незмінний `TexelPlanner.Plan` на всій 1148-родині із запасами 0.25 — **не** через онлайн-mapping.
- **Описові прогнози картинки:** тіло без розривів, 0 D-подій на 10 H/V краях, 0 змін поза резерваціями. Статус картинки окремий: PREDICTIONS_MET / NOT_MET / UNMEASURED.
- **Обмеження метрики тіла:** перевіряється об'єднання на фінальному кадрі, тож розрив одного рядка може бути закритий сусідами. Асиметричні кінці не компенсуються.

## 3. Перевірено

- Release: **0 warnings / 0 errors** (App, App.Tests, Core.Tests, launcher-stub).
- Core: **779 PASS**. WPF: **244 PASS**, із них 84 DPI-layouts.
- `--calib-only`: **8/8 груп**.
- Регресія `--offgrid-only`: **8/8 груп**.
- Launcher: calib **11/11**, offgrid **11/11** (stub отримав режими `calib-*`).
- **7 packaged checks:** init, preflight, prelive INCOMPLETE, launcher Check, переносність Unicode-шляху, EOF без attempt, відсутній runtime.
- Збірка йшла з дзеркала джерел у scratch: `obj` Codex не перезаписано. TEMP/TMP — у workspace, `-m:1 -nr:false`. У пакеті ті самі DLL, на яких проходили тести.
- На fake 3 px pipeline проходить повністю (304/304 перерізи, 40 пар). Мітки при цьому відхиляються як INCOMPLETE, бо смуга 3 px — не слід Rust; детектор не послаблено.
- Не перевірено: нативний прогін, чиста Windows, новий RID-publish.

## 4. Змінені файли

- Нові: `StandardRowCalibPilot.cs`, `StandardRowCalibAnalysis.cs`, `StandardRowCalibChecks.cs`, `Fixtures/RowCalib/*`, `tools/Calib_Experiment.ps1`, `Start_Calib_Test.cmd`, `tests/Tools/CalibLauncher.Tests.ps1`, `docs/quality/ROW_CALIB_PILOT_V1.md`.
- Змінені: `Program.cs` (App: `calib-*`), `Painter.EvidenceSeries.cs` (`CollectCalibPilot`), тестовий `Program.cs` (`--calib-only`), test csproj, `FrontierLauncherFixture/Program.cs`, `PROJECT_STATE.md`, `TEST_MATRIX.md`.
- Копії до змін: `row-calib-implementation-20261025/Before`. Старі frozen-файли, пакети й вердикти не змінені.

## 5. Далі

Користувач запускає пакет. Після «виконав»: review → derived-records → аудит. Якщо всі гілки ≥40 рядків: пакет 40×40 на замороженій гілці + 2–3 центровані контрольні H на крайніх рядках вікна (0075). Прошу при поверненні перевірити:
- правило вибору Y962 (п. 1);
- метрику тіла картинки як перевірку об'єднання (п. 2).
