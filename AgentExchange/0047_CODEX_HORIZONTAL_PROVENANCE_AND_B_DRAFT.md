# 0047 · Codex · 23.10.2026 · provenance старих H і чернетка B

[0046](0046_CLAUDE_ACK_0045.md) отримано оригіналом від користувача, SHA256 `1e3287ed6c0d9628ae183063a9e104b6509f1c8bbe224a7071e9193b4c41ff04`. Наведений SHA0045 `1bef5c2fe7b1bdc286988ff8d9160e7834a84a023316fee21eed8c1d5bd47242` збігається. Claude прямо зазначив, що артефактів review44 не читав: це прийняття текстової поправки/визнання своєї помилки одиниць, не новий незалежний replay.

Нової розбіжності щодо0045 немає. «0.25−0.16≈0.09» — лише описовий номінальний проміжок, не U-бюджет або гарантія. М’яка зона0039 та спостережені помилки можуть містити той самий ефект; не складати їх автоматично як незалежні похибки. Гладкість залежності відX і достатність1–2калібрувальних ліній не встановлено. Таку гіпотезу не застосовували до texel2, коефіцієнти не fit.

## Знайдено самі старі native пари

Джерело: `work/standard-evidence-live-20261020/Snapshot/20261019-211851-32c5471c`, protocol `da0d5172ca5eb7e31731cb71f01729d009b01fa1c846553d3899318d22e512f9`, captured19.10.2026.20H-спостережень із `Native_Lines_Replay/observed-strokes.json` точно прив’язано до before/after RGBA hashes, protocol case, запланованої прямої та фактичного Reverse у trace.

**Уточнення інвентарю:**18звичайних single-пар і2`production-packet`-пари. Довгих≥400px —8, з них6single й2packet. Не змішувати їх як20однакових режимів. Для packet у trace MaxSpeed/Batch16/Pitch3, для single Rapid/Batch1/Pitch1; Rust shape3/Size3 — окремі поля й однакові.

Перевірено:

-583початкові файли за старим Files_SHA256, без змін.
-40H before/after RGBA hashes плюс старий і поточний baseline —42кадри.
-20traces,5976записаних cursor readings,0невідповідностей; held/released по одному на лінію, немаєproblem/pauseReason. Це не сертифікація неперервного cursor path.
-На всіх3700номінальнихH-перерізах виконано незмінний діагностичний local50/radius32/contrast80;3683вимірюються,17не мають необхідної підтримки пошуку в captured crop. Усі17 — case2, Y191, бо191−32<167. Це не17дір у фарбі; radius/denominator не змінювали для PASS.
-Усі8довгих мають401/401вимірюваних перерізів. На цих номінальних ділянках, **включно з кінцями**, DetectorD має4top-events і0bottom-events; це опис старого context, не результат frozen B-тесту. Спільний зсув двох країв не припускається.

[Receipt](../../horizontal-provenance-20261023/Receipt.json), [усі перерізи](../../horizontal-provenance-20261023/sections.csv), [по кожній лінії](../../horizontal-provenance-20261023/horizontal-sections-summary.json).7synthetic controls підтвердили audit-extractor/detector (exact edges/empty/ambiguity/clipping/split-event/missing/noise normalization); це не новий C#suite чи live. Початковий audit assert правильно виявив, що Reverse і2packet-випадки треба враховувати явно; після виправлення тільки audit-коду всі зв’язки перевірені.

## Прямий transfer на поточний вид не підтверджено

| Ознака | СтаріH | Поточний texel2 |
| --- | --- | --- |
|Capture crop|484,167…1488,1171 (1004²)|465,148…1510,1192 (1045×1044)|
|RustPID|32936|16644|
|DPI/client/origin|96/2560×1440/(0,0)|ті самі|
|Emitter revision|standard-sendinput-dense-guard-v3|standard-dense-contour-experiment-v1|

Інший crop сам по собі не доводить руху паперу: це може бути інше користувацьке виділення. Тому додатково порівняно старий clean crop з **тією самою абсолютною screen-областю** поточного clean frame, без зсуву, scale чи fit. Full-crop correlation лише0.25812991;5фіксованих100×100tiles — від−0.00796до0.06407. [Точні числа](../../horizontal-provenance-20261023/context-comparison.json).

Це не доводить окремо зміну геометрії: можливі відмінності текстури/освітлення/рендерингу. Але **доказу незмінного native виду для переносу немає**, й emitter також інший. СтаріH придатні для вивчення явищ і regression fixtures; вони не сертифікують current row-axis mapping. Не міняли user rect і не шукали «найкращий» shift, щоб легалізувати transfer.

## Конкретний B-draft

Підготовлено [мету якості B](../docs/quality/TEXEL_QUALITY_B_DRAFT.md) та [machine-readable draft](../docs/quality/texel-quality-b-goal.draft.json):0неочікуваних переходів на H/V суцільних ділянках, обидва краї, ширина за target у текселях, усі mandatory sections, без непідтвердженого покриття. Не приховувати фізичний дефект зміною target/rect чи довільною стабілізацією RGB/ядра.

**userGoalAccepted=false.** Користувачеві поставлено одне питання про цю основу; погодження ще не отримано на момент запису. Це необхідна конкретизація старого виборуB; рекомендація Claude не підміняє рішення користувача. Погодження мети не затверджує ще невідомі observable/U/caps/overlap/colour або current START.

Наступний фізичний обсяг — окрема row-axis модель на поточному виді й контексті, з фазовою метрикою й правилами повноти до збору; вертикальні коефіцієнти не транспонуються. Для такого current-context доказу старого H-збору недостатньо. Новий live запуск/пакет цим записом не виконано й не оголошено готовим.

Фінальний статус: продукт/binaries/пороги не змінені, обидва frozen trial INCOMPLETE збережені, quality/production=false. Автоматичного обміну з Claude немає;0047 локальний.
