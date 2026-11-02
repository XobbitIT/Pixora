# Pixora — поточний стан

Оновлено **02.11.2026**, Claude (локально) після Product_1 з основного конфігу ([0109](AgentExchange/0109_CLAUDE_SESSION_PRODUCT_1_RESULT.md)); попередній запис — [0108](AgentExchange/0108_CLAUDE_SESSION_CALIB_1_RESULT.md). Спершу [AGENTS.md](AGENTS.md); за потреби [архітектура](ARCHITECTURE.md), [перевірки](TEST_MATRIX.md), [Steam](STEAM_RELEASE.md). Історія — в [AgentExchange](AgentExchange/README.md) ([INDEX](AgentExchange/INDEX.json)) і тематичних звітах. Попередня версія цього файлу — в коміті `1ab3dcf`.

## Репозиторій і місця
- Код: `D:\Pixora\work\pixora-source`, гілка `ai-team/baseline-20261030` (від `release/pixora-1.0.14-rc`).
  - `1ab3dcf` — знімок усього незакоміченого стану rc.8; `3daa59b` — тести кінця b+1.867.
  - 31.10: R1/HEX guard; f04ebf9 — виправлений огляд і Product_2/3; 925d17d — дизайн калібрування сесії; HEAD 8de7e6b — офлайн-аудит binding (етап 0).
  - **Незакомічено:** пілот калібрування сесії етапів 1+2 (0107): `StandardSessionCalibPilot.cs`, тести, лаунчер; diff — `work/session-calib1-20261031/patch.diff`; запис результату 0108.
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
- **Session_Calib_1 на основному конфігу (465/151/1509/1192, Max Speed) — SESSION_TRANSFER_CONFIRMED** ([0108](AgentExchange/0108_CLAUDE_SESSION_CALIB_1_RESULT.md)): прогін користувача 02.11; контролі 203/465/515 → j збігся, медіани |залишку| ≤0.061 (поріг 0.15), 57/57 перерізів, 0 змін поза резерваціями. Офлайн-огляд повторено побайтово тотожно; конфіг не змінений. Live binding `1542dd10…`, `bindingActive=true`, `startEnabled=false`. Межа: лише 3 контролі лівої смуги, чорний, Size 3.
- Не сертифіковано: кольори, Solid/U, інші Size/форми/вид/rect, чиста Windows. `physicalQualityCertified=false`.

## Відомі обмеження
- Із запасом 0.20 спільно адресовані всі рядки 198–537; живі прогони Product_2/3 дали PREDICTIONS_MET. На рядках із запасом ≲0.26 верхні H-краї −0.10…−0.14, резерв до порогу 0.15 малий ([0105](AgentExchange/0105_CLAUDE_TEXEL_PRODUCT_3_RESULT.md)).
- Пакет Product_2 містить старий огляд: він дає INCOMPLETE на рядках із запасом <0.25. Виправлений бінарник — `work/texel-product2-20261031/FixedReviewer`.
- Причину залишків H ще не встановлено: Product_3 змінив залишок того самого краю, а лише 4 верхні краї мають низький запас. Не оголошувати ні форму, ні запас єдиною доведеною причиною. Вивід sections.json і edges-margins.csv закомічено (`4caa0ef`), зведення P1–P3 — у [0106](AgentExchange/0106_CLAUDE_RACE_A_B_RESULTS.md); причинний аналіз відкладено.
- Основний конфіг користувача (`%LOCALAPPDATA%\Pixora`, SHA `0fca2a34…`) має полотно 465/151/1509/1192 і `Max Speed`. Тексельний START там і далі відмовляє: його не змінено.
  - Етап 0 — Passed (0107); етапи 1+2 — CONFIRMED (0108).
  - START з основного конфігу — крок 3: PREDICTIONS_MET (0109), лише Product_1 і лише з binding цієї збірки.
- Лаунчер Session1 у `-Mode Check` після спроби показує трасу стека (`retained data; retry/resume disabled`) замість короткого пояснення. Захист правильний, UX — дрібний недолік.
- Legacy-ключ `texel_end_trim_px` маршрут не змінює, але входить в ідентичність плану. Рішення користувача відкрите.

## Наступна дія

**02.11 — крок 3 виконано ([0109](AgentExchange/0109_CLAUDE_SESSION_PRODUCT_1_RESULT.md)):** Product_1 через «Почати» з основного конфігу (465/151/1509/1192, Max Speed) через session binding `4993dab0…` — **PREDICTIONS_MET** (107/107, gaps 0/249, holes 0/413, 0 подій, 0 змін поза). Перед цим Session_Calib_1 на новій збірці — CONFIRMED (спроба 1 INCOMPLETE до paint-down через `brush-shape-row`). Пакет `outputs/Pixora_Session_Product_1_20261102` (App `38382f4d…`, Core `5c4a8d88…`).
- **Закомічено `c3ae220`:** патч Codex (race `20261102-175413`: `TexelProductSession`, START через binding, binding Id в ідентичності плану/checkpoint, огляд, `--texel-session-package`), лаунчер `tools/Product1Session_Start.ps1`/`Start_Product1_Test.cmd`, 0109. Core 800 PASS, WPF 245 PASS (перший WPF-прогін — нестабільний access violation у `CheckBoundProbeRecovery`, повтор PASS; не досліджено).

1. Push/merge — лише за дорученням.
2–3. **Race D (`20261102-210437`, переможця немає):** Codex — патч >160 КБ через випадковий вивід тестів `work/task-d-checks`; Claude — відхилено (B не доведено). Застосовано код Codex без `work/` (**незакомічено**):
   - лаунчер Product_1: огляд запускається завжди, коли last-run є, код виходу UI в консолі; `tests/Tools/Product1Launcher.Tests.ps1` 3/3, Session1 11/11;
   - фонові потоки прев'ю/калібрування більше не читають живі UI Settings (`MainWindow.cs` прев'ю через snapshot мови; `Capture.cs`, `SpeedPage.cs` — клон). Core 801 PASS, WPF 246 PASS ×3.
   - Межа: нестабільний AV траплявся рідко, тож 3 зелені прогони не доводять, що саме це була причина; гіпотеза — одночасне перше звернення до лінивого JsonObject з двох потоків.
4. **Рішено ([0110](AgentExchange/0110_USER_DECISION_RELEASE_BUILD_CALIBRATION.md)):** binding лишається за SHA App/Core; калібрувати лише фінальну релізну збірку. Живий тексельний тест dev-збірки — зі свіжим Session_Calib_1 цієї збірки. Для релізу потрібне штатне калібрування сесії в UI.
5. Причинний аналіз H-перерізів — окремо; frozen-пороги не змінювати. Колір і розширення домену — наступні напрями.

Нового Rust-вводу агентом не було.
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
