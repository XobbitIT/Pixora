# Спільна тексельна адресація: дослідницький кандидат v1

Оновлено25.10.2026 після review0059. [Код](../../src/CanvasForge.Core/StandardTexelPlaneHypothesis.cs), [автоматичні перевірки](../../tests/CanvasForge.Core.Tests/StandardTexelPlaneChecks.cs). Це pure Core без Native, налаштувань, planner credit або інтеграції у START. Ціль B залишається прийнятою користувачем; фізична кваліфікація відсутня.

## Координати й порядок обчислення

Обидві осі працюють у фізичних screen pixels одного явного `StandardBandResponseContext`. Видимий папір і fitted period не визначають розмір текстури Rust. Параметри передає викликач; прихованих default511/525, fit або перекалібрування в класі немає.

Стовпці — чинний `StandardTexelColumnHypothesis`: `u=N*(x+βx*(x−anchorX)−L(y))/W(y)+cX`, index=floor(u). Рядки — `q=y+βy*(y−anchorY)`, `v=S*q/(1−q/poleY)+cY`, index=floor(v)+offset. Фази projected local50 top/bottom зберігаються окремо від cursor phases.

Для спільного центру адреси `(column,row)`:

1. Аналітично інвертувати row phase до її середини, отримати неперервний Y та округлити round-to-even.
2. Обчислити центр column phase **на отриманому цілому Y**, потім round-to-even для X.
3. На цілій парі `(X,Y)` повторно перевірити обидва індекси, обидва мінімальні margins і спільний domain. Не підставляти неперервний Y в column inverse: це була б інша точка.

Мінімальні margins — вимога математичної моделі, не validated input-U або універсальний фізичний запас. Значення0.25 використано лише як явно задану умову дослідницьких векторів.

## Межі та відмови

- Ідентичний prediction-context обох осей, явний SHA джерела кожної та SHA compatibility evidence. Хеш прив’язує кандидат до даних, але не підтверджує фізичну правдивість caller-supplied параметрів або cross-session transfer.
- Domain — перетин domains осей, ніколи об’єднання. Невідома точка, нульова/нескінченна модель, singular branch, похідна phase≥1, вихід за bounds або недостатній margin → явна відмова. Clamping/best-effort немає.
- Шлях містить1…65536 різних послідовних чотирисусідніх адрес; пропуск, повтор і діагональний стрибок відхиляються. Адреси та points — detached readonly snapshots; IDs включають coefficients, domains, source/context/compatibility та implementation hash.
- `PointsAreCellCentersOnly=true`. Саме `CenteredPath` **не є dense screen-pixel path** і не модель швидкості/експозиції. Не інтерполювати мовчки й не підключати до Painter до окремого протоколу емітера та safety/capture перевірок.
- ProjectedEdges прогнозує той самий diagnostic local50. Він не створює Solid, Possible, виміряну U, caps, форму 2D stamp або модель накладань.
- Моделі та шляхи завжди `DispatchAllowed=false`, `ProductionEligible=false`, `PhysicalQualityCertified=false`.

## Докази та наступний дослід

За вже використаними матеріалами row/column мають однакові записані GeometryContext/rect/DPI/origin/client/Shape3/Size3/palette/timing та byte-identical reference view. Обидва live view guards пройшли. Collecting PID/бінарники різні; одночасного2D фізичного підтвердження немає. Зберігається `GRID=INCONCLUSIVE` для H-300 і невстановлена U.

Незалежний Python перевірив141351 центровану адресу: мінімальний model-margin поX0.2537517461, поY0.2503458264.25заздалегідь розрахованих точок і margins збережено як hash-bound independent fixture; C# перевіряє їх і весь domain. Це математичний round-trip, не повторне native малювання.

Окремі `DenseHorizontal` / `DenseVertical` повертають immutable `StandardTexelDensePath`. H проходить кожен цілий X при сталому центрованому Y; V заново рахує X із формули на кожному цілому Y. Поперечний індекс і мінімальний margin обов’язкові; уздовж лінії зміна індексу очікувана, margin лише записується. Пропуски, clamping і endpoint interpolation не дозволені. Endpoints проходять перевірку projected observation envelope. Тип не має дозволу на Native/Solid.

[Дослід v2](../../../../work/texel-2d-review59-20261024/Analysis/TwoD_Blueprint_V2.json):4короткіH+4короткіV для позиції,2довгі центрованіV з2екранними контролями, описовий Block16 із16 released H. Довгі траси мають847points і767mandatory body sections; модель прогнозує0D на центрованих і2на кожному краї кожного контролю. V-індекси low=k−3/high=k вже були у v1; у v2 також named fields.

Відбір коротких проб у локальному вікні±4 підвищує поперечний margin без after-даних. Вимога0,35 недосяжна на всіх рядках двох довгихV: у Y1037/Y765 найкращий integer margin0,25410/0,25479. Це не фізичнаU. Модель і старі verdicts незмінні.

Статус **OFFLINE_DRAFT_NOT_RUNNABLE**: немає прив’язаного Native runner/package і завершеної per-section/capture/no-retry policy. Block описовий без coverage verdict; union країв ліній не є2Dstamp/caps/overlap/колір. [v1](../../../../work/texel-2d-20261024/Analysis/TwoD_Blueprint.json) збережена. 3нові групи перевіряють10densepaths незалежними Python hashes/reverse, margins/відмови; full769/focused98PASS. Подробиці — [0060](../../AgentExchange/0060_CODEX_DENSE_2D_AND_REVIEW59.md).

Нову модель/допуск не перевіряти як independent на старих12H або texel/UV матеріалах: вони вже training/post-hoc. Ручний прогін цим документом не запускається.
