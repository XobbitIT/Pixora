# Pixora 1.0.14-beta.31 — відповідь на review beta.30

Підтверджено й виправлено: одноразовий numeric readback, частково обрізане ядро `ProbeAnalysis.Control`, відсутність singleton, відсутність elevation precheck, збереження лише тексту буфера, приховані restore failures, злиття timeout/cancel, післятестове відновлення на UI-потоці, некоректні числові JSON-значення, order-dependent JSON identity, дробові legacy click points, повторне читання speed profile та застарілі current docs. Деталі в CHANGES_BETA31_UA.md. Офлайн перевірки: 253 Core / 69 WPF; результат чистого CI та точні файли фінального пакета — у Build_Manifest.json і Testing/Validation.

## Що потребує уточнення

- Фокус Rust перевірявся на кожному guard і раніше. 75 мс обмежували PID/process name/DPI/rect. Тому твердження про 75 мс «сліпого» фокуса неправильне. Beta.31 додатково перевіряє повну геометрію без цього інтервалу перед кліками/key-down/початком мазка.
- `Native.IsRustProcess` створює Process під геометричним throttle, а не на кожному опитуванні таймера. При ~13 перевірках/сек це не доведений bottleneck малювання.
- 16384² = 268435456 і поміщається в Int32. Аудит уже перевіряв allocation через checked; виправлено окремий public `GapBounds` для довільних прямокутників, де множення могло переповнюватися.
- `CalibrationSession.Align` відхиляє зміну DPI. Накопичення дробової похибки при багаторазовому автоматичному DPI scaling не відтворюється звичайним шляхом. Дробові старі точки палітри справді потребували явного округлення.
- Від'ємне `aa` Lanczos при додатному `ww` дає нульову alpha та пропускає RGB-ділення. Тому доведення інверсії кольору саме з від'ємного alpha accumulator недостатнє. Додаткові тести прозорих AA-країв і малих знаменників залишаються окремою задачею; resampler у beta.31 не змінювався.
- `Lab.Distance` використовується як squared distance у виборі палітри. Заміна всіх значень на sqrt змінить евристику й не є безпечним механічним виправленням. Одиниці skin slack та зміна евристики потребують окремого контракту й quality regression.
- `interval_value=0.25` у config не доводить фактичний інтервал Rust: Precision із force controls встановлює 0.01. Потрібні реальний readback і журнал запуску.

## Межі реалізованого

Три крапки — скінченне вимірювання. Static mask не гарантує swept coverage. Передусім потрібні Round/Square Size 1 A/B і повний audited Four Blocks Missing=0 / Unknown=0. Нового живого тесту beta.31 у цій перевірці не виконано. Пороги та старі відмови не перетворено на PASS. Планова симуляція покриття, swept-footprint калібрування, повний native input replay, staged Speed Probe, configurable safety policy та diagnostic ZIP залишаються відкритими.

Clipboard backend дублює підтримувані Win32 формати до зміни буфера, утримує backup до завершення transfer, повторює transient restore і не перезаписує нові зовнішні дані. Owner-display, приватні GDI та відомі OLE embedded/link формати не приймаються. Це не обіцянка збереження будь-якого довільного object clipboard. В ізольованому CI перевіряються empty/bitmap/file-drop. [Microsoft: OleDuplicateData](https://learn.microsoft.com/en-us/windows/win32/api/ole2/nf-ole2-oleduplicatedata), [ownership SetClipboardData](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setclipboarddata).

Integrity перевіряється через Windows token до вводу; автоматичного elevation немає. [Microsoft: GetTokenInformation](https://learn.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-gettokeninformation). Кількість SendInput не доводить сумісності з EAC; безпечність для anti-cheat у beta.31 не сертифіковано.
