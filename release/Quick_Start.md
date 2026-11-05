# Pixora 1.0.14-rc.9 — кандидат релізу (Standard + експериментальний тексельний маршрут)

Розпакуй увесь архів в окрему папку й запусти `Pixora.exe`. .NET 8 Desktop Runtime x64 уже в пакеті, окремо встановлювати його не треба. Незавершений малюнок попередньої збірки заверши тією збіркою: новий план не продовжує старий прогрес, і Pixora пояснить чому.

## Standard
Основний режим. Обведи полотно Rust, відкрий зображення, пройди підготовку й натисни «Почати». F6 — пауза, ESC — зупинка. Фізичні нерівності ліній Standard ще не сертифіковані як усунені.

## Тексельний маршрут Standard — експериментальний
Сторінка **«Експериментально»**. Режим для чорно-прозорих зображень: 1 піксель файлу = 1 тексель таблички, без масштабування. Типово вимкнений.
- **Підтримується:** той самий вид полотна; Standard Size 3 круглий; чорний #000000 і прозорий фон; до 385×339 текселів (стовпці 75–459, рядки 198–536); деталі щонайменше 3 тексели завширшки і 2 заввишки. Усе інше відхиляється до вводу з причиною.
- **Перед малюванням:** на чистому полотні натисни «Калібрувати сесію» (3 короткі лінії, близько 2 хв). «Почати» потребує підтвердженого калібрування саме цієї збірки.
- **Відомі обмеження:** кінець штриха може не дотягнути до краю (зрізаний кут; у вимірах до ≈0.55 текселя) або вийти за край праворуч (зайвий тексель; у вимірах до ≈1.8 текселя від межі). Найчастіше це трапляється на кутах фігур і у фігурах висотою 2 тексели. Межа не гарантована; фізична якість не сертифікована.

Adaptive, кольори в тексельному режимі, інші Size і форми не входять у цей кандидат.

---

**English.** Unpack the whole archive into its own folder and run `Pixora.exe`; the .NET 8 runtime is included. Standard is the main mode; its physical line waviness is not certified as fixed. The **Experimental** page offers the Standard texel route for black-and-transparent images: 1 source pixel = 1 texel, Size 3 round, up to 385×339 texels, details at least 3×2 texels. Press **Calibrate session** on a clean Canvas before painting. Known limitation: a stroke end may stop short (cut corner, measured up to ≈0.55 texel) or extend past the edge (extra texel, measured up to ≈1.8 texels), mostly at figure corners and in shapes 2 texels tall; no bound is guaranteed and physical quality is not certified. inGameTest=false for this build until its live run is recorded.
