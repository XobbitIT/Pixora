# Pixora 1.0.14-rc.11 — кандидат релізу (Standard + експериментальний тексельний маршрут)

Розпакуй увесь архів в окрему папку й запусти `Pixora.exe`. .NET 8 Desktop Runtime x64 уже в пакеті, окремо встановлювати його не треба. Незавершений малюнок попередньої збірки заверши тією збіркою: новий план не продовжує старий прогрес, і Pixora пояснить чому.

## Як почати
Картка **«Наступний крок»** на сторінці «Малювання» підказує, що робити, і підсвічує потрібну кнопку: відкрий зображення → у Rust відкрий редактор таблички, встанови Interval 0.01 та Opacity 1.00 і натисни «Підготувати до малювання» → «Почати». F6 — пауза, ESC — зупинка. Додаткові інструменти — **Налаштування → Розширений режим**.

## Кольори
- **«Авто — швидше»** бере найменше кольорів без помітної втрати точності (типово до 1.5 ΔE): менше змін кольору й штрихів, коротший час.
- **Логотипи й герби.** Якщо на межах кольорів багато дрібних перехідних відтінків, картка «Наступний крок» запропонує «Об’єднати краї»: менше зайвих ліній і коротший час.
- **Прямий HEX до 1024 кольорів.** Більше кольорів → довше; на малих цілях різниця ледь помітна. Кнопка «Порівняти 64 … 1024» показує кольори, штрихи, ΔE і плановий час кожного варіанта.

## Standard
Основний режим. Фізичні нерівності ліній Standard ще не сертифіковані як усунені.

## Тексельний маршрут Standard — експериментальний
**Налаштування → Експериментально.** Режим для чорно-прозорих зображень: 1 піксель файлу = 1 тексель таблички, без масштабування. Типово вимкнений.
- **Підтримується:** той самий вид полотна; Standard Size 3 круглий; чорний #000000 і прозорий фон; до 385×339 текселів (стовпці 75–459, рядки 198–536); деталі щонайменше 3 тексели завширшки і 2 заввишки. Усе інше відхиляється до вводу з причиною.
- **Перед малюванням:** на чистому полотні натисни «Калібрувати сесію» (3 короткі лінії, близько 2 хв). «Почати» потребує підтвердженого калібрування саме цієї збірки.
- **Відомі обмеження:** кінець штриха може не дотягнути до краю (зрізаний кут; у вимірах до ≈0.55 текселя) або вийти за край праворуч (зайвий тексель; у вимірах до ≈1.8 текселя від межі). Найчастіше це трапляється на кутах фігур і у фігурах висотою 2 тексели. Межа не гарантована; фізична якість не сертифікована.

## З’єднання штрихів — експериментальне
**Налаштування → Експериментально → «З’єднувати сусідні штрихи одного кольору»** (типово вимкнено). Пензель переходить між сусідніми рядками без відпускання кнопки лише всередині великих однотонних ділянок. Фізично ще не підтверджено.

Adaptive, кольори в тексельному режимі, інші Size і форми не входять у цей кандидат.

---

**English.** Unpack the whole archive into its own folder and run `Pixora.exe`; the .NET 8 runtime is included. The **Next step** card on the Painting page guides you from opening an image through Rust preparation (Interval 0.01, Opacity 1.00, Prepare for painting) to Start, highlighting the button to press; Settings → Advanced mode shows all tools. **Auto — faster** picks the fewest colors without a noticeable accuracy loss (1.5 ΔE by default); HEX Direct allows up to 1024 colors, and Compare 64 … 1024 shows colors, strokes, ΔE and planned time. Standard is the main mode; its physical line waviness is not certified as fixed. **Settings → Experimental** offers the Standard texel route for black-and-transparent images: 1 source pixel = 1 texel, Size 3 round, up to 385×339 texels, details at least 3×2 texels. Press **Calibrate session** on a clean Canvas before painting. Known limitation: a stroke end may stop short (cut corner, measured up to ≈0.55 texel) or extend past the edge (extra texel, measured up to ≈1.8 texels), mostly at figure corners and in shapes 2 texels tall; no bound is guaranteed and physical quality is not certified. For logos with many edge shades the Next step card offers Merge edges. Settings → Experimental also has experimental Standard stroke joining (off by default, not physically confirmed). inGameTest=false for this build until its live run is recorded.
