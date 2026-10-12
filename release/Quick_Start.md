# Pixora 1.0.14-rc.1 — Quick Start

Windows x64 · .NET Desktop Runtime included · F6 pause / ESC stop.

**Українська:** Розпакуй увесь ZIP, запусти Pixora.exe та відкрий зображення. Відкрий редактор Rust, вибери палітру або HEX і натисни «Підготувати до малювання». Обведи запитані області; активний колір палітри знаходиться автоматично. Standard використовує круглий Size 3, Interval 0.01, Opacity 1 і стабільний ввід без обов’язкових відбитків чи тесту швидкості. Перевір прев’ю й натисни «Почати». Не рухай мишу під час вводу. F6 — пауза, ESC — стоп. 100% означає виконані команди: оглянь результат у Rust.

**English:** Extract the complete ZIP, run Pixora.exe and open an image. Open Rust’s painting editor, choose Palette or HEX and click Prepare for painting. Select the requested regions; the active palette color sample is found automatically. Standard uses round Size 3, Interval 0.01, Opacity 1 and stable input without mandatory imprints or Speed Probe. Check the preview and click Start. Leave the mouse alone during input. F6 pauses, ESC stops. 100% means processed commands: inspect the result in Rust.

**Polski:** Rozpakuj ZIP, uruchom Pixora.exe i otwórz obraz. Otwórz edytor Rust, wybierz paletę lub HEX i przygotuj rysowanie. Zaznacz obszary; próbka aktywnego koloru zostanie znaleziona automatycznie. Standard: okrągły Size 3, Interval 0.01, Opacity 1, stabilne sterowanie; pomiar pędzla opcjonalny. Sprawdź podgląd i naciśnij Start. Nie ruszaj myszą. F6 wstrzymuje, ESC zatrzymuje. Sprawdź wynik w Rust — 100% nie gwarantuje pokrycia.

**Deutsch:** ZIP entpacken, Pixora.exe starten und Bild öffnen. Rust-Editor öffnen, Palette oder HEX wählen und Zeichnen vorbereiten. Bereiche erfassen; die aktive Farbprobe wird automatisch gefunden. Standard: runder Size 3, Interval 0.01, Opacity 1, stabile Eingabe; Pinselmessung optional. Vorschau prüfen und Start drücken. Maus nicht bewegen. F6 pausiert, ESC stoppt. Ergebnis in Rust prüfen — 100% garantiert keine Abdeckung.

**Français :** Extrayez le ZIP, lancez Pixora.exe et ouvrez une image. Ouvrez l’éditeur Rust, choisissez Palette ou HEX et préparez le dessin. Sélectionnez les zones ; l’échantillon actif sera trouvé automatiquement. Standard : Size 3 rond, Interval 0.01, Opacity 1, saisie stable ; mesure facultative. Vérifiez l’aperçu et démarrez. Ne bougez pas la souris. F6 met en pause, ESC arrête. Vérifiez dans Rust — 100% ne garantit pas la couverture.

**Español:** Extrae el ZIP, ejecuta Pixora.exe y abre una imagen. Abre el editor de Rust, elige Paleta o HEX y prepara el dibujo. Selecciona las áreas; la muestra activa se localizará automáticamente. Standard: Size 3 redondo, Interval 0.01, Opacity 1, entrada estable; medición opcional. Revisa la vista previa e inicia. No muevas el ratón. F6 pausa, ESC detiene. Revisa el resultado en Rust — 100% no garantiza cobertura.

**Русский:** Распакуй ZIP, запусти Pixora.exe и открой изображение. Открой редактор Rust, выбери палитру или HEX и подготовь рисование. Выдели области; образец активного цвета будет найден автоматически. Standard: круглый Size 3, Interval 0.01, Opacity 1, стабильный ввод; измерение кисти необязательно. Проверь превью и начни. Не двигай мышью. F6 — пауза, ESC — стоп. Проверь результат в Rust — 100% не гарантирует покрытие.

Focus loss during preparation pauses the current transaction. Return to Rust and press Resume. An incomplete physical trial is discarded; clear its traces before retrying. Changed DPI/editor geometry requires recapture.

Adaptive, Size 2, Speed Probe and auditing live in Experimental. They require measurements and have no guaranteed speed benefit. Standard does not certify physical coverage. There is no diagnostic export button. Logs and images are in %LOCALAPPDATA%\Pixora; share needed files manually.

Підготовка призупиняється при втраті фокуса. Повернись у Rust і натисни «Продовжити». Незавершена фізична проба відкидається; очисти її сліди перед повтором. Після зміни DPI чи геометрії редактора повтори захоплення. Adaptive, Size 2, тест швидкості й аудит — у «Експериментально», без гарантованого прискорення. Кнопки експорту діагностики немає. Журнали та знімки зберігаються в %LOCALAPPDATA%\Pixora; надсилай потрібні файли вручну.

User runtime excludes source, test screenshots, internal reviews and personal configuration. This RC has offline verification; a new Rust test is still required.

Palette recovery requires a visible active color display. Remove server panels covering Rust’s brush preview; F6 resumes painting, Resume in Pixora retries preparation. The old checkpoint is retained until new paint input.

Для відновлення палітри потрібен видимий активний колір. Прибери панелі сервера з прев’ю пензля Rust. F6 продовжує малювання; «Продовжити» у Pixora повторює підготовку. Старий checkpoint зберігається до нового вводу малювання.
