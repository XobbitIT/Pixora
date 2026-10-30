# 0051 · Codex · 24.10.2026 · row live: повний збір, training-only аналіз

Користувач виконав новий row-pilot3. Source protocol `451afe06b104fdf2fdcdb50fca572c5ea9c3bde0071711fd3dc54eca3d1d716a`, version `1.0.14-rc.8+local-row-axis-3`; [snapshot/receipt](../../row-live-20261024/Receipt.json). **DATA_COLLECTED12/12cases,12strokes,12linked pairs.** Runner UTC23.10T22:13:00.3502185…22:14:29.3752327 (Київ24.10 о01:13…01:14),≈89.025s із capture/guards/service; це не pure-motion benchmark або production ETA.

## Цілісність і підготовка

- 191файл source snapshot; packaged row-verify exit0; повторний row-review exit0, summary/training CSV byte-identical із launcher review.
- 267immutable build payloads точно за початковим Files_SHA256. Ще1build-entry — series-status.json, який за контрактом змінився NOT_STARTED→DATA_COLLECTED; його before/live hashes записані окремо. Це штатна lifecycle-зміна, не приховане оновлення fixture/manifest.
- 66PNG з companionRGBA hashes перевірені.12actual paths/10932points точно за протоколом;564sampled cursor readings без розбіжностей/failedread; down/up поодному на кожен штрих. Неперервна cursor path не доведена.
- RustPID23692,96DPI,origin(0,0),client2560×1440,Shape3/Size3/Palette, identity mapping, contour-v1. Fresh reference→baseline maxRGB4, baseline→confirmation2; RGB12 збережений.
- View-v2:1962supports,0missing, обидві unique registration(0,0), left corr0.9992358/right0.9991318, peak margins0.02750/0.02124.
- Live slider_check підтвердив Size3 двома кадрами (fraction0.02/0.02, expected0.020202…), writes0,clipboardUsed=false,numericReadback=false. Отже startup-fix0050 отримав **живий доказ already-correctSize гілки**. Вводу нового значення Unicode цим запуском не перевірено; спосіб клавіш лишається fake-covered. Немає clipboard failure або його обходу/очищення.

## Виміряно лише8training

**6648/6648mandatorybody**,831на кожнуH; **0D-events на обох краях у8/8ліній**, frozen D=adjacent≥0.3px/same sign/sum≥1.2px. Raw training capture sections7271/7288вимірюються;17`no_strong_paint` належать заздалегідь end_cap, жоденbody не пропущений. Caps не сертифіковано.

НезалежнийPython extractor повторив7288status/reason і7271пар меж із **максимальною різницею0px**.10synthetic math/negative controls перевірили H-індексацію,strict80/ambiguous/search, distributed/opposite/gappedevents,1.2boundary, missing-adjacent rejection іOLS. [Повні перерізи](../../row-live-20261024/Analysis/training-sections-independent.csv), [16країв/ширина/центр](../../row-live-20261024/Analysis/training-lines.json).

| Training Y | Mean width px | Width range px | Max residual top/bottom до власноїOLS,px | Median center−commandY,px |
| --- | ---: | ---: | ---: | ---: |
|220|5.3833|0.7941|0.1802 /0.4054|+2.1007|
|380|5.3604|0.5317|0.2733 /0.2261|+3.0900|
|460|5.2611|0.3835|0.1226 /0.1200|+1.2181|
|620|5.3958|0.8704|0.4491 /0.2410|+0.9735|
|700|5.1111|0.7537|0.2304 /0.2235|+2.4640|
|860|5.2770|0.5562|0.2426 /0.1818|+0.8685|
|940|5.1418|0.6784|0.2395 /0.2207|+1.6902|
|1100|5.1187|0.3270|0.0835 /0.1232|+0.7009|

OLS — опис кожного спостереженого краю, не target-fit/inverse mapping/U. Найбільший fitted drift уздовж830px тіла≈0.07989px; це не метрологічна межа геометрії. Median center−command змінюється +0.70…+3.09px, тому один offset не випливає з цих даних. Модель індексів рядків, texture resolution/inset, rendered-штамп і AA-край не ідентифіковані. Розкид 831перерізів однієїлінії не є20незалежними single-pass повтореннями; noiseU/часову стабільність ним не сертифікуємо.

## Вердикт і подальший scope

Frozen status **MEASURED_NOT_QUALIFIED**, productionEligible/physicalQualityCertified=false. Головний вузький результат: на цьому виді/Size3 чорні екранні H-команди дають повні body без великих D-сходинок. Це не доводить0будь-яких тексельних переходів, точну планову ширину, caps/стики/overlap/колір або готовністьStandard.

Held-outY300/540/780/1020 **не витягували, не переглядали як фарбу, не використовували для fit/selection**. Для них перевірено лише файл/RGBA/command/capture-integrity, як вимагає протокол; автоматичний reviewer також відкрив лише training. Коефіцієнти не fit; наступний крок — навчальна row-модель/ідентифікованість та freeze її family/observable/U/критерію/coefficients/code hashes до held-out. НевідомаU не замінюється0 і не підбирається післяконтролю. Нове малювання користувача на цьому кроці не призначено.

Продукт/пороги/джерела алгоритмів/бінарники не змінювали; новогоbuild/fullsuite/nativeinput агентом не було. Дві технічні поправки лише в audit-script: відокремити mutable live-status від build-payload і прочитати BOM session.jsonl якutf-8-sig. Дані/пороги/знаменники через них не змінювали. Старі failed attempts/INCOMPLETE збережені. Gitcommit/push/автоматичногоClaudeобміну немає;0051 локальний.
