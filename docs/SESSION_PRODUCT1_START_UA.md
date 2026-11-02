# Product_1 через session binding

Підготовка не виконує Rust-ввід і не змінює основний конфіг. Використовує frozen pictureA (300/203, 107 штрихів), профіль і родину без підгонки.

```powershell
.\tools\SessionProduct1_Experiment.ps1 -Mode Prepare -Runtime .\Release\Pixora.exe -Package .\Product1Session -MainConfig .\main-config.json -Binding .\Analysis_Session1\session-binding.json -SessionEditor .\Session1_Experiment\session-editor.json -Pic8Assets .\tests\CanvasForge.App.Tests\Fixtures\RowPicture8
.\tools\SessionProduct1_Experiment.ps1 -Mode Run -Runtime .\Release\Pixora.exe -Package .\Product1Session
.\tools\SessionProduct1_Experiment.ps1 -Mode Review -Runtime .\Release\Pixora.exe -Package .\Product1Session
```

Run відкриває звичайний UI з ізольованою копією основного конфігу. Користувач завантажує `Product_1.png`, перевіряє прев’ю і сам натискає «Почати». Повторний Run після появи last-run відмовляється; збережена спроба оглядається окремо. В основному UI також доступне підключення binding та відповідного `session-editor.json` лише в пам’яті поточного вікна, без перезапису налаштувань.

Binding pin — SHA точних байтів вибраного запису; файл перечитується під час побудови, підготовки й перед кожним штрихом. Будь-яка зміна вимагає нового явного підключення, не автоматичної активації. Відсутні/невідомі фізичні поля, неактивний статус, інший rect/таймінг/Size/slot/origin/client/DPI/profile/grid/App/Core відмовляються до paint-down. Requested speed зберігається окремо від фактичного Max Speed. Перед першим штрихом обов’язкові RequireView і frozen 16 ROI/0.985 з нульовим зсувом; новий reference при відмові не навчається.

Snapshot зберігає `session-binding.json`, `texel-session.json`, маршрут, exact rect before/after і fresh full view. Огляд використовує копію binding із snapshot та записані App/Core SHA, перевіряє його SHA, весь маршрут і fresh view; чинні RGB12/contrast80 та H/V-пороги незмінні. Вердикт — `PREDICTIONS_MET`, `physicalQualityCertified=false`.

**Сумісність бінарників:** binding `1542dd10…` описує SHA App/Core калібрувального прогону. Перекомпіляція змінює ці SHA, тому старий запис не дає допуску новим DLL. Це навмисна відмова, а не привід підмінити SHA або послабити перевірку. Цей патч не створює нового живого binding і не сертифікує нові бінарники. Реальний запис/кадри 0108 та P1/P2/P3 не входять до цього worktree; живий Product_1 лишається перевіркою користувача.

Офлайн-перевірки:

```powershell
dotnet run --project tests/CanvasForge.Core.Tests -c Release
dotnet run --project tests/CanvasForge.App.Tests -c Release -- artifacts/session-start --texel-session-start-only
dotnet run --project tests/CanvasForge.App.Tests -c Release -- artifacts/product --texel-product-only
```
