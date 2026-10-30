# 0016 · Codex: реалізація за review0015

22.10.2026. Оригінал 0015 отримано від користувача, SHA256 `0c04e4ffe5bf0f67437461779303b5eb4e75fdc738b9f28dbe7b1499735f5fdd`. Хеші названих у ньому 0014 та U-протоколу точно збіглися з попереднім станом. Рецензент не мав пасивних JSON/CSV і не перевіряв їхні числа; цей статус збережено. Користувач прямо доручив після review почати реалізацію. Автоматичний обмін Claude не поновлювався; 0016 не надіслано.

## Що реалізовано

**1. Raw requested-color mask у Core.** [StandardPaintMask](../src/CanvasForge.Core/StandardPaintMask.cs) приймає native baseline/after, точні Canvas/ROI/вісь та frozen requested RGB. Піксель входить у маску лише при `maxRGB(actual,requested)≤12` і `maxRGB(actual,baseline)>80`. Колір не навчається за after, RGB12/contrast80 не послаблені. Усі matching spans зберігаються з отворами. Відсутній, розірваний або обрізаний рядок не має краю; missing не вилучається зі знаменника. Native alpha мусить бути 255. Результат — immutable snapshot, але не held-out Solid і не eligibility.

Індекси `FirstPixel/EndPixelExclusive` цілі; **фізичні краї** за прийнятою конвенцією screen pixel centers мають координати `index−0.5`. Автоматично трактувати їх як цілі physical edges або порівнювати зі старим target зі зсувом 0.5 не можна. Це окремий observable, не заміна local50% у старих моделях.

**2. Інтеграція в наявний офлайн-звіт.** [StandardBandResponseReview](../src/CanvasForge.App/StandardBandResponseReview.cs) тепер записує raw masks/counts окремо від діагностичних local50% edges і старого exploratory ranking. Нового продуктного CLI/кнопки збору не створено. Джерела читаються без змін, Painter/START/native-input lifecycle не запускаються. Звичайне малювання й target не змінені.

**3. Реальний C-control без Rust.** У Release App test host додано opt-in перевірку [NativeCaptureControlChecks](../tests/CanvasForge.App.Tests/NativeCaptureControlChecks.cs): відомий opaque GDI pixel buffer 257×193 показується у власному невеликому nonactivating вікні без масштабування; викликається саме production `Native.Screenshot`. Вікно закривається в finally; foreground не змінюється, Native input у test host заборонений. Отримано **2 кадри ×49601 px з точним збігом усіх RGB/alpha**, включно з чотирма межами. Source/capture RGBA SHA однаковий: `ba663d4b47170f9d2f5e74ec0b42384b01e010099380e917ef149c7df8749cb0`. Негатив з 1px-зсувом oracle має 49408 mismatch. Це контроль поточної capture-chain при **96 DPI** у цій Windows-сесії, не game projection, інші DPI або універсальна U.

## Перевірка пропозиції RGB12 на наявних станах

Пропозиція «перевірити, що маска дасть повні рядки» перевірена, і **ця передумова не виконалась**:

| 26 ascending-станів, усі обов'язкові рядки | Кількість |
| --- | ---: |
| Усього | 10426 |
| Один суцільний, не обрізаний run | 3796 |
| Немає matching run | 2632 |
| Кілька розділених matching runs | 3998 |
| Обрізано межею ROI | 0 |

Вихід C# незалежно звірено Python на raw native RGBA: **10426/10426 рядків**, requested RGB, matching/strong-nonmatching counts, run counts, причини, pixel indices та physical edges збігаються. Перевірено 58 file/RGBA bindings. Отже, таку маску можна чесно показати як raw color observation, але зараз не можна проголосити повним production-контуром або з'єднати її прогалини. Точний capture-control не усуває ці отвори й не легалізує підстановку observed RGB.

## Пропорційність і допуски

Повний 30-frame R не запущено і не перетворено на блокер фізичних робіт. [U-протокол](../docs/quality/UNCERTAINTY_STUDY_PROTOCOL.md) переглянуто **до нового збору**: опційно 4 кадри у двох моментах, лише для діагностики. Додано явні UNKNOWN/recorded поля для неконтрольованих lighting/time/weather/camera/HUD/overlays. Це не припущення, що збирач контролює гру. max-of-4 так само не стає e; лінійна сума U зберігається.

Рішення ±1px чи 0.5px не прийнято автоматично. Наявна minimax-таблиця обмежує записані комбінації в конкретному діагностичному observable; не доводить універсальну межу Rust і не замінює прийнятної видимої якості продукту. Збільшення допуску також не виправить відсутні RGB12-пікселі або disconnected rows. Чернетка лишається DRAFT.

## Перевірки та що далі

**737 Core tests PASS**, 10 нових mask-сценаріїв; **66 focused Standard Quality checks**. Core/test host та App/App test host Release: 0 warnings / 0 errors. Focused App response pipeline regression PASS; повний новий WPF suite/clean-Windows не заявляється. Новий C-control виконано реально; решта native paint — повторний аналіз старих даних, без нового вводу в Rust.

Під час перевірки виправлено власний дефект sentinel для від'ємних screen X у новому mask-коді; його виявили тести негативного origin/збереження sparse spans. Логи першої невдалої перевірки й фінальних PASS збережено. Це не зміна координат вводу продукту.

Наступний фізичний блокер — повний відгук обох країв/історії та непридатність raw-маски як повного контуру на цих даних. Наявний descending/інші low-старти/caps не виміряні; не домислюємо їх із high і не повертаємо Q/jitter. Для конкретно відсутніх даних використовується вже існуючий frontier-протокол; нового збирача цей крок не додає. **Фізичний коректор і вирівнювання Standard ще не реалізовані.** Це початок реалізації перевіреного backend-кроку, не нова збірка зі «виправленими лініями».

Артефакти: [Receipt](../../strict-paint-mask-20261022/Receipt.json), [незалежна звірка](../../strict-paint-mask-20261022/independent-verification.json), [усі native mask rows](../../strict-paint-mask-20261022/NativeMaskReplay/strict-mask-native.json), [C-control](../../strict-paint-mask-20261022/CaptureControl/capture-control.json), [тести Core](../tests/CanvasForge.Core.Tests/StandardPaintMaskChecks.cs), [зміни контракту](../docs/quality/PHYSICAL_QUALITY_CONTRACT.md).
