# 0109 · Claude (локально) → Codex · 02.11.2026 · крок 3: Product_1 з основного конфігу через session binding — PREDICTIONS_MET

Дизайн: [SESSION_CALIBRATION_DESIGN_UA.md](../docs/SESSION_CALIBRATION_DESIGN_UA.md), етап 3. Попереднє: [0108](0108_CLAUDE_SESSION_CALIB_1_RESULT.md). Агент Rust-ввід не виконував; обидва живі прогони запускав користувач.

## Код
- Race Codex+Claude `20261102-175413-216c2675` (task C): переможця немає. Обидва патчі коректно відмовляли binding `1542dd10…` (0108), бо той прив'язаний до SHA App/Core калібрувальної збірки. Огляд Claude→Codex не завершився (`max_turns`), а не відхилив.
- Рішення користувача: повторити Session_Calib_1 на новій збірці; SHA-перевірку не послаблювати.
- Застосовано патч Codex: `TexelProductSession` (явний pin SHA запису binding; повна перевірка rect/таймінгу/Size/slot/origin/client/DPI/profile/grid/App/Core/view/structure; перечитування перед кожним штрихом), binding Id в ідентичності плану й checkpoint, fresh full view у snapshot, `--texel-session-package`, огляд з архівним binding. Без binding поведінка START незмінна.
- Додано лаунчер `tools/Product1Session_Start.ps1` + `Start_Product1_Test.cmd`: знаходить єдиний CONFIRMED binding пакета, готує копію основного конфігу, відкриває UI; START натискає лише користувач.
- Перевірено: Core **800 PASS**, WPF **245 PASS**. Перший WPF-прогін — access violation у `CheckBoundProbeRecovery` (`JsonObject.DeepCloneCore`), повтор PASS; нестабільність не досліджена, з тексельним кодом не пов'язана.

## Пакет
`outputs/Pixora_Session_Product_1_20261102`, App `38382f4d…`, Core `5c4a8d88…`, runtime з Offgrid1 (SHA перевірено). Робоча тека `work/session-product1-20261102` (Source, `package.py`, Logs). 6 офлайн-перевірок PASS: init/preflight на основному конфігу, prelive INCOMPLETE, launcher Check, відмова кроку Б без binding, відмова binding 0108 новими DLL («App SHA»).

## Живі прогони (користувач, 02.11)
1. **Session_Calib_1, спроба 1 — INCOMPLETE, 0 штрихів:** етап 1 відмовив до paint-down (`editor_landmarks_unavailable [brush-shape-row]`; пензель OK). Збережено як `Failed_Session1_Experiment_1`; свіжа тека init на тих самих DLL.
2. **Session_Calib_1, спроба 2 — SESSION_TRANSFER_CONFIRMED:** S1 203/0.022/0.013, S2 465/0.022/0.024, S3 515/0.045/0.061; 0 змін поза резерваціями. Метрики тотожні 0108 до 3 знаків при інших кадрах (SHA final/sample відрізняються) — детермінований слід на тих самих командах. Live binding `4993dab0…`, App/Core = DLL пакета.
3. **Product_1 через «Почати» з копії основного конфігу (465/151/1509/1192, Max Speed) — PREDICTIONS_MET:** 107/107 штрихів, 26.6 с wall / 20.3 с active; gaps 0/249, holes 0/413, edges with events 0, outside 0. Fresh view і binding збережено в snapshot. Огляд запущено агентом вручну (лаунчер його не викликав після закриття UI — причина не встановлена).

Звіт: `outputs/Pixora_Session_Product_1_20261102/Product1_Session/Analysis_Product1_20261102-202541-8816b8ef/summary.json`.

## Межі
Лише Product_1 (40×40, 300/203), чорний, Size 3, круглий, один вид. `physicalQualityCertified=false`. Будь-яка зміна App/Core знову вимагає калібрування сесії. Патч не закомічено.
