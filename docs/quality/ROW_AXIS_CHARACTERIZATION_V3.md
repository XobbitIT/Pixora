# Row pilot3 — Size без буфера та явний результат у консолі

24.10.2026. Доповнення до [baseline v2](ROW_AXIS_CHARACTERIZATION_V2.md). Підготовка попередньої спроби зупинилась до першого штриха: `ClipboardFormatException`, format49345/`FileContents`, native error0, ownerPID2796. Читання payload не виконувалось агентом. Формат не вдалося безпечно скопіювати через звичайний Win32clipboard API; фактичний вміст/застосунок-джерело невідомі. Збереження вмісту буфера не послаблюємо.

## Size у вже вибраній Visual policy

- Два послідовні знімки чинним VisualControlVerification підтверджують геометрію Track/ValueField і дробове положення. Якщо Size уже правильний, немає редагування поля, clipboard capture/copy/paste/restore або введення клавіш.
- Якщо відоме стабільне поле має інше значення, Ctrl+A → guarded Unicode digit/decimal key events → Enter, потім два свіжі знімки. Межі, допуск0.75/TrackWidth і budget спроб не змінені. Без післяввідного підтвердження немає verifiedControls/paint-down.
- Перед кожним символом виконується чинний boundary guard; key-up уfinally навіть при скасуванні/помилці. Невідоме/нестабільне поле, неприпустимий payload або недоступний capture зупиняє підготовку.
- Visual policy має бути обрана наперед; exception clipboard не перемикає Clipboard policy на Visual. Чинні strict numeric/HEX clipboard lease, збереження сторонніх/virtual-only форматів, ownership і restore не змінені. Palette-шлях row-досліду не потребує HEX clipboard.

На записаному failure-frame RustSlider даєfraction0.02, очікувано0.020202…; це в межах чинного допуску. Offline replay двічі прочитав той самий збережений кадр і підтвердив Size3 з0writes. Це відтворення parser/гілки керування, не два нових незалежних native знімки. Живий запис клавіш уRust не виконувався; якщо гра не застосує такий ввід, візуальна перевірка має відхилити його.

## Console contract

`row-run` звітує про completion/failure вконсоль без модального MessageBox, який може лишитися позаду fullscreenRust. Інші UI/старі CLI зберігають свій dialog default. Launcher одразу показує Status/Reason для **обох** форм status JSON: runner `{result:{Status,Reason}}` і startup `{status,reason}`. Retained attempt як іраніше переходить тільки до review, неpaint. Відсутній/невідомий статус не є успіхом.

12траєкторій, split/order/body/caps, emitter/timing, RGB12/contrast80,16ROI/0.985/delta0, exactrect/freshbaseline та held-out policy незмінні. Version `local-row-axis-3`, manifest/app/core hashes нові; `standard-row-characterization-v2` і його measurement policy не змінюються. Старий INCOMPLETE0/12 і файли збережені без редагування. Це усунення startup-залежності й прихованого результату, не фізичний коректор Standard.
