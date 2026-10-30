# Row-offgrid пілот v1b — реалізований сценарій

25.10.2026. [Правилаv1a](ROW_OFFGRID_RESEARCH_PROTOCOL_V1A.md) збережені. Єдина змінаv1b до збору — **симетрична median(abs(residual))** для кандидата й rivals, за рецензією0068. Max/P95/mean/signedMedian звітуються окремо. Це не зміна RGB12/contrast80, completeness або контракту гарантованого Solid.

## Реалізація

- [TexelGrid/Target/Planner](../../src/CanvasForge.Core/TexelResearch.cs): явний experimental API, fitted coefficients/provenance/context/domain. Observation edge-label-span2 окремий від невідомої фізичної висоти штампа; input-family зберігається без довільного вибору одногоbeta/c.
- `CenteredRowScreenSpan` розв'язує спільний для родини інтервал рядка, знаходить цілийY із найбільшим мінімальним запасом; недостатній margin/неоднозначність дають відмову. Саме цим методом генеруються нові центровані траси й рядки планувальника. Кожна траса мусить точно збігтися із замороженим незалежним JSON.
- `TexelTarget` робить nearest-resampling у явно заданий index-rect із збереженнямalpha та незмінним snapshot. Ці розміри не називаються встановленою роздільністю текстури Rust. `TexelPlanner` будує горизонтальні логічні runs, центрує кінці по стовпцях і відхиляє недоведені row/margin/partial-alpha. Це draft без фізичної моделі width/caps/overlap/кольору, EstablishedSolidPixels0 і dispatch/productionfalse.
- [Manifest/reader](../../src/CanvasForge.App/StandardRowOffgridPilot.cs) фіксує assets/settings/probes/cases/source/binaries/context; новий thinPainter adapter викликає чинні contour runner/capture/per-track verifier/fresh-session guards.
- [Аналіз](../../src/CanvasForge.App/StandardRowOffgridAnalysis.cs) перевіряє всі дані перед арифметикою й вимірює кожен cumulative-prefix від одного blank stage-before. Стара plane1-реалізація та її used package не змінені.

Core API не під'єднаний до звичайного START чи перемикачаUI. Придатність профілів/кольорів і загальна фізична готовність не з'являються від офлайн-тестів.

## Frozen-входи

[protocol-v1b.json](../../tests/CanvasForge.App.Tests/Fixtures/RowOffgrid/protocol-v1b.json), SHA256 `78d88a380d124686a7c1b03c91b30a162394791ee9f30f4f665702377ad8ea9c`;
[row-family-v1.json](../../tests/CanvasForge.App.Tests/Fixtures/RowOffgrid/row-family-v1.json), SHA256 `9e341f94fbce597e2a9471fea26159b3c5144f6d522ac19fa79e3839387190cd`.

Геометричний кандидат0065dc3c5681…2e77 не ретюнінгувався. Context465,148..1510,1192/client2560×1440/origin0/DPI96/Shape3/Size3/Paletteblack та весь emitter/timing контекст незмінні. Пакет містить копії native-reference й налаштувань; старт підтверджує чистий baseline цієї сесії, історичний кадр лишається geometry-reference.

8cases/37releasedstrokes/1687points, без додаткової фарби або calibration:

- H1…H6: Y288/329/521/562/827/898, X580…700; bodyX596…684.
- Centered15: Y646…674 крок2, X930…960; minfamily margin0.2606063.
- BoundaryControl16: точні16трас0063, Y942…972/X930…960; bodyобохблоківX936…954.

Разом1123mandatorysections і37linked before/after/trace pairs. Missing/extra/duplicate/nonfinite/disconnected/clipped/order/context/hash errors даютьINCOMPLETE до будь-якої line/block арифметики. Caps окремі, жоден bodyX не відкидається як outlier.

## Рішення

Для кандидата residual unwrapped до очікуваного top-labelj/bottom-labelj+2. Для rivals — nearest-integer residual їхніх власних frozen training-фаз. **Обидва** використовують median(abs(residual)) по всіх обов'язковихbodyX на кожному краї кожноїH; не abs(median(signed)) та не signedMedian≤0.15. Candidate≤0.15 на6×2краях; коженrival має>0.15 на обохкраях≥3H. Max і nearest-rank P95 не впливають на вердикт. Неправильний цілий label кандидата не маскується modulo.

Joint-discrimination по всіх7training-піках і legacyV2, поріг0.25/≥3свідки, Step±1% від2.0384971778072845px/commandrow та класиzero/one/two/gaps зv1a не змінені. Кожен із14центр./15контр. переходів звітується; endpoint-average не приховує0/2. Причинна інтерпретація умовна щодоgrid/context/step; єдиної причини механізм не доводить. Повний статус завждиMEASURED_NOT_QUALIFIED, physicalUncertaintynull, production/physicalcertfalse.

## Запуск та захист спроб

[Готовий пакет](../../../../outputs/Pixora_Row_Offgrid_1_20261025/READ_FIRST_UA.md), [Start_Offgrid_Test.cmd](../../../../outputs/Pixora_Row_Offgrid_1_20261025/Start_Offgrid_Test.cmd). Offlineoffgrid-init/preflight/verify/review обробляються до Nativelease/WPF; ввід можливий тількиoffgrid-run після користувацькогоEnter/countdown. EOF/Q — скасування до спроби. Retained markers чиsample-folders перемикають повторнийRun лише вReview зі збереженням початкової причини. Новийlauncher використовує правильнийthrow у негативних шляхах; історичнийPlane_Experiment.ps1/usedpackage не переписаний.

Стара міткаOFFLINE у provenance-asset описує момент фіксації правил, а не стан збірки; runnable визначається новимcompiled manifest таBuild_Manifest. Стан нового bundle при складанніNOT_STARTED; файлиmarkers не створювалися.

## Перевірки та межі

[Receipt](../../../row-offgrid-implementation68-20261025/Receipt.json) розділяє Core/app/launcher/package/native. Тести охоплюють точні1687points, контекст/цілуgauge, незмінність масивів, підтримкуendpointів, alpha/логічніruns, симетріюмедіани, missing-last-control, невдалийafter-capture, відмову disconnected-prefix, Unicodeportable і retained-data. Nativeпари користувача не створювалися агентом.

[Офлайн24×24приклад](../../../../outputs/Pixora_Row_Offgrid_1_20261025/Offline_Only_Example24/target24-enlarged.png) — логічна ціль із прозорими рядками поза підтриманою активною смугою; [plan.json](../../../../outputs/Pixora_Row_Offgrid_1_20261025/Offline_Only_Example24/plan24.json) створенийTexelPlanner. Це не preview симульованої фарби й не результат Rust. Кольорова фізика/порядок накладання ще не доведені; живого case після37штрихів у цьому пакеті немає.

Dependency closure8.0.31 перевикористано зSHA-перевіркою, не clean-Windows certification або новийRID-publish. Physical quality, fullSolid/width/caps/overlap/color та штатна інтеграціяSTART лишаються відкритими.
