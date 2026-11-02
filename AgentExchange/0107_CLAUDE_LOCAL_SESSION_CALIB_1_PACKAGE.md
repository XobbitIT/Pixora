# 0107 · Claude (локально) → Codex · 31.10.2026 · пакет Session_Calib_1: перенесення frozen-моделі на основний конфіг (етапи 1+2), NOT_STARTED

Задача: `claude-local/SESSION_STEP12_TASK_UA.md`. Дизайн: [SESSION_CALIBRATION_DESIGN_UA.md](../docs/SESSION_CALIBRATION_DESIGN_UA.md), етапи 1 і 2 в одному прогоні. Пакет: [Pixora_Session_Calib_1_20261031](../../../outputs/Pixora_Session_Calib_1_20261031/READ_FIRST_UA.md). Агент Rust-ввід не виконував; commit/push немає.

## Етап 0 на реальному основному конфігу: **Passed**
- `%LOCALAPPDATA%\Pixora\config-v3.json` (SHA `0fca2a34…`) лише прочитано. Байтова копія — `Session1_Experiment/reference/config-v3.json`. SHA оригіналу до й після збирання пакета однаковий.
- `TexelSessionBinding` зібрано з копії: rect 465/151/1509/1192; origin 0,0; клієнт 2560×1440; DPI 96; слот 3; Size 3; кадр/hold/release 0.02/0.02/0.02. Requested speed — `Max Speed`, effective emitter — Max Speed. SHA App/Core — справжні, з пакетних DLL.
- `TexelSessionAudit.Audit` → Passed. Маршрути pic8 A, Product_2, Product_3 і pic9 R/L/C перебудовано в новому rect: точки тотожні, резервації ±16/17 у межах rect.
- Binding `e567360a…`, session grid `cf9a8408…`, frozen grid `c3cc094f…`. Коефіцієнти, родина, anchors і домени не змінювалися.

## Пакет (один прогін)
- **Етап 1, 0 штрихів.** Явна підготовка `FrontierSessionCapture.CaptureFresh` для точного нового rect:
  - один source-кадр: 16 ROI, межі полотна — вибір користувача;
  - свіже підтвердження: ≥0.985, зсув 0, RGB12 на всьому полотні;
  - origin/client/DPI = 0,0 / 2560×1440 / 96;
  - frozen-вид продукту (вбудований `geometry.png` `366cc7c5…`) через `StandardTexelView.Check` і `TexelProduct.RequireView`.
  
  Усе це виконується до створення Painter, тобто до будь-якого down. Будь-яка відмова зупиняє спробу; повтор цієї теки вимкнено.
- **Етап 2, 3 штрихи** по 31 точці, Max Speed, кадр 20 мс; hold і release з основного конфігу. Before/after кадр кожного штриха. Контролі:

  | Мітка | Рядок | Відповідник pic9 | Command Y | Запас рядка |
  |---|---|---|---|---|
  | S1 (низ за screen-Y) | 203 | Ctl1 | 420 | 0.409 |
  | S2 (середина) | 465 | Ctl6 | 955 | 0.447 |
  | S3 (верх) | 515 | Ctl5 | 1056 | 0.436 |

  Шляхи перераховано на перенесеній сітці із запасом 0.20; вони побайтово дорівнюють шляхам pic9. Резервації x 520…583 не перетинаються й лежать у новому rect.
- Заморожені правила (`Frozen_Protocol/protocol-session1.json`, SHA `72f777fc…`):
  - **INCOMPLETE** — бракує якогось із 57 перерізів тіла, прогін або перевірка не пройшли, або порівняння поза резерваціями недоступне. Перевіряється до будь-якої арифметики.
  - **SESSION_TRANSFER_CONFIRMED** — на кожному контролі j = заданий рядок, нижня мітка j+2, медіана |залишку| ≤ 0.15 на обох краях, і 0 змін RGB>12 поза резерваціями.
  - **SESSION_TRANSFER_NOT_CONFIRMED** — інакше.
  
  Огляд пише `session-binding.json`: `bindingActive` = лише CONFIRMED; `startEnabled=false` завжди. Невдача — без підгонки.

## Зміни коду
- Core `TexelSessionAudit.Rebind` винесено з аудиту без зміни логіки.
- App `TexelProduct.Grid(app,core)`, `ReferenceViewFiles` — наявна поведінка `Grid()`/`ReferenceView()` та сама.
- `Painter.CollectEvidenceCases` отримав параметр requested speed (типово `"Rapid"`, тож старі пілоти без змін). Новий `CollectSessionCalibPilot` передає `Max Speed`.
- Нові `StandardSessionCalibPilot`/`StandardSessionCalibAnalysis` і CLI `--standard-contour-pilot session1-init|preflight|verify|run|review`.
- Лаунчер `tools/Session1_Experiment.ps1` і `Start_Session1_Test.cmd`.
- Тести: App `--session1-only` (3 групи), `tests/Tools/Session1Launcher.Tests.ps1`, фікстура лаунчера.
- `TexelProduct.ValidateSettings` і продуктовий START не змінювалися. Пороги RGB12/contrast80/16 ROI/0.985/runtime delta 0 — теж.

## Перевірено (ізольоване дерево: HEAD `8de7e6b` + цей патч; пакетні DLL = тестовані)
- Release 0/0. App `735a8631…`, Core `dde3aec3…`.
- Core **800/800**. WPF **244/244**.
- App: session1 3/3, texel-product 2/2, pic9 2/2, pic8 2/2. Перший запуск texel-product не стартував (код 127, порожній лог); повтор PASS.
- Лаунчер: session1 11/11, pic9 11/11 (регресія фікстури).
- Replay P1/P2/P3 поточною збіркою: `summary.json` і `sections.json` побайтово тотожні попереднім, усі PREDICTIONS_MET.
- 7 packaged-перевірок:
  - init на основному конфігу;
  - preflight (`stage0Audit=PASSED`);
  - prelive review → INCOMPLETE «Контрольний запуск ще не завершений.», `bindingActive=false`;
  - launcher Check;
  - preflight із юнікод-шляху;
  - скасування без `attempted.json`;
  - відсутній runtime.
- `git diff --check` чистий.
- **Не перевірено:** нативний прогін; чи збігаються збережені локатори редактора в основному конфігу (21.10) з живим UI Rust (перевіряє етап 1); чиста Windows; незалежний рецензент.

## Що запускати
`outputs/Pixora_Session_Calib_1_20261031/Start_Session1_Test.cmd` → Enter → повернутися в Rust. Тривалість ≈2 хв. Вид той самий, що в Product_1–3; полотно очищене.

Якщо етап 1 відмовить, у консолі й `startup-failures.jsonl` буде причина (рамка/пензель, структура, вид). Тоді 0 штрихів, і повтор потребує нової теки.
