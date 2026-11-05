# 0119 — Огляд продукту вимірює кінці рядків (реліз Б, крок 1)

Дата: 05.11.2026. Автор: Claude (локально). Codex — рівний партнер: коло 12 CHANGES_REQUIRED → виправлено → коло 13 **APPROVE** (`work/texel-endphase-20261104/codex-round12.md`, `codex-round13.md`; повні звіти — `work/codex-endphase-20261104/_codex/round12|13/`).

## Навіщо
Огляд продукту (правила pic8) не бачив зрізаних кутів і зайвих текселів на кінцях штрихів (0116, 0117): V-краї потребують ≥4 рядків. Через це навіть Product_1 мав PREDICTIONS_MET із зрізаним кутом. Для релізу Б (0118) обмеження мають бути видимі й виміряні.

## Що зроблено
- `src/CanvasForge.App/TexelReviewEnds.cs` (новий):
  - вимірюється кожен clear-кінець рядка цілі: тексель b зафарбований, b+1 порожній, b+1..b+3 порожні в сусідніх рядках;
  - EdgeStep від текселя b-1, residual = фаза сітки − (b+1);
  - верхній рядок фігури міряється у двох перерізах (+1 px усередині і центр); кінець виміряний лише тоді, коли виміряно обидва;
  - short: будь-який переріз < −0.15; extra: будь-який > 1.0;
  - статус: CLEAN / KNOWN_LIMITATION / UNMEASURED / NONE.
- `TexelProductReview.cs`: поле `Ends` у `summary.json`, файл `ends.csv`. CLI-рядок огляду показує `ends=… short=… extra=… measured=…`. **Вердикт PREDICTIONS_MET/NOT_MET і пороги не змінено.** `summary.json` успішного огляду тепер містить `Ends`, тож старі побайтові replay summary з новою збіркою вже не збігатимуться.
- Тести: `CheckTexelReviewEnds` на синтетичних кадрах:
  - кінець +0.5 → CLEAN; −0.5 → 6 short; +1.5 → 6 extra;
  - без центрального перерізу → UNMEASURED 4/6;
  - незмінений кадр → UNMEASURED;
  - правило clear.

  Тест контракту `summary.json` оновлено. Інструмент `--texel-review-ends RUN BEFORE OUT` міряє записані прогони. Старим маршрутам без RowMargin (до 0101) підставляється 0.25, так само в `--texel-run-ends`.

## Перевірено
- App: texel-product 3/3 (з розмірами й resume), texel-session-start, session1 3/3, pic8, pic9 — PASS.
- WPF: 246 PASS. Core не змінювався.
- Звірка з `--texel-run-ends` (`crosscheck_review_ends.py`) на 10 записаних прогонах: 0 розбіжностей. Codex незалежно отримав те саме.

| Прогін | Ends | short / extra | виміряно | min |
|---|---|---|---|---|
| Product_1, сесійний P1, UI 0111, UI 0112 | KNOWN_LIMITATION | 1 / 0 | 100/102 | −0.444 |
| Product_2 | CLEAN | 0 / 0 | 102/102 | +0.061 |
| Product_3 | CLEAN | 0 / 0 | 102/102 | +0.345 |
| Max 04.11b | KNOWN_LIMITATION | 15 / 18 | 2130/2139 | −0.495 |
| Corners baseline / cap | KNOWN_LIMITATION | 51 / 14 | 1712/1725 | −0.546 |
| Corners extend | KNOWN_LIMITATION | 0 / 252 | 1711/1725 | +0.056 |

Чотири прогони Product_1 у різні дні дали тотожні числа (той самий маршрут і місце). Рендер Rust тут детермінований, як і cap ≡ baseline у 0117.

## Межі
Блок інформаційний: він робить дефект видимим, але не виправляє його. Фізичний дефект кінця штриха лишається (0117). Дані: `work/texel-endphase-20261104/review_ends`. Rust-вводу агентом не було.

## Далі (реліз Б)
Крок 2: показати обмеження користувачеві в UI тексельного блоку та в документації. Крок 3: фінальна збірка, калібрування сесії в UI, STEAM_RELEASE.md.
