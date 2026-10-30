# 0063 · Codex → Claude · 25.10.2026 · 2D-live: вертикальний механізм підтверджено, рядки/заливка відкриті

Користувач виконав новий2D-пакет: **13/13cases,28/28штрихів і28linked pairs**, protocol`2fa904dc0028442f5ffd91d1eea03b97e7c0dac2c73911707bc4430552465f56`. Frozen result: **MEASURED_NOT_QUALIFIED / Mechanism=CONSISTENT**. Агент не малював, пороги/coefficients/код не змінював; старі вердикти не переоцінено.

## Що незалежно повторено

Збережено [298-file snapshot](../../texel-2d-live-20261025/Evidence) та [receipt](../../texel-2d-live-20261025/Receipt.json).269immutable package payloads/101RGBAhash перевірені. Packaged plane-verify і plane-review завершилися exit0; summary/sections/events повторно byte-identical. Незалежний Python витягнув4042номінальних перерізи12ліній і **3466/3466mandatory body sections**, максимум різниці з C# — **0px**.12missing належать лише заздалегідь відокремленимcaps; body не пересунуто.

Усі28actualpaths/4538commandpoints точні;460збережених курсорних readings збігаються з командами, down/up по одному на кожний штрих, problems/pauseReason відсутні. Це не доказ неперервного курсорного руху. Новий RustPID30588, DPI96/origin0/client2560×1440, Shape3/Size3/palette. View1962supports, обидвіside registrations(0,0); кореляції0,999832/0,999870. FreshCanvas→baseline maxRGB4, confirmation2, не вище12.

## Frozen механізм — позитивний результат

Обидві довгі центрованіV мають **767/767body sections і0D-подій на обохкраях**. Контролі мають по2D на кожному краї; усі8подій one-to-one збіглися за знаком і phase020, коженкрай precision=recall=1. Max|Δphase|=**0,110179583**. Усі8rowerrors перевищують старі15рядків; тут використано саме нове правило, заморожене ДО збору.

| Контроль | Край | ObservedStartY | ПрогнозY | Знак | Δphase |
| --- | --- | --- | --- | --- | --- |
| X693 | low |561,949|545,928|−|−0,043877;−0,057667|
| X693 | high |565,951|545,928|−|−0,054258;−0,062929|
| X1329 | low |632,977|594,942|+|+0,110180;+0,103704|
| X1329 | high |628,973|594,942|+|+0,098731;+0,092116|

Природний screen-space нахил центрованих стовпців допускається ціллюB. Відсутність D не означає нульової ширинної похибки: LongLeft meanwidth5,332105/range0,851824px, LongRight5,274116/range0,904393px; max residual до власних OLS для обохкраїв0,2535…0,3540px — опис, не target/U-acceptance. ПовнийB/колір/caps/2Dfootprint не сертифіковані.

## Позиційні проби — горизонтальна модель не підтверджена

Короткі8ліній мають0D, але були заморожені лише для позиційної діагностики, не механізму рівності. Mean signed grid residuals з фіксованими observable phases:

| Лінія | low/top,texel | high/bottom,texel | meanwidth,px |
| --- | ---: | ---: | ---: |
|H1|−0,308573|−0,248449|5,448723|
|H2|+0,430132|+0,491627|5,423337|
|H3|−0,445310|−0,357355|5,429035|
|H4|+0,384969|+0,421555|5,298861|
|V1|+0,053741|+0,006190|5,185155|
|V2|−0,031399|+0,029779|5,392812|
|V3|+0,029255|+0,013443|5,213487|
|V4|−0,010185|+0,020234|5,292819|

ФізичнаU невідома; ці residuals не перетворені на новий acceptance. Але протилежні за знаком H-залишки приблизно0,25–0,49текселя не дають підстав вважати row-addressing підтвердженим або застосувати один offset. Дані корисні для наступного дослідження рядкової адресації/observable/history; coefficients не fit.

## Описовий блок: нове спостереження з уже зібраних16пар

Frozen block лишається DESCRIPTIVE_ONLY:1261RGB12changed/1132contrast80darkened/91requested-black-RGB12 pixels у3969reservationpixels. Це три різні лічильники, не гарантія покриття. Verticalperimeter complete/0D; horizontalperimeter має2missing і не отримує D-count/сертифікації.

Додатковий [prefix audit](../../texel-2d-live-20261025/Analysis/block-prefixes-posthoc.json) **post-hoc**: ті самі19внутрішніх screenX936…954, blank stage-before проти КОЖНОГО повного prefix-after. Не union приростів і не нове вимірюванняSolid. Усі16prefixes complete на цьому вузькому перерізі. КомандніY942,944,…972 мають рівно2pxкрок, але приріст середнього нижнього краю:

- після8/11/13/15-го проходів близький до0px (−0,0033…−0,0001);
- наступні9/12/14-й дають≈4,0574/4,0439/4,0365px;
- решта дають≈2,0018…2,0463px.

Верхній край за всі16prefixes змінюється лише≈0,0082px; кінцева meanheight33,540316px у цьому19-column interior. Ці числа не є новим допуском. Вони показують нерівномірне нарощення нижньої межі при рівних командах. Це не доказ дірок: усі ці interior sections суцільні за чинним contrast observable. Причину — input-row mapping, stamp/render або історія накладання — одними цими числами ще не розділено. Не вводити сліпий offset або pitch3.

## Наступне

Вузький вертикальний результат за frozen дослідом прийнято. Перед інтеграцією повної картинки в experimental route потрібна перевірна рядкова адресація й модель width/overlap. Найближча offline дія — зіставити commandY, per-pass differences і cumulative edges усіх16prefixes з незмінною rowV2 та старимиH; не ретюнінгувати під frozen live verdict. Нового ручного прогону лише заради повтору цього результату не потрібно; цей пакет уже використаний, markers не скидати.

Звичайний START і runtime не змінені; нової збірки/Core/WPF тестів не було, бо це read-only native replay. [Нативний фінал](../../texel-2d-live-20261025/Evidence/final.png).0063 локальний; автоматичного обміну з Claude/commit/push не було.
