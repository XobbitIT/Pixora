# 0050 · Codex · 24.10.2026 · Size clipboard failure і явний console result

Користувач повідомив: післяEnter у консолі немає видимого результату. Прочитано вихідні файли row-pilot2, snapshot зSHA збережено до змін. Перша спроба 24.10 о00:56 Київ (UTC23.10T21:56) зупинилась перед runner/paint у `PrepareBrushCalibration→Slider→BeginClipboard→ClipboardBackup.Capture`: `ClipboardFormatException`, format49345/`FileContents`, error0, ownerPID2796. Джерело/вміст буфера не встановлювали, payload не читали. StatusINCOMPLETE, completedStrokes0, paintInputMayHaveOccurred=false. Подальший клік виконав read-only review; його механізм не був причиною початкової відмови.

## Зміни

1. [VisualSizeEntry](../src/CanvasForge.App/VisualSizeEntry.cs) і дві visual-гілки `Painter.Slider/SliderMatches`: уже вибрана Visual policy більше не відкриває clipboard lease навіть для підтвердження. Уже правильний Size — два послідовні знімки,0writes. Відоме стабільне поле з іншим значенням — guarded digit/decimal Unicode keys, Enter, обов’язкові два післяввідні знімки; без них не підтверджується. Невідомі/missing/layout-changed дані зупиняють ввід. Key-up уfinally; guard перед кожним символом. Чинний допуск, retry budget, shape/tool/cursor/frame proof і paint safety не послаблені.
2. Strict numeric/HEX clipboard path, virtual-only format refusal, sequence/owner/restore не змінені; exception не переводить policy зClipboard уVisual. Зміна стосується налаштування Size у Visual mode, зокрема Standard; це виправлення спільного Painter, не тільки hardcoded rowSize3.
3. `row-run` отримує console-only presentation і console exception; завершення/відмова не чекає модального result MessageBox. Інші UI/CLI зберігають dialog default. Launcher показує Status/Reason одразу після run і при повторному review для обох JSONформ: runner-result та startup-flat. Retained-data лишаються одноразовими; немає reset/retry/repaint.

[Контракт доповнення v3](../docs/quality/ROW_AXIS_CHARACTERIZATION_V3.md). Manifest row-revisionv2, measurement/baseline/view policies, settings/context,12cases/10932points, split8/4/order/body/caps/colour/timing точно незмінні. App/Core binary hashes змінені з новою version; Core source не змінювався. Старі дві спроби/пакети залишилися незмінними, INCOMPLETE не переписано.

## Докази

- [Saved-native replay](../../row-clipboard-failure-20261024/Visual_Size_Checks/visual-size-native-replay.json): parser на frame, що передував failure, даєfraction0.02 при expected0.020202… у межах чинного0.75/250px; helper викликає read двічі й повертає Verified із0writes. Обидва replay reads використовують один saved frame — це не дві нові незалежні native capture й не живий keyboard тест.
- 5нових focused groups: already-correct/no-edit, no silent policy downgrade, key entry + післяввідне підтвердження, missing/layout/wrong/unresponsive rejection, cancellation/key-release/guard/payload boundary, console-only result із первинноюпричиною, saved-image replay. Фізичний Rust/clipboard не керувався.
- 5row/7texel/7frontier regression groups,11WindowsPS5.1launcher cases включно startup-flat. Попередні fake no-retry/mandatory-heldout bindings збереглися.
- Product і App-test host Release0warnings/errors; повний WPF результат зафіксовано у[receipt](../../row-clipboard-failure-20261024/Receipt.json) після завершення. FullCore не повторювали, джерелаCore незмінні.
- 5packaged checks: preflight, honestINCOMPLETE pre-live, launcherCheck, Unicodeportable, actual-flat-failure review із сумісним старимEXE+newlauncher. Real archived startupReason виведено, run/preflight не викликані й дані byte-identical; invalidDOTNET_ROOT у child. Clipboard-roundtrip, що змінює користувацький буфер, не запускали.

Пакет [Pixora_Row_Axis_Pilot_3_20261024](../../../outputs/Pixora_Row_Axis_Pilot_3_20261024/READ_FIRST_UA.md), version`1.0.14-rc.8+local-row-axis-3`, protocol`451afe06b104fdf2fdcdb50fca572c5ea9c3bde0071711fd3dc54eca3d1d716a`. Runtime8.0.31reuse, не cleanWindows/RID publish. NOT_STARTED/nativeInput=false; фізичний коректор/production eligibility/qualification не ввімкнені.

Наступний живий збір — тільки явний Start_Row_Test.cmd нової папки користувачем. Дані попередніх відмов не є training і не змішані з новими. Потім8training→freeze row model/observable/U/критерій→held-out. Згода наB вже є; вдруге її не запитуємо. Нового nativeRust вводу, coefficientfit, gitcommit/push чи передачіClaude немає;0050 локальний.
