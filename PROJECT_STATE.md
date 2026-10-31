# Pixora — поточний стан

Оновлено **31.10.2026**, Codex після перевірки етапу 1 (попередній продуктовий стан — Claude). Спершу [AGENTS.md](AGENTS.md); за потреби [архітектура](ARCHITECTURE.md), [перевірки](TEST_MATRIX.md), [Steam](STEAM_RELEASE.md). Історія — в [AgentExchange](AgentExchange/README.md) ([INDEX](AgentExchange/INDEX.json)) і тематичних звітах. Попередня версія цього файлу — в коміті `1ab3dcf`.

## Репозиторій і місця
- Код: `D:\Pixora\work\pixora-source`, гілка `ai-team/baseline-20261030` (від `release/pixora-1.0.14-rc`).
  - `1ab3dcf` — знімок усього незакоміченого стану rc.8; `3daa59b` — тести кінця b+1.867.
  - 31.10: R1/HEX guard; f04ebf9 — виправлений огляд і Product_2/3; HEAD 925d17d — дизайн калібрування сесії та тест ідентичності. Калібрування ще не реалізоване.
- **Головні** місця досліджень: `D:\Pixora\work`, `D:\Pixora\outputs`, `D:\Pixora\claude-local`.
- `D:\Pixora\Research` — копія від 30.10 (SHA-перевірена при копіюванні). Не видаляти до повної звірки; у неї не писати. Верхні рівні `work`/`outputs` збігаються з головними (31.10). Глибока звірка ще не виконана.
- Стара тека `C:\…\Codex\2026-10-03\new-chat` станом на 31.10 порожня. У неї не писати.
- Паралельна робота агентів: `D:\Pixora\AI-Team-Console` (0.2.3), `.\pixora\Start-PixoraRace.cmd -TaskFile …`. Race-агенти не змінюють цей файл і AgentExchange; результат записує людина або призначений агент.

## Мета
Ціль B (прийнята 23.10): рівність у текстурі таблички, 0 неочікуваних H/V-переходів у сітці текселів, ширина в текселях. Правий край — стан «повне покриття» (+≈0.5 текселя, [0090](AgentExchange/0090_USER_DECISION_RIGHT_EDGE_FULL_COVERAGE.md)). Adaptive — після Standard. **Steam: NOT READY.**

## Що працює (доказ, межі)
Продуктовий тексельний маршрут v2 ([0099](AgentExchange/0099_CLAUDE_LOCAL_TEXEL_PRODUCT_V2.md)), за прапорцем, типово OFF:
- **Core `TexelCompensatedPlanner` (`…-v2-endphase`):** кожен run закінчується на першому пікселі з фазою курсора ≥ b+1.867. Старт і рядки — як у pic3; `MinimumMargin` 0.25.
- **App `TexelProduct`:**
  - профіль `4d9d4e1d…`, похідна родина 1196 ([0078](AgentExchange/0078_CLAUDE_LOCAL_CALIBRATION_RESULT.md));
  - ціль ≤40×40 без масштабування, стовпці 75–459, рядки 198–537;
  - рядки адресуються із запасом 0.20 (0101; стовпці 0.25), рядок без спільної адресації → відмова з номером рядка;
  - лише чорний #000000 і прозорий фон.
- **Умови:** те саме полотно 465/148/1510/1192, Standard Size 3 круглий, Rapid, 20 мс, той самий вид (перевірка кадру перед першим штрихом).
- **CLI:** `Pixora.exe --texel-product-review RUN|latest OUT`, `--data-folder DIR`.
- **Живі докази:** **Texel_Product_1 через «Почати» — PREDICTIONS_MET** ([0100](AgentExchange/0100_CLAUDE_TEXEL_PRODUCT_1_RESULT.md), 107/107, 0 розривів/0 подій). **Texel_Product_2 (запас рядка 0.20, 340/453) — PREDICTIONS_MET** ([0103](AgentExchange/0103_CLAUDE_TEXEL_PRODUCT_2_RESULT.md)), але тільки після виправлення огляду. Пілоти pic8/pic9 PREDICTIONS_MET ([0096](AgentExchange/0096_CLAUDE_LOCAL_PICTURE8_RESULT.md), [0098](AgentExchange/0098_CLAUDE_LOCAL_PICTURE9_RESULT.md)) на стовпцях 75–459 у смугах рядків 203–242, 452–475, 493–532; контролі 6/6.
- **Повторна перевірка Codex 31.10:** Core **799 PASS**, WPF **244 PASS**, продуктові App-тести **2/2 груп PASS**; обидві Release-збірки 0/0. P1/P2/P3 replay: усі summary.json побайтово тотожні попереднім. Перевірено HEAD 925d17d разом із патчем перерізів (закомічено в `4caa0ef`, [0106](AgentExchange/0106_CLAUDE_RACE_A_B_RESULTS.md)). [Звіт етапу 1](docs/verification/STAGE1_RELEASE_20261031.md).
- Не сертифіковано: кольори, Solid/U, інші Size/форми/вид/rect, чиста Windows. `physicalQualityCertified=false`.

## Відомі обмеження
- Із запасом 0.20 спільно адресовані всі рядки 198–537; живі прогони Product_2/3 дали PREDICTIONS_MET. На рядках із запасом ≲0.26 верхні H-краї −0.10…−0.14, резерв до порогу 0.15 малий ([0105](AgentExchange/0105_CLAUDE_TEXEL_PRODUCT_3_RESULT.md)).
- Пакет Product_2 містить старий огляд: він дає INCOMPLETE на рядках із запасом <0.25. Виправлений бінарник — `work/texel-product2-20261031/FixedReviewer`.
- Причину залишків H ще не встановлено: Product_3 змінив залишок того самого краю, а лише 4 верхні краї мають низький запас. Не оголошувати ні форму, ні запас єдиною доведеною причиною. Вивід sections.json і edges-margins.csv закомічено (`4caa0ef`), зведення P1–P3 — у [0106](AgentExchange/0106_CLAUDE_RACE_A_B_RESULTS.md); причинний аналіз відкладено.
- Основний конфіг користувача (`%LOCALAPPDATA%\Pixora`) має полотно 465/151/1509/1192 і `Max Speed`, тому тексельний START там відмовить. Без калібрування сесії (етап 3) звичайний користувач режим не запустить.
- Legacy-ключ `texel_end_trim_px` маршрут не змінює, але входить в ідентичність плану. Рішення користувача відкрите.

## Наступна дія

1. **Етап 1 завершено:** актуалізовано стан, перевірено Release/Core/WPF/продуктовий огляд і пакети. Product_2 зберігає старий reviewer; див. [звіт](docs/verification/STAGE1_RELEASE_20261031.md). Патч перерізів закомічено в `4caa0ef`; push лише за дорученням.
2. **Етап 2:** реалізувати калібрування сесії за [дизайном](docs/SESSION_CALIBRATION_DESIGN_UA.md), починаючи з offline audit кандидата. Exact rect, effective timing, provenance, нова identity й відмова старих checkpoints; без автоматичного дозволу START за одним offline PASS.
3. **Етап 3:** окремо розібрати H-перерізи вже зібраних прогонів. Нові результати не підміняють frozen-пороги або вердикти.
4. Інші роботи: release merge/push лише за дорученням; прибирання історичних дублів після звірки; колір і розширення домену після session-binding.

Користувач уточнив 31.10: у поточному кроці виконати лише етап 1. Нового Rust-вводу не було.
## Відкриті рішення користувача
1. `texel_end_trim_px`: прибрати з ідентичності плану (з тестом сумісності checkpoint) чи лишити.
2. Чи входить Adaptive у перший реліз.
3. Мінімальний обсяг релізу. Увага: «на все полотно» потребує калібрування між смугами.
4. Хто записує результати race у PROJECT_STATE/AgentExchange.

## Постійні межі
- Точний user-selected rect. RGB12/contrast80, 16 ROI/correlation 0.985, runtime delta 0 не послаблювати.
- Невідомий focus/tool/cursor забороняє down; утримуване відпускається.
- Жодного Rust-вводу від агента. Commit/push — лише за дорученням.
- Закриті напрями без нових доказів не відкривати: Q/jitter, сліпі 0.996/1.0037, dot-sweep, ±2 інтерполяція, статичні кольорові offset, StampRows 3.
