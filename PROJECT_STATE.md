# Pixora — поточний стан

Оновлено **31.10.2026**, Claude. Спершу [AGENTS.md](AGENTS.md); за потреби [архітектура](ARCHITECTURE.md), [перевірки](TEST_MATRIX.md), [Steam](STEAM_RELEASE.md). Історія — в [AgentExchange](AgentExchange/README.md) ([INDEX](AgentExchange/INDEX.json)) і тематичних звітах. Попередня версія цього файлу — в коміті `1ab3dcf`.

## Репозиторій і місця
- Код: `D:\Pixora\work\pixora-source`, гілка `ai-team/baseline-20261030` (від `release/pixora-1.0.14-rc`), HEAD `3daa59b`.
  - `1ab3dcf` — знімок усього незакоміченого стану rc.8.
  - `3daa59b` — тести кінця b+1.867 у `TexelCompensatedPlanner` (Core 789 PASS).
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
  - рядок без спільної адресації із запасом 0.25 → відмова з номером рядка;
  - лише чорний #000000 і прозорий фон.
- **Умови:** те саме полотно 465/148/1510/1192, Standard Size 3 круглий, Rapid, 20 мс, той самий вид (перевірка кадру перед першим штрихом).
- **CLI:** `Pixora.exe --texel-product-review RUN|latest OUT`, `--data-folder DIR`.
- **Живі докази:** pic8/pic9 PREDICTIONS_MET ([0096](AgentExchange/0096_CLAUDE_LOCAL_PICTURE8_RESULT.md), [0098](AgentExchange/0098_CLAUDE_LOCAL_PICTURE9_RESULT.md)) на стовпцях 75–459 у смугах рядків 203–242, 452–475, 493–532; контролі 6/6.
- **Тести:** Release 0/0; Core 789; WPF 244; App texel-product/pic8/pic9 PASS.
- Не сертифіковано: кольори, Solid/U, інші Size/форми/вид/rect, чиста Windows. `physicalQualityCertified=false`.

## Відомі обмеження
- Між смугами є рядки без спільної адресації (≈389 підтриманих). Картинку через межу смуг цими правилами не намалювати; потрібне додаткове калібрування.
- Найбільший H-залишок pic9: **0.138** (правий край R) при порозі 0.15. Не досліджено.
- Основний конфіг користувача (`%LOCALAPPDATA%\Pixora`) має полотно 465/151/1509/1192 і `Max Speed`, тому тексельний START там відмовить. Без калібрування сесії (етап 3) звичайний користувач режим не запустить.
- Legacy-ключ `texel_end_trim_px` маршрут не змінює, але входить в ідентичність плану. Рішення користувача відкрите.

## Наступна дія
1. **Живий тест** [Pixora_Texel_Product_1_20261030](../../outputs/Pixora_Texel_Product_1_20261030/READ_FIRST_UA.md) (NOT_STARTED) кнопкою «Почати». Потім `--texel-product-review latest`, запис **0100**. Прогноз PREDICTIONS_MET; розбіжність означає різницю продуктового шляху й пілота.
2. **R1:** тести `TexelExperimentSettings.ValidatePlan` у `TexelCompensatedChecks.cs`.
3. Паралельно офлайн: залишок H 0.138 на pic9.
4. Етап 0: злиття в release/push (лише за дорученням), прибрати `--standard-cursor-only/` і `TESTING_BETA56_UA – копія.md`.

План етапів 0–5 і задачі race R1–R5: документ користувача «Pixora — план завдань».

## Відкриті рішення користувача
1. Коли проганяти Texel_Product_1.
2. `texel_end_trim_px`: прибрати з ідентичності плану (з тестом сумісності checkpoint) чи лишити.
3. Чи входить Adaptive у перший реліз.
4. Мінімальний обсяг релізу. Увага: «на все полотно» потребує калібрування між смугами.
5. Хто записує результати race у PROJECT_STATE/AgentExchange.

## Постійні межі
- Точний user-selected rect. RGB12/contrast80, 16 ROI/correlation 0.985, runtime delta 0 не послаблювати.
- Невідомий focus/tool/cursor забороняє down; утримуване відпускається.
- Жодного Rust-вводу від агента. Commit/push — лише за дорученням.
- Закриті напрями без нових доказів не відкривати: Q/jitter, сліпі 0.996/1.0037, dot-sweep, ±2 інтерполяція, статичні кольорові offset, StampRows 3.
