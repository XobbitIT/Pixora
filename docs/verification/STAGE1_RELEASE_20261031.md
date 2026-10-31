# Етап 1 — актуалізація та перевірка Release

31.10.2026, Codex. Виконано за уточненням користувача: лише етап 1. Калібрування сесії (етап 2) й причинний аналіз H-країв (етап 3) не реалізовувалися в цьому кроці. Rust-вводу, commit/push не було.

## Точний стан

Гілка ai-team/baseline-20261030, HEAD 925d17dddddeb550ed772d5e8db4d37cea0bc0e9. До початку роботи вже були незакомічені зміни збору sections/edges-margins та їхні тести (п'ять modified і два untracked файли). Вони включені в перевірку; авторство реалізації не приписується цьому кроку. Їхній патч не змінений. Резервна копія, diff, untracked sources і маніфест 437 файлів — у [каталозі перевірки](D:/Pixora/work/session-implementation-20261031).

Виправлення rowMargin огляду вже закомічене як f04ebf9. 925d17d містить дизайн калібрування сесії та Core-тест ідентичності, а не готове калібрування.

## Реально виконані перевірки

- Core Release: 0 warnings / 0 errors; **799/799 PASS**.
- App + App.Tests Release: 0 warnings / 0 errors.
- Повний WPF-набір: **244/244 PASS**, включно з 84-layout DPI matrix; offline test host, без Rust-вводу.
- Продуктові App-тести `--texel-product-only`: **2/2 груп PASS** (точні маршрути pic8/pic9, відмови, огляд і додаткові дані перерізів).
- Поточний App повторно прочитав native before/after трьох Product-прогонів. **P1/P2/P3 PREDICTIONS_MET; кожен summary.json побайтово тотожний попередньому**. По 107 runs, gaps 0/249, hole changes 0/413, events 0, outside 0. Це replay записаного, не новий живий прогін і не незалежна реалізація аналізатора.
- `git diff --check`: без помилок.

## Пакети та обмеження

Перевірено 3 × 258 записів Files_SHA256.json: **771 збігаються, 3 відрізняються**. У кожному пакеті відрізняється лише Product_Data/config-v3.json — робочий конфіг після запуску. Не перезаписували, не скидали й не зараховували як hash-PASS. DLL та інші 257 файлів кожного пакета тотожні маніфесту. Огляд відтворював effective-settings конкретних last-run.

- Product_1: старий успішний запуск; історичний пакет не замінено.
- Product_2: його вбудований reviewer застарілий (hard-coded 0.25), ним не оцінювати рядки 0.20…0.25. Належний історичний результат — Live_Review_Fixed; повтор поточною збіркою ідентичний.
- Product_3: містить виправлений reviewer. Наступні пакети збирати з актуального джерела, не копіювати reviewer Product_2.

Збережено локальну копію перевіреної framework-dependent Release у `D:\Pixora\work\session-implementation-20261031\Release` (21 файл, SHA-маніфест поруч). Це артефакт перевірки, **не новий self-contained live-пакет**; потрібний відповідний .NET Desktop Runtime. Старі живі пакети залишені незмінними.

App SHA256: `1678d0eb01a67f284551790288287258314c6dc7efb9abe83330ed412cbd9af0`.
Core SHA256: `19c7673db2a8a193180b9acd139f1da72b679ee3cb8af125d985b5c32649bee4`.

Логи, package-audit.json, replay-comparison.json, source-files.json, Release_SHA256.json і Receipt.json — у каталозі перевірки. Змін продуктового коду в цьому кроці немає; оновлено лише документацію стану.

## Наступне

Реалізація session-binding за docs/SESSION_CALIBRATION_DESIGN_UA.md: exact user rect, timing, capture/view/structure provenance та identity; спершу offline audit. Кандидат не дає права на START до належного підтвердження сесії. Поточний frozen режим не послаблювати. Експорт перерізів уже є в робочому патчі й пройшов регресію; окремий причинний аналіз — наступний етап.

Фізична сертифікація, чиста Windows, новий живий START, інший rect/вид/Size/колір не перевірялися. physicalQualityCertified=false; Steam NOT READY.
