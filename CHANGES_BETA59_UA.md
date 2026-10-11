# Pixora 1.0.14-beta.59 — узгодження калібрування, вводу та стану

Локальні зміни за збереженим живим тестом beta.58. Нову версію у Rust ще не запускали.

- STOP зберігає план і checkpoint для RESUME, включно з повторним STOP під час cleanup.
  Скасування pending preview, завантаження та підготовки й далі відкидає застарілу роботу.
- Кожний ReadSlider паркує курсор поза всіма numeric controls. Locator окремо
  виключає повну область пошуку tool/shape/controls; перевіряє фокус, process, DPI й геометрію.
- Перед вибором калібрувального кольору встановлюється відомий readable Size.
  Для Size 1/2 у палітрі active swatch перевіряється на Size 3, після чого requested Size
  повертається до крапки. Після source failure лишається readable Size для повтору.
  Це не підміна active proof на palette-cell proof і не зниження RGB tolerance.
- Звичайний Draw використовує StandardStrokeMotion та guarded SendInput для
  початку/кінця руху, як калібрування. Pitch, паузи й мінімальне утримання 40 мс збережені.
- Fresh START видаляє baseline попереднього запуску цього плану до запису нового
  checkpoint. Перерваний countdown не дає RESUME використати історичний кадр.
- Ручні успішні spatial/speed перевірки узгоджують stepper, history та readiness summary.
  Зміна Size відкидає непридатний spatial proof.
- Старий audit позначається як попередній; новий план не успадковує старий прогрес
  або coverage через BuildUi. Діагностика попереднього результату залишається доступною.
- Ранній ETA більше не показується як гарантоване «до». До 20 зразків це попередня
  оцінка з позначкою уточнення темпу. Модель початкових витрат SendInput не змінена.
- Логи `wide` тепер рахують Size більші за working Size; додано розподіл operations
  за Size. Це діагностика, а не доказ прискорення Adaptive.
- Нові UI-повідомлення перекладені сімома мовами.

Створено `ARCHITECTURE_UA.md`: схема поточної системи, перевірені слабкі зв'язки
і поетапна цільова архітектура. Повна перебудова MainWindow/Painter у цьому релізі не виконувалась.

**Сумісність:** StandardStrokeMotion.Revision входить до PlanIdentity.
Старий checkpoint від попередньої версії потребує нового START; його не можна
непомітно продовжити за зміненим протоколом. Профілі пензлів не послаблені.

**Межі:** лишаються непідтверджене фізичне покриття країв, користь Adaptive за
однакового target та точність початкового ETA. Збережені результати beta.58
не стають успішними заднім числом. Потрібен новий живий тест.

14 критичних файлів формул/критеріїв звірено SHA256 зі збереженим beta.58 source:
BrushFootprints, BrushLocalColor, BrushCalibrationBatch, SpeedCalibration,
ProbeAnalysis, ProbeSpatialCalibration, CoverageAudit, FinalCoverageAudit,
Planner, MeasuredColorPlan, MeasuredPlanSelection, MeasuredStrokeOptimization,
PaintTimingPlan, RemainingTime. Вони не змінені.

Офлайн: regression checks у Core та WPF; fake input для напрямів, endpoints,
переривань і Shift; checkpoint та baseline через тимчасові каталоги;
84 UI-макети у семи мовах і трьох DPI. Native clipboard smoke вимкнений;
Pixora.exe та ввід у Rust під час цих перевірок не запускалися.

Фінальні результати: **478 Core / 173 WPF PASS**, включно з 84 UI-макетами.
Незалежний агент повторно запустив уже зібраний Core DLL: **478 PASS**.
Обидві Release-збірки: **0 warnings / 0 errors**. SHA256: **14/14 незмінні**.
Докази збережені в `outputs/Pixora_beta59_Build_Evidence.json` та
`outputs/Pixora_beta59_checks` у робочому просторі чату. Живий тест beta.59 не виконано.
