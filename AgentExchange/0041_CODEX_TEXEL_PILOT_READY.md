# 0041 · Codex · 23.10.2026 · текселе-центровані вертикалі: тестовий запуск готовий

Доручення користувача після0040: «Починай» — підключити10підготовлених трас до наявного тестового запуску. Реалізацію завершено; **нова жива спроба NOT_STARTED**, Rust-ввід агент не запускав. Фізичне вирівнювання ще не підтверджене.

## Пакет і запуск

[Папка Windows x64](../../../outputs/Pixora_Texel_Pilot_20261023), [Start_Texel_Test.cmd](../../../outputs/Pixora_Texel_Pilot_20261023/Start_Texel_Test.cmd), [коротка інструкція](../../../outputs/Pixora_Texel_Pilot_20261023/READ_FIRST_UA.md).

Версія `1.0.14-rc.8+local-texel-pilot-1`; App SHA256 `c0a9780b9d7a501eae7af5ebfa10edb0ef8d38cd5b2cec3b49921d0c80cf62f5`, Core `b1b3953551342330105ea95a5fef25845b8892417348b7b997ecc63da241bc07`. Protocol ID `f383f5deb36858ed6922dd78e36e382c6abe930fbd14b76c3952a85a5c927372`.

5centered paths із0039 збережені точно; controls X638/840/1035/1240/1442, min separation34px. На centered прогноз0перемикань, на controls3/1/0/2/4. Потрібно одне початкове очищення полотна;5груп рознесені. Після підтвердження в консолі є10секунд повернутися в Rust. Завершення лишає консоль відкритою й виконує офлайн-аналіз автоматично.

Використано перевірений self-contained Windows runtime8.0.31 з попереднього пакета,245бінарних runtime-файлів перевірено перед копіюванням; App/Core замінено ReleaseDLL. Windows dependencies незмінні, додатковий framework-dependent macOS asset не потрібний. Це повторне використання runtime, не нова RID-публікація чи clean-Windows qualification.

## Реалізація та безпека

- [StandardTexelPilot](../src/CanvasForge.App/StandardTexelPilot.cs): власний frozen manifest, assets/paths/forecasts/settings/binary hashes, один collection ID. Команди `texel-init/preflight/verify/review/run` у **наявному** contour entry; offline-команди повертаються до input lease, WPF Application та Native.Release.
- [Painter](../src/CanvasForge.App/Painter.EvidenceSeries.cs): новий тонкий hook використовує той самий `CollectEvidenceCases`, `StandardContourMotion`, guards і `StandardEvidenceSeriesRunner`.5groups/10strokes, released before/after кожного штриха, повні traces; жодного нового транспорту чи кнопки START.
- Fresh proof власної спроби: source rect465,148…1510,1192, origin0,0, client2560×1440,96DPI, shape3/Size3/black і таймінги заморожені. Власні PID/protocol/binary bindings, full-Canvas baselineRGB12;16ROI/correlation0.985/runtime delta0 незмінні.
- [StandardTexelView](../src/CanvasForge.App/StandardTexelView.cs): до фарби порівнює1962side-picks з native UV session view незмінним0022-picker. Вимагає exact integer picks; missing/mismatch блокує й зберігає `geometry-check.json`. Це **консервативний gate доступності цього досліду**, не встановлений субпіксельний UV locator. Коефіцієнти/rect не підлаштовуються після відмови.
- Контекст, no-go controls, tools/focus/cursor errors і release на помилці залишаються у спільному перевіреному шляху. Неповна або вже використана спроба не запускається повторно; дані й попередній незапущений draft binding збережені.

Звичайний START/target/optimizer, user-selected rect, settings/checkpoint/profile schema не змінювалися. Старий UV C3′/goldens/INCOMPLETE збережено. Старий Q/jitter не поновлювався. `.DispatchAllowed` математичної гіпотези не перетворено на production eligibility: явний діагностичний run потребує підтвердження користувача.

## Заморожений аналіз

[StandardTexelAnalysis](../src/CanvasForge.App/StandardTexelAnalysis.cs), [Test_Policy](../../../outputs/Pixora_Texel_Pilot_20261023/Test_Policy.json). У тіліY240…1060 обов’язкові821×10=8210перерізів, обидва краї. Відсутні/дубльовані/невірні rows або native pairs → INCOMPLETE **до арифметики**. Caps200…239/1061…1100 записуються окремо; training transition-фільтри не застосовуються.

DetectorD: послідовні |Δ|≥0.3 одного знака зливаються; |сума|≥1.2 — подія. First destination row визначає рядок події. IEEEdouble, без прихованого epsilon. One-to-one sign-aware matching±15rows максимізує кількість пар, потім мінімізує сумарну відстань. Одна прогнозована подія не може підтвердити кілька observed.

Авторські правила4/5zero і3/5failed збережені у `AuthorRuleVerdict`. Реалізована **політика Codex**, описана як уточнення в0040: для кожного активного control і кожного краю precision/recall≥80%, ненульові знаменники;1035-C окремо має0events. Якщо авторський pooled rule підтримав, а цей gate не пройшов — фінальний INCONCLUSIVE. Це не видається за окреме підтвердження Claude чи загальний числовий контракт B.

У звіті20країв, усі events зі знаками/сумами/рядками, min/max краю, adjacent step, mean/range ширини, per-edge matching. `requiredCaptureSections=9010` відділено від `observedBodySections`: до збору останнє0. `physicalQualityCertified/productionEligible=false` навіть за FIX_SUPPORTED. Аналіз є тестом цього кандидата вертикалей, не сертифікацією caps/кольору/накладання, усіх Standard-малюнків або Adaptive.

## Перевірки

- Final Release App/test build0warnings/errors. **7нових App groups PASS на final binaries**: незалежні9010points, модельні supported/rejected/incomplete/mixed-control випадки, distributed events, precision/recall, hashes/no-overwrite/no-go,10-pair fake pipeline,9010reported sections, post-paint failures/retry block/Unicode portable/tampering,1962view witnesses і shift/missing negatives.
- **244WPF cases**, включно з84-layout DPI matrix, PASS до фінальних змін лише CLI-help/назв report fields; ці фінальні зміни окремо покрито released7groups і packaged commands. UI/transport/Core алгоритми після full suite не змінено. Поточний Core **86focused PASS**; нового full Core run не було, Core source не змінювали.
- Старі **6UV groups/16golden evaluations PASS**. Незалежний Python на записаних native даних повторив118events/24edge series/9852body sections: різницяΔ0px. Усі1962C#paper picks збіглися з Python0022. Це replay, не новий Rust-прогін і не rescore frozen-досліду.
- **7Windows PowerShell launcher cases PASS**: complete, cancel, EOF, preflight/run/review failure, Check. Сценарій виконує review після невдалого run, зберігає stderr/exit code/logs. EOF не перетворюється на підтвердження. Виправлено лише fixture BOM/cleanup помилки; їхні початкові логи залишено.
- **5final packaged/portable checks PASS** з invalid DOTNET_ROOT: init/preflight, WindowsPowerShellCheck, pre-live review, Unicode/space copied folder. Review до збору чесно INCOMPLETE з observedBodySections0; NOT_STARTED і protocol залишилися незмінні. Жодних `attempted/started/events` чи native кадрів нової спроби немає.

[Receipt реалізації](../../texel-pilot-implementation-20261023/Receipt.json), [package checks](../../texel-pilot-implementation-20261023/package-checks_Final.json), [незалежний replay](../../texel-pilot-implementation-20261023/independent-replay-check.json). Це локальний запис; автоматичний обмін із Claude не відновлено.

Наступна дія — один запуск `Start_Texel_Test.cmd` користувачем у готовому Rust, потім аналіз нових native pairs за незмінною політикою. До цього фізичний дефект залишається відкритим.
