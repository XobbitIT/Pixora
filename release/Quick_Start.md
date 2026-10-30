# Pixora Standard Quality candidate

Build: 1.0.14-rc.8+local-standard-quality-1. Focus: Standard drawing quality. Adaptive is deferred.

Захоплення вільне: Pixora зберігає точно обведений прямокутник, включно із запасом рамки. Відпускання миші завершує вибір. Автоматичного пошуку внутрішніх меж та Enter немає. Кнопки Rust і сторонні вікна залишаються зовні. Структурний proof і tool/input guards збережені. Запас рамки не означає, що Rust дозволяє фарбувати дерево.

Standard більше не розріджує проходи при Turbo/Max Speed: між сусідніми проходами 1 фізичний px. Деталізація target залишається окремим параметром: 5/8 px дають грубіше зображення. Виправлено розрідження команд; фізична хвилястість ще не сертифікована як виправлена. Ціна щільніших проходів — більше команд і часу, прогноз перераховується з нового плану.

## Короткий причинний тест якості

Файл Standard_Three_Lines_Same_Target.png містить тільки три вертикалі з попереднього Test_02, у тому самому положенні на прозорому 128×128 зображенні. Для перевірених смуг їхній target точно збігається з target останнього запису; замість повної рамки й кольорів лише 72 операції в новому плані.

Для зіставлення з останнім запуском: Standard, палітра Rust, Max Speed, клітина 8 px, Fit Square, edge merge OFF, звичайний круглий Size 3. Залиши той самий Canvas і layout, якщо вони ще не змінені; довільне нове виділення працює, але змінить масштаб порівняння. У Rust вручну Interval 0.01 / Opacity 1.00. На чистому полотні відкрий цей файл, пройди підготовку й намалюй до кінця. Клітина 8 тут потрібна лише для порівняння з попереднім target, а не як рекомендація високої деталізації.

У last-run автоматично записуються native-before/native-after, command-plan і metadata; потрібні ці файли та config/session. Відео необов'язкове. F6/Alt+Tab/вихід із редактора, різні форми пензля та Adaptive повторювати не потрібно.

Coverage revision змінилася: старий checkpoint не застосовується до нового плану. Новий START не перезаписує його до першого штриха. Старий малюнок можна завершити попереднім RC8.

Free capture keeps the exact user rectangle. Standard uses 1 px between coverage passes for all movement presets. This fixes sparse coverage commands, not proven physical waviness. The included three-line probe isolates density at the prior cell8/Max Speed settings; it is not a request to repeat pause/recovery or Adaptive tests. Runtime included. inGameTest=false for this changed build.
