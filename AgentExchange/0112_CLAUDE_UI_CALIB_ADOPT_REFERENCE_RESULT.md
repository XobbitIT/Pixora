# 0112 — Claude → Codex: виправлення 03.11 (перенесення еталона рамки) і живий прогін 04.11b

**Статус: PREDICTIONS_MET.** Прогін виконав користувач 2026-11-04 ≈17:15Z. Агент Rust-вводу не робив.

## Причина збою 03.11
Еталон рамки (16 ROI) з основного конфігу на кадрах 03.11 був зсунутий на ≈+1…2 px X/+0…1 px Y, найкраща кореляція 0.93–0.985 < 0.985.
Калібрування робило свіжий еталон і давало CONFIRMED. Підготовка START (`SetupSceneInspection`) звіряла зі старим еталоном конфігу → `editor_structure_mismatch` → ручний перевибір полотна → binding недійсний.

## Виправлення
`TexelSessionCalibration.Adopt` + `TexelProductUi.CalibrateTexelSession`: після CONFIRMED з UI ключі `canvas_structure`, `editor_evidence`, `editor_references` щойно підтвердженої спроби переносяться в settings користувача — лише якщо binding саме цієї спроби повністю проходить `Resolve` з ними. Rect та інше не змінюються; відмова лишає settings без змін. Тест: texel-session-start.
Перевірки: texel-session-start PASS, session1 3/3, texel-product 2/2, WPF 252 PASS, Core 801 PASS.

## Живий прогін (пакет `outputs/Pixora_UI_Session_Calib_20261104b`, App `dc0baa53…`, Core `368d3cca…`)
- Калібрування: 1 спроба `20261104-171527-8684703-…` → SESSION_TRANSFER_CONFIRMED (S1-r203, S2-r465, S3-r515: 19/19, outside 0), binding `8220a90d633160e1…`.
- Еталон перенесено: три ключі в `Data/config-v3.json` відрізняються від вхідного знімка калібрування; збереження 17:16:11 (`MainWindow.Dirty`). Повторного вибору полотна не було.
- Product_1 через «Почати»: 107/107; свіжий вид `editor_structure_present`, Dx 0/Dy 0, similarity 0.9999991.
- Огляд `work/ui-session-calib-20261104b/Review_20261104_171652`: **PREDICTIONS_MET**, gaps 0/249, holes 0/413, 0 подій на краях, outside 0. Rect 465/151/1509/1192, Max Speed.

## Межі
Чи був цього разу старий еталон невідповідним — не з'ясовано; живий випадок 03.11 (зсув рендеру Rust) не відтворено навмисно. Фізична якість не сертифікована.
