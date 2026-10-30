# 0042 · Codex · 23.10.2026 · виправлення хибної відмови texel-pilot

Локальний запис; автоматичного надсилання Claude немає.

Спроба0041 зупинилася **до фарби**: `INCOMPLETE`,0/10штрихів, `paintInputMayHaveOccurred=false`. Рух/відпускання курсора для підготовки могли відбутися. Старий protocol `f383f5de…927372`, geometry-check і статус збережені без rescore/перезапуску. [Snapshot і receipt](../../texel-view-failure-20261023/Receipt.json).

## Причина

Мій gate вимагав точного збігу1962 argmax picker0022. У лівому рядкуY399 оцінки X470/X467 були45.453333/45.440000, стали45.386667/45.440000: максимум змінився на3px при зміні RGB у його підтримці не більше2. ПраворучY821:73.746667/73.626667 →73.506667/73.533333, argmax змінився на1px при RGB≤1. Це нестійкість вибору майже рівних максимумів, а не доказ руху полотна на3/1px. По всій підтримці left/right maxRGB5/3. Сам старий gate не був каліброваним subpixel locator.

## Зміна

Лише ізольований texel-view gate та його manifest. Picker1962 залишається діагностикою: `Mismatched` — кількість відмінних argmax, більше не вердикт геометрії. Missing edge все ще блокує.

Нова policy `texel-view-native-support-zero-registration-v2`:

- Кожна з1962 native околиць58×5px порівнюється зі **старим незмінним UV-кадром**, на тих самих координатах. На кожній correlation≥0.985 та normalized chroma change≤0.05; жодних пропущених рядків або правила «1960достатньо».
- Для кожної сторони повна смуга58×985px повинна мати унікальну реєстрацію **(0,0)**. Пошук±3px тільки виявляє зсув; minimum peak margin0.001, ті ж константи CanvasStructure. Пошук не змінює rect, команду чи reference. Недостатня текстура/неоднозначність відхиляються.
- Новий manifest явно містить ViewPolicy, binary/context/asset bindings та новий collection/protocolID. Стара спроба не очищається і не стає придатною для повтору.

Це обґрунтована заміна помилкового способу порівняння після відмови, **не preregistered до старих даних** і не оцінка U або доказ subpixel accuracy. RGB12 не використано як довільний новий бюджет геометрії. Чинні16ROI/0.985/runtime delta0, tool/cursor/focus guards і full-Canvas RGB12 залишилися незмінними.

## Перевірка

Saved-frame replay: усі1962околиці проходять; мінімальна correlation left/right0.999671699/0.999909129; whole-strip0.999898907/0.999970042, peak margin≈0.027135/0.020937, нульовий displacement. Змодельовані native зміщення±1/2/3px по X та Y — **12/12 відхилено**; локальна5-row деформація теж відхиляється. Малий brightness+2 приймається. Flat/ambiguous та missing frame відхиляються synthetic checks. Це replay/штучні негативи, не новий фізичний тест Rust.

Незалежний Python повторив1962support та98registration offset stats:4120чисел, maxdifference1.5543122344752192e−15. [Результат](../../texel-view-failure-20261023/independent-replay.json).

Release App/Core0warnings/0errors.7focusedApp groups,244WPFcases включно84layouts — PASS. Native replay і fullWPF використовували ті самі product DLL hashes, що фінальний пакет; потім змінено лише силу текстури в synthetic positive test (слабка початкова fixture правильно відхилялася як ambiguous). Повторний focused на фінальному test host — PASS.5packaged/portable checks з invalid DOTNET_ROOT — PASS; pre-live reviewINCOMPLETE, статусNOT_STARTED не змінився. Початкові fixture/build-host-copy невдачі збережені; чиста фінальна збірка без warnings. FullCore не повторювали: Core-алгоритми не змінювали, змінився assembly informational version.

## Новий пакет

[Pixora_Texel_Pilot_2_20261023](../../../outputs/Pixora_Texel_Pilot_2_20261023), `1.0.14-rc.8+local-texel-pilot-2`:

- App SHA256 `3d6c8e0fda2cfad307ed44c05a130fd95b30cccb2df0ab59e8b6f8867f42ad18`.
- Core SHA256 `26b5a4187e8c23e844fab565c87eb7cc95720e39048b8d26e9609ca1bf0c2b08`.
- ProtocolID `d685ae89ad78671b2a8c5ac8f6d654800c116012e3594ac6220b92f1d4890417`.

Усі10paths/9010points, control forecasts, context/settings та reference hashes збігаються з0041. Коефіцієнти N/β/c, detector/verdict/denominators не підбиралися. Повторно використаний перевірений self-containedWindows8.0.31 runtime; не новий RIDpublish/cleanWindows. Нову спробу агент не запускав. **Виправлено хибне блокування запуску, а не доведено фізичну рівність.** Наступна дія — один явний запуск нового Start_Texel_Test.cmd користувачем та аналіз10nativepairs.
