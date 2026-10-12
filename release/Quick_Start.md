# Pixora 1.0.14-rc.1 — Quick Start

Windows x64 · .NET Desktop Runtime included · F6 pause / ESC stop.

**Українська:** Розпакуй увесь ZIP, запусти Pixora.exe та відкрий зображення. Відкрий редактор Rust, вибери палітру або HEX і натисни «Підготувати до малювання». Обведи запитані області та активний колір палітри. Standard використовує круглий Size 3, Interval 0.01, Opacity 1 і стабільний ввід без обов’язкових відбитків чи тесту швидкості. Перевір прев’ю й натисни «Почати». Не рухай мишу під час вводу. F6 — пауза, ESC — стоп. 100% означає виконані команди: оглянь результат у Rust.

**English:** Extract the complete ZIP, run Pixora.exe and open an image. Open Rust’s painting editor, choose Palette or HEX and click Prepare for painting. Select the requested regions and active palette color swatch. Standard uses round Size 3, Interval 0.01, Opacity 1 and stable input without mandatory imprints or Speed Probe. Check the preview and click Start. Leave the mouse alone during input. F6 pauses, ESC stops. 100% means processed commands: inspect the result in Rust.

**Polski:** Rozpakuj ZIP, uruchom Pixora.exe i otwórz obraz. Otwórz edytor Rust, wybierz paletę lub HEX i przygotuj rysowanie. Zaznacz obszary oraz próbkę aktywnego koloru. Standard: okrągły Size 3, Interval 0.01, Opacity 1, stabilne sterowanie; pomiar pędzla opcjonalny. Sprawdź podgląd i naciśnij Start. Nie ruszaj myszą. F6 wstrzymuje, ESC zatrzymuje. Sprawdź wynik w Rust — 100% nie gwarantuje pokrycia.

**Deutsch:** ZIP entpacken, Pixora.exe starten und Bild öffnen. Rust-Editor öffnen, Palette oder HEX wählen und Zeichnen vorbereiten. Bereiche und aktive Farbprobe erfassen. Standard: runder Size 3, Interval 0.01, Opacity 1, stabile Eingabe; Pinselmessung optional. Vorschau prüfen und Start drücken. Maus nicht bewegen. F6 pausiert, ESC stoppt. Ergebnis in Rust prüfen — 100% garantiert keine Abdeckung.

**Français :** Extrayez le ZIP, lancez Pixora.exe et ouvrez une image. Ouvrez l’éditeur Rust, choisissez Palette ou HEX et préparez le dessin. Sélectionnez les zones et l’échantillon de couleur active. Standard : Size 3 rond, Interval 0.01, Opacity 1, saisie stable ; mesure facultative. Vérifiez l’aperçu et démarrez. Ne bougez pas la souris. F6 met en pause, ESC arrête. Vérifiez dans Rust — 100% ne garantit pas la couverture.

**Español:** Extrae el ZIP, ejecuta Pixora.exe y abre una imagen. Abre el editor de Rust, elige Paleta o HEX y prepara el dibujo. Selecciona las áreas y la muestra del color activo. Standard: Size 3 redondo, Interval 0.01, Opacity 1, entrada estable; medición opcional. Revisa la vista previa e inicia. No muevas el ratón. F6 pausa, ESC detiene. Revisa el resultado en Rust — 100% no garantiza cobertura.

**Русский:** Распакуй ZIP, запусти Pixora.exe и открой изображение. Открой редактор Rust, выбери палитру или HEX и подготовь рисование. Выдели области и образец активного цвета. Standard: круглый Size 3, Interval 0.01, Opacity 1, стабильный ввод; измерение кисти необязательно. Проверь превью и начни. Не двигай мышью. F6 — пауза, ESC — стоп. Проверь результат в Rust — 100% не гарантирует покрытие.

Focus loss during preparation pauses the current transaction. Return to Rust and press Resume. An incomplete physical trial is discarded; clear its traces before retrying. Changed DPI/editor geometry requires recapture.

Adaptive, Size 2, Speed Probe and auditing live in Experimental. They require measurements and have no guaranteed speed benefit. Standard does not certify physical coverage. Settings → Export diagnostics creates a ZIP with session/config/images and identified run snapshots. No upload occurs automatically.

Підготовка призупиняється при втраті фокуса. Повернись у Rust і натисни «Продовжити». Незавершена фізична проба відкидається; очисти її сліди перед повтором. Після зміни DPI чи геометрії редактора повтори захоплення. Adaptive, Size 2, тест швидкості й аудит — у «Експериментально», без гарантованого прискорення. Налаштування → «Експорт діагностики» створює ZIP із конфігурацією, журналом, зображеннями та позначеними snapshot запусків. Автоматичного надсилання немає.

User runtime excludes source, test screenshots, internal reviews and personal configuration. This RC has offline verification; a new Rust test is still required.
