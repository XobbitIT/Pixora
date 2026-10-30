# 0091 · Claude (локально) → Codex · 26.10.2026 · пакет Row_Picture_6: правий край «повне покриття», NOT_STARTED

За рішенням користувача [0090](0090_USER_DECISION_RIGHT_EDGE_FULL_COVERAGE.md). Пакет: [Pixora_Row_Picture_6_20261026](../../../outputs/Pixora_Row_Picture_6_20261026/READ_FIRST_UA.md), [контракт](../docs/quality/ROW_PICTURE_PILOT_V6.md). Протокол `0933193313b5cd1b1a660c23ea6f70efc221edc579071c310e78bacb435fb96b`, App `46147756…`, Core `0cd1c3ac…`. Агент ввід не виконував.

## Правило кінця (з наявних кадрів, 0089)
- Для довгих run стан «довше» буває тоді й лише тоді, коли φ(x_кінця) − (b+1) ≥ +0.31: 22/22 випадків pic4/pic5-Try2/pic2/pic3.
- Стіна pic2 (4 px) при +0.313 дала «коротше». Тому кінець = **перший піксель із φ ≥ b+1.5**. Це +1 px до pic2 для всіх 79 run.
- Для продукту (Codex): замінити `endTrimPx` цим правилом, коли pic6 підтвердить.

## Оцінка (до збору)
- Як pic3.
- Верхні H-краї стартують на 1 px усередині фарби (артефакт r227).
- V-кінець: медіана у вікні [−0.15, +0.75].
- Прогноз: `PREDICTIONS_MET`.

## Перевірено
- Release 0 / 0.
- Core 783/783; WPF 244/244.
- App: pic6 3/3, pic5 3/3, pic4 4/4, texel-product 1/1, pic3 3/3, pic2 6/6, calib 8/8, offgrid 8/8.
- Replay pic4/pic3/pic2: `result` ідентичний.
- Launcher pic6/pic5/pic4/pic3: 11/11.
- 7 packaged checks.
- Не перевірено: нативний прогін.
