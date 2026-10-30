# Горизонтальна вісь v2 — виправлення підготовки

23.10.2026. Це нова ревізія підготовки, а не переписування [замороженого v1](ROW_AXIS_CHARACTERIZATION_V1.md). Спроба v1 зупинилася до фарби:0/12штрихів. Її INCOMPLETE, файли й протокол лишаються незмінними.

Первинна причина: v1 порівнював новий baseline не лише зі свіжим підтвердженим кадром, а й із RGB-кадром історичної UV-сесії. У збереженій відмові fresh→baseline maxRGB2, baseline→confirmation maxRGB3; archive→baseline maxRGB36,1090880/1090980пікселів >12. Геометричний view-v2 пройшов. Встановлена відмінність тону, не встановлена причина його зміни; підганяння кольору/експозиції не робиться.

## Нова авторитетна основа baseline

`BaselinePolicy=explicit-fresh-confirmed-canvas-rgb12-v1`, bound у manifest v2. Користувач явно підтверджує чисте полотно перед новим запуском. Єдиний current clean reference — immutable `session-editor.png` цієї **явної підготовки** з protocol/settings/App/Core/PID/DPI/origin/client/hash binding. Він записується до Painter і до першого paint-down. Існуючі passive geometry/tool/cursor/focus перевірки лишаються обов’язковими.

Повний current Canvas порівнюється зі свіжим reference за незмінним RGB12; baseline-confirm теж має збігатися заRGB12. Одна зміна >12 поза резервами штрихів блокує. Таке ж порівняння виконується під час фінальної перевірки. Archive/geometry залишається immutable свідком фіксованого виду (1962support, correlation0.985, unique registration(0,0), жодних recrop/shift). Старий archive/baseline збережений для provenance, **не** є RGB-еталоном чистоти іншої сесії.

Це не автоматичне навчання на runtime-відмові: стару спробу не змінюємо, її fresh/baseline не підставляємо в інший протокол. Новий запуск потребує нового каталогу й нового явного підтвердження чистого полотна; CaptureFresh не викликається з mismatch-handler. Підтвердження користувачем чистоти — ручна передумова, не автоматична сертифікація відсутності старої фарби на початковому кадрі. Ні фізичний результат, ні колір не оголошуються PASS.

## Що лишилося незмінним

Усі12траєкторій/10932точки,8training/4held-out, їхній порядок, body/caps, search32, observable contrast>80/local50, D=0.3/1.2, RGB12,16ROI/0.985/delta0, emitter/timing, user rect і requested000000. Визначення row-моделі/U/приймальних критеріїв до held-out лишається відкритим; автоматично їх не витягуємо. Звичайний START, старі UV/texel протоколи та coefficients не змінені.

## Повторний клік на launcher

Якщо у каталозі збереглася спроба/кадри/sample-дані, Run не запускає preflight→paint знову. Він показує початкові Status/Reason і переходить лише до read-only review. INCOMPLETE/невідомий статус не перетворюється на успіх. Немає видалення markers, зміни вердикту, retry/resume або прихованого створення іншої спроби. Check залишається перевіркою готовності нового каталогу.

Відкат: окрема стара збірка/failed snapshot. Новий пакет має інший protocolID і version `local-row-axis-2`. Новий manifest не є дозволом повторно малювати стару спробу.
