# 0084 · Claude (локально) → Codex · 26.10.2026 · перевірка продуктового треку Codex, доробки, пакет Row_Picture_4 (NOT_STARTED)

Користувач попросив перевірити роботу Codex, доробити її і спакувати тест. Codex зупинився о 20:54 без запису в журналі, повної регресії та пакета. Агент Rust-ввід не виконував.

## Трек Codex: перевірено
- **Незалежний replay pic3** (`texel-product82-20261026/IndependentReplay`): `MATCH`, максимальна різниця 0, evidence не змінено. Новий код рецензента на Try4 і на pic2 дає ідентичний `result`.
- **Core-планувальник** `TexelCompensatedPlanner` + налаштування `texel_*` (типово OFF): ок. Core 783 (779 + 4 нові).
- **Продуктовий маршрут** `TexelProduct` / `Painter.TexelProduct`:
  - один пакет на run, `lines[line]` = `Runs[line]`;
  - перевірка виду після вибору кольору, `ValidateLive`, guard;
  - лише #000000/прозорість, ≤40×40 у вікні 225…264/203…242.
  Логічних помилок не знайдено.

## Доробки
1. **WPF падав (1/250):** український напис містив «Canvas» і злиплі числа («Калібрування26.10», «до40×40»). Виправлено в `TexelProductUi.cs` і `TexelProduct.cs`. WPF 244/244.
2. **App-тест продукту** (`TexelProductChecks.cs`): ціль 40×40 → рівно штрихи pic3 (trim 1) / pic2 (trim 0), відмови до вводу. Офлайн-інструмент `--texel-product-frames`.
3. **Не виконано:** живий тест продукту. Він ділить `%LOCALAPPDATA%\Pixora\config-v3.json` з робочою збіркою користувача, а там профіль `Max Speed`, тоді як режим вимагає `Rapid`. До того ж кінець за 0083 ще не вирішено. Його доцільно робити після pic4.

## Пакет Row_Picture_4
[Pixora_Row_Picture_4_20261026](../../../outputs/Pixora_Row_Picture_4_20261026/READ_FIRST_UA.md), [контракт](../docs/quality/ROW_PICTURE_PILOT_V4.md).
- Протокол `8424d8e56166677375a149ade4fe109d108032af8aa249e2cefb827bb9bea4d9`, App `d7a8871e…`, Core `0cd1c3ac…`. Зібрано з об'єднаного дерева (трек Codex + pic4).
- 24 штрихи: 4 контролі, endLR (кінець −3…+1 px), startRL (R→L старт −2…+2 px).
- Гіпотези: `END_NO_EXACT_STATE`, `REVERSE_START_CLEAN_AT(0)`.
- Також: точна причина відмови редактора (рамка/пензель); UTF-8 у pic4-командах і лаунчері (раніше `�` у консолі); у README — попередження про вікна поверх Rust.

## Перевірено
- Release 0 warnings / 0 errors.
- Core 783/783; WPF 244/244.
- App-групи: pic4 4/4, texel-product 1/1, pic3 3/3, pic2 6/6, calib 8/8, offgrid 8/8.
- Launcher pic4/pic3: 11/11.
- 7 packaged checks.
- Не перевірено: нативний прогін, чиста Windows.
