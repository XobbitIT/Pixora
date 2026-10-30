# 2D-тексельний пілот v1 — контракт запуску

25.10.2026. Це окремий runnable-дослід через чинний `--standard-contour-pilot plane-*`, не новий маршрут START або автоматично придатний профіль. МетаB вже прийнята користувачем; exact width, U, caps/overlap/колір ще не сертифіковані.

## Джерело й контекст

[StandardPlanePilot.cs](../../src/CanvasForge.App/StandardPlanePilot.cs) поєднує незмінні fitted rowV2/column0039 через pure Core dense paths. Матриця параметрів і provenance SHA записані в manifest. Точний контекст: rect465,148..1510,1192, client2560×1440, origin0, DPI96, Shape3/Size3, Rust Palette/000000, Rapid Pitch1/Batch1, frame/end/release0,02s. Інший контекст відхиляється; UI/target не масштабуються й rect не підрізається.

Frozen blueprint v2, phase policy v1 та execution policy v1 збережені з SHA, перевіряються при init/read і копіюються в bundle. Статус OFFLINE_DRAFT_NOT_RUNNABLE у старому blueprint — історичний; готовність нового пакета визначає його Build_Manifest та новий manifest, старий asset не переписано.

## Сценарій й повнота

13cases,28released strokes,4538command points.4короткіH/4короткіV — лише позиція;2довгіV+2екранніконтролі по847points, bodyY284…1050; Block16 —16окремих H без serpentine-стиків. У12ліній3466обов’язкових body sections. Caps фіксовані окремо; missing caps не змінюють body.

[Painter adapter](../../src/CanvasForge.App/Painter.EvidenceSeries.cs) використовує існуючі contour runner, stable captures та per-track verifier. 16px reservation — лише guarded change-containment region, не Solid/footprint. Усі13reservation неперетинні й усі команди проходять no-go guard. Перед вводом — explicit fresh capture, immutable reference binding, current fullCanvasRGB12 baseline, історична frame geometry/zero-registration;16ROI/.985/delta0 і tool/cursor guards незмінні. Дані калібрування не беруться з runtime-відмови.

Обов’язкові всі28linked before/after/trace pairs, baseline/confirmation/final, hashes/QPC/order/context. Зникнення будь-якої пари, зокрема описового блоку, дає INCOMPLETE. Будь-який missing/invalid/duplicate/out-of-order mandatory section блокує всі line metrics/detection/matching; denominator не зменшується. Початкова невдача та partial input зберігаються, утримуване відпускається. Сліпого Undo, автоматичного retry/resume немає.

## Аналіз, заморожений до збору

[StandardPlaneAnalysis.cs](../../src/CanvasForge.App/StandardPlaneAnalysis.cs): diagnostic native local50/radius32, strict contrast80; requested RGB12 не замінюється fitted rendered RGB. Body вимірюється на кожному обов’язковому screen-along pixel. D незмінний: adjacent abs≥0,3px, consecutive same sign, accumulated abs≥1,2px. На кожному краї публікуються події, знаки, SignedEventSum, межі/ширина й grid residuals. Позиційні mean/P95 — опис, без прийнятоїU вони не фізичнийPASS.

Контролі: точний integer threshold floor(u), absolute unwrapped u(controlX,StartY), |Δphase|≤0,20 включно й той самий знак. Не брати найкращий рядок interval, nearest integer або modulo. Monotone one-to-one maximum-cardinality/minimum-error matching; перший tie зберігається в порядку skip-observed, skip-predicted, match. Кожен край вимагає nonzero observed/predicted та precision≥0,8 і recall≥0,8; зайві/протилежні події лишаються в denominator. Старого±15 немає. Прогнози: X693 — Y545/928 зі знаком−; X1329 — Y594/942 зі знаком+.

Research mechanism:

- CONSISTENT: обидві довгі центровані лінії мають0D на обохкраях, кожен із4control edges проходить phase gate.
- CONTRADICTED: після complete-data gate хоча б один край центрованої довгої лінії маєD.
- INCONCLUSIVE: complete centered0D, але control gate не пройшов.
- INCOMPLETE: обов’язкові дані неповні/некоректні; mechanism NOT_EVALUATED.

Загальний статус повного збору лишається MEASURED_NOT_QUALIFIED за будь-якого mechanism. Block звітує raw RGB12/contrast80 counts і4сторони/події лише описово, без coverage/Solid/PASS або гарантії16×16готових текселів. Немає production eligibility/physical certification чи автоматичного перенесення в START.

## Команди й користувацький запуск

Offline команди init/preflight/verify/review повертаються до Native lease/WPF/Release. Live entry — тільки `plane-run` через новий Start_Plane_Test.cmd з явним Enter та10s countdown. Init використовує завершений native row source, новий порожній каталог і frozen fixtures. Check не запускає ввід; повторний Run із retained markers автоматично переходить лише до Review й показує початкову причину. EOF/Q скасовує до attempt.

Release1.0.14-rc.8+local-texel-2d-1 — [готовий пакет](../../../../outputs/Pixora_Texel_2D_Pilot_1_20261025/READ_FIRST_UA.md). [Receipt](../../../../work/texel-2d-runtime-20261025/Receipt.json) відділяє fake pipeline, WPF, launcher, packaged checks від ще не виконаного native запуску. Старі live-вердикти й використані пакети не змінено.
