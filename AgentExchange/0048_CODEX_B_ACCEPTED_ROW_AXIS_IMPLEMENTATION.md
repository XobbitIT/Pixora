# 0048 · Codex · 23.10.2026 · прийнята мета B і горизонтальна вісь

Джерело рішення — пряма відповідь користувача «Так, приймаю цю основу» на питання `call_a291d356a6704d3f8324b1a45565c9a8`:0неочікуваних сходинок на H/V у текстурній сітці, обидва краї й ширина точно за target у текселях; екранний нахил проєкції допустимий. [Машиночитаний запис](../docs/quality/texel-quality-b-goal.draft.json) тепер userGoalAccepted=true. Це уточнення стану після0047, не зміна його історичного pending-запису. Observable/U та production gate не прийняті автоматично.

## Що реалізовано

[StandardRowPilot](../src/CanvasForge.App/StandardRowPilot.cs) у чинному contour entry:12 звичайних H-проходів X520→1430 на Y220…1100 з кроком80, по911 команд.8training+4held-out, детермінований SHA-порядок усередині груп. Body X560…1390:831 mandatory section/line, caps по40 окремо. [Повна заморожена політика](../docs/quality/ROW_AXIS_CHARACTERIZATION_V1.md).

Новий сценарій не використовує вертикальні N/β/c/ψ, не fit/inverse й не змінює звичайний START/target/settings/schema. Користувацький rect зберігається. Повторно використано contour runner, fresh rect/proof,1962native supports/zero-registration, clean baseline,16ROI/0.985/delta0 та tool/cursor/focus guards. Кожна лінія має окремий резерв, released before/after і trace. Спроба одноразова, після відмови дані не видаляються.

`row-review` перевіряє цілісність усіх12 пар, але витягує краї **лише8training** (7288 номінальних перерізів,6648body). Перед арифметикою all-mandatory completeness; пропущена held-out capture теж блокує review. Немає нового локального RGB/contrast допуску. D=0.3/1.2 лишається screen diagnostic, не B-verdict. Результат повних training — MEASURED_NOT_QUALIFIED; жоден synthetic/frozen/packaged PASS не видається за фізичну рівність.

Held-out краї не відкриваються автоматично. Перед ними потрібні frozen row-модель/метод вибору/observable/U/критерій/coefficients і code hashes. Raw власнику доступні — це процедурне відкладення, не криптографічна сліпота. Один блок не сертифікує часову стабільність, noise floor, інші Size/форми/напрямки, caps/стики/overlap/колір.

## Перевірки й артефакт

- Product/App test host Release:0warnings/0errors.5нових focused groups; fake12-pair collection, both-edge/width diagnostics, missing/duplicate/NaN/reverse/order, held-out leakage refusal, post-paint capture failure/release/no retry, context/control/frozen-path tamper і Unicode portability.
- Регресія на точних фінальних DLL:7texel groups,6UV groups/16frozen evaluations;244WPF cases включно84-layout DPI matrix. Core sources не змінювалися, нового повногоCore suite не заявляємо.
- Windows PowerShell5.1:7нових row launcher cases і7texel regression cases. Completion/Q/EOF/preflight/run/review errors/Check; fixture не має product/native references.
- Незалежний Python перерахунок:усі10932 точки й SHA-порядок збіглися з C# та manifest.
- 5packaged offline/portable checks з invalidDOTNET_ROOT:preflight, pre-live honestINCOMPLETE, launcherCheck, Unicode path, unknown-row command rejection. Протокол не отримав attempt/capture markers. Це не чистийWindows/RID publish.

Пакет: [Pixora_Row_Axis_Pilot_20261023](../../../outputs/Pixora_Row_Axis_Pilot_20261023/READ_FIRST_UA.md), version `1.0.14-rc.8+local-row-axis-1`. AppSHA `1101aec4c4cf6c5d35e0363d4dd460ede380c7d11ab17b6000268603e9cd2de7`, CoreSHA `0cb5b0d92bc1af2ad648918636cdc4a11cdba0a7d228ddad4154f2e449911cc4`, protocol `4a01c2dfaa63361a949a59db4b4f7d48adde1514fe4ced42eeda60cc6fa9f515`. Runtime8.0.31 перевикористано з перевіреним closure; нові лише product DLL. Core binary metadata змінилась через informational version; Core source/алгоритми незмінні.

[Receipt/Before/logs](../../row-axis-pilot-20261023/Receipt.json). Перший запуск UV regression посилався на неіснуючу теку `UV_Fixtures` і зупинився до тестів; з правильними frozen inputs усі6груп пройшли. Помилка команди й обидва логи збережені, код через неї не змінювали. Negative synthetic preflight у row-tests.log навмисно друкує InvalidDataException й очікується тестом.

## Межа результату і наступна дія

Прийнята мета й готовий вимірювальний сценарій; **фізичний дефект загального Standard ще відкритий**. Новий сценарій NOT_STARTED, nativeInput=false; productionEligible/physicalQualityCertified=false. Старі UV/texel frozen INCOMPLETE та270/276immutable пакети не переоцінено, дані не переписано. Вводу вRust, coefficient fitting, gitcommit/push чи передачіClaude не було.

Коли користувач окремо готовий: один запуск Start_Row_Test.cmd. Потім snapshot→8training→заморожена row-модель/measurement policy→відкладена незалежна перевірка; за відсутньої U сертифікації немає. Не просити знову погоджувати вже прийняту метуB. Автоматичний обмін зClaude лишається зупиненим;0048 локальний.
