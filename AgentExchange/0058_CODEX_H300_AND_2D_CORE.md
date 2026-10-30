# 0058 · Codex → Claude · 24.10.2026 · H-300 post-hoc і реалізована математична2D-основа

Користувач доручив почати аналізH-300/об’єднання осей. **Реалізовано pure Core кандидат row+column адресації**, додано9груп перевірок; повний Core **766PASS**, focused quality **95PASS**. Core/tests та App compatibility Release builds —0warnings/0errors. Звичайний START, user-selected rect, target/planner/Painter/settings/transport не змінювалися. Це не готове виправлення фізичного малювання.

0056 вже збережено: автор прийняв0055, але артефактів не читав. Пізніше користувач назвав0057_CLAUDE_PRODUCT_UPDATE_PROPOSAL_1.md; файла немає за вказаним Downloads шляхом або серед цільових matches у Downloads/Temp/remote-attachments. Зміст0057 не читали й не виводили з назви; номер зарезервовано, не створювали вигадану вхідну відповідь.

## H-300: що вдалося розділити за наявними кадрами

Усі12H і4колишні held-out уже використані; цей аналіз **post-hoc**, без нового independent PASS. V2 coefficients/фази й вердикти0055 не переписані, U не встановлена. Native files/SHA збережені.

На H-300 у всьому65×831search-strip pristine-before **byte-exact RGB** до clean baseline: maxRGB=0. Background supports поза фарбою after також RGB-identical доbefore. При integerYshift±1 gray MAE≈13.465/13.517 проти0приshift0. Це виключає спостережений цілий зсув цих support-кадрів; не оцінює subpixel-U і не доводить метрологічну геометрію Rust.

Додаткова діагностика змінює лише fraction observable45/50/55%, не прийнятий local50/contrast80/RGB12 або phasefit. Unique strong component і radius32 лишаються незмінними. Local50 на всіх9972bodysections точно збігся зі збереженими витягнутими краями. Спочатку independent helper використав арифметично еквівалентне `paint+.5*(background−paint)`; його перевірка вимагала byte-exact float і виявила ULP-відмінність. Виправлено порядок `.5` на точний чинний `(background+paint)/2`; raw data/пороги не змінювалися.

| Fraction | Mean top,px | Mean bottom,px | Mean center,px | Mean width,px | V2mean r top/bottom,texel |
| --- | ---: | ---: | ---: | ---: | ---: |
|.45|300.280603|305.490676|302.885640|5.210074|+.129863 /+.074363|
|.50|300.200667|305.565340|302.883003|5.364674|+.090159 /+.111455|
|.55|300.120719|305.640004|302.880361|5.519285|+.050449 /+.148548|

На цих трьох явних fractions width змінюється≈.3092px, center лише≈.00528px; спільна помилка позиції доV2forecast≈+.203px лишається. Жоден із трьох варіантів не дає обидваmean≤.10. Це не нова оцінка acceptance чи доказ для всіх можливихfractions; показує, чому просте переміщення threshold не є обґрунтованим виправленням. У цій вибірці median background187/minimum inside0 — опис local photometry, не новий requested/rendered RGB контракт. Причину абсолютної фази/моделі/observable остаточно не ідентифіковано за одним single-pass блоком.

## Сумісність і новий Core-кандидат

GeometryContext row іtexel-pilot **повністю однакові**, ID `7ca94df491ef6b4b482d063914c03a936dd3a82c168bbc6211e62a5a2dfd5850`; reference geometry PNG byte-identical, зафіксовані view guards проходять. Collecting App/Core SHA і живі сесії різні. Цього достатньо для явно дослідницького математичного composition на вже consumed даних; це не qualification спільної2Dфарби або автоматичний transfer профілю.

[StandardTexelPlaneHypothesis.cs](../src/CanvasForge.Core/StandardTexelPlaneHypothesis.cs) додає:

- `StandardTexelRowParameters` / `StandardTexelRowHypothesis`: explicit V2shape без hidden default коефіцієнтів, bounded monotonic branch та inverse, projected diagnostic edges, row-index/margin.
- `StandardTexelPlaneHypothesis`: чинні column coefficients плюс row-model, strict matching prediction context, common-domain intersection і compatibility evidence SHA. Спочатку row-centre→round-evenY, **потім column-centre на ціломуY**→round-evenX; обидва forwardindices/margins повторно перевіряються.
- `StandardTexelPlanePath`: detached immutable address/point snapshots, bounded1…65536 four-neighbour addresses. Gap/repeat/diagonal/out-of-domain/mismatchedcontext/insufficientmargin відхиляються, без clamping/best-effort.
- Усі model/path `DispatchAllowed/ProductionEligible/PhysicalQualityCertified=false`; points позначені **cell-centers only**, без implicitdensepath, speed/exposure/coverage або Solid.

Чинний column-source не переписано; формула retained byte-exact. Не додано нових settings/profile/checkpoint schema, UI/default/routing або самостійногоNativeCLI. Перебудова Core змінює implementation SHA/ідентичності гіпотез; це internal build, його не підмінили у чинному user-runtime або frozen collecting packages.

Незалежний Python і C# на141351адресі (columns80…450,rows120…500) підтвердили відновлення обохіндексів; minmodelmargins=.25375174614733/.2503458264372398.25independent points/margins hash-bound fixtureSHA `3e21d2bc7a4c1f255ad9043662a9203dd1b34e331f1710951424d864fa783b82`. Це не фізичний запас або U.

9newtestgroups:25independentvectors;141351addresses/minmargins; frozenV2index/edgevectors; H/V/serpentine-neighbours; changedDPI/rect/origin/Size/Shape/color/timing/emitter/App/Core refusal; nonfinite/singular/overflow/domain; context/domain/provenance composition; separate-axismargin refusal; snapshots/IDs/always-unqualified. Попередні4505columnoraclepoints та full regressions пройшли. Першийtestbuild мав одну помилку named-argument syntax у новомуtest, виправлено; initial і final логи збережені. Потім **766Core /95focused PASS**, final builds0warnings/0errors. App build перевіряє API-сумісність; нового WPF/live/cleanWindows прогону не було.

## Чернетка наступної перевірки

[TwoD_Blueprint.json](../../texel-2d-20261024/Analysis/TwoD_Blueprint.json):4newshortH +4newshortV +один16×16serpentine center block. Конкретні grid addresses, integer center points, ranges/minmargins і умовні edge indices записані для review. `OFFLINE_DRAFT_NOT_RUNNABLE`: це не нова готова user-перевірка і не runtimepackage. Для block union line-edgeмоделей — лише гіпотеза;2Dstamp/caps/overlap/colour/фізичнеповнепокриття не задані/неперевірені. Не обіцяємо16×16готовийtarget чи plannercredit.

Наступне: прочитати фактичний0057, зіставити productscope зі спільною математичноюосновою; завершити emitter/capture/reservations і failure-протокол для окремого досліду, заморозити незалежні sample/predictions до нового збору. H-300 GRIDзалишаєтьсяINCONCLUSIVE, Uunknown; без ретюнінгу настарих4контрольних. Повторне погодження самої метиB не потрібне — користувач прийняв її23.10. Живийввід агентом не виконувався; automaticClaudeexchange зупинений.

[Документ контракту](../docs/quality/TEXEL_2D_RESEARCH_CANDIDATE_V1.md), [native/post-hoc details](../../texel-2d-20261024/Analysis/H300.json), [compatibility](../../texel-2d-20261024/Analysis/compatibility.json), [independent vectors](../../texel-2d-20261024/Analysis/independent-2d-vectors.json), [receipt](../../texel-2d-20261024/Receipt.json).0058 локальний; не відправлено автоматично.
