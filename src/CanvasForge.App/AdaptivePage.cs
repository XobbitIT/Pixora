using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CanvasForge.Core;

namespace CanvasForge.App;
internal sealed partial class MainWindow
{
    private TextBlock adaptiveStatus = new(), adaptiveSummary = new(), adaptivePreparation = new(), adaptiveResult = new();
    private CheckBox adaptiveEnabled = new();
    private Button adaptiveCalibrate = new();
    private Button adaptiveRetry = new();
    private string adaptiveFailure = "";
    private void ShowSpeedSetup()=>ShowPage("speed");

    private void BuildAdaptive()
    {
        var page = FormContent();pages["adaptive"] = Scroll(page);
        page.Children.Add(Text(T("Пензель", "Brush"), 24));
        page.Children.Add(Card(T("1. Підготовка", "1. Preparation"), out var preparation));
        adaptivePreparation = Text("", 12); preparation.Children.Add(adaptivePreparation);
        preparation.Children.Add(Button(T("Захопити полотно й керування Rust", "Capture Canvas and Rust controls"), () => ShowPage("capture")));
        preparation.Children.Add(Text(T("Форма пензля для калібрування", "Brush shape for calibration"), 11, Muted));
        var shapes = new UniformGridCompat(2);
        foreach (var (name, slot, label) in Enumerable.Range(1,7).Select(slot=>(slot==3?"Round":slot==4?"Square":$"Shape {slot}",slot,
            slot==3?T("3 · Суцільний круглий","3 · Solid round"):slot==4?T("4 · Суцільний квадратний","4 · Solid square"):T($"Форма {slot}",$"Shape {slot}"))))
        {
            var selected = settings.Int("brush_shape_slot", 3) == slot;
            var shapeButton=Button((selected ? "✓ " : "") + label, () =>
            {
                if (selected) return;
                ReadSettings(); settings.Set("brush_shape", name); settings.Set("brush_shape_slot", slot);
                settings.Data.Remove("brush_calibration_points");settings.Data.Remove("brush_calibration_context");
                settings.Set("adaptive_brush", false); adaptiveFailure = ""; Dirty(); BuildUi();
            }, selected);shapeButton.Tag=$"brush-shape:{slot}";shapes.Add(shapeButton);
        }
        preparation.Children.Add(shapes.Panel);
        preparation.Children.Add(Text(T("Режим кольорів: ", "Color mode: ") + Option("color_mode",settings.Text("color_mode")) + ". "
            + T("Змінюється в розділі «Малювання».", "Change it on the Painting page."), 11, Muted));

        page.Children.Add(Card(T("2. Автоматичне калібрування", "2. Automatic calibration"), out var calibration));
        adaptiveStatus = Text("", 13); calibration.Children.Add(adaptiveStatus);
        calibration.Children.Add(Text(T("Size — значення в Rust, а не діаметр у пікселях. Три незалежні крапки визначають можливий слід та стабільне суцільне ядро від координати миші.", "Size is the Rust control value, not a diameter in pixels. Three independent dots measure the possible footprint and stable solid core relative to the mouse command."), 12, Muted));
        AddCombo(calibration,"brush_calibration_size",T("Розміри для калібрування","Sizes to calibrate"),new[]{"1/3/10/20","1","3","10","20","40","60","100"});
        adaptiveCalibrate = AsyncButton(T("Калібрувати автоматично", "Calibrate automatically"), CalibrateBrush, true);
        calibration.Children.Add(adaptiveCalibrate);
        adaptiveRetry=AsyncButton(T("Повторити лише невдалі вибрані Size","Retry only failed selected Sizes"),CalibrateFailedBrush);
        adaptiveRetry.Tag="brush-retry-failed";calibration.Children.Add(adaptiveRetry);
        calibration.Children.Add(Text(T("Під час тесту не рухай мишу. ESC — скасувати. Після завершення очисти полотно.", "Do not move the mouse during the test. ESC cancels. Clear Canvas afterwards."), 11, Muted));
        calibration.Children.Add(Text(T("Кожен Size зберігається лише після 3/3 узгоджених вимірювань. Слабкий Size не блокує решту. Підтверджений Size 3/10/20 можна окремо перевірити тестом швидкості; адаптивному режиму потрібен Size 1.",
            "Each Size is saved only after 3/3 consistent measurements. A weak Size does not block the others. Verified Size 3/10/20 can be tested independently in Speed Probe; adaptive mode requires Size 1."),12,Muted));
        adaptiveResult = Text("", 12); calibration.Children.Add(adaptiveResult);
        var states=new StackPanel();calibration.Children.Add(states);
        BuildBrushStates(states);
        var samples = new StackPanel();
        calibration.Children.Add(new Expander { Header = T("Виміряні розміри пензля", "Measured brush sizes"), Content = samples });
        samples.Children.Add(Text(T("Розмір → діаметр крапки / суцільне покриття (px)", "Size → dot diameter / solid coverage (px)"), 11, Muted));
        foreach(var p in BrushFootprints.Read(settings,true))
            samples.Children.Add(Text(T($"Форма {p.ShapeSlot}",$"Shape {p.ShapeSlot}")+$" · Size {p.Size} → {p.SafetyBounds.Width} × {p.SafetyBounds.Height} px · "+T("ядро","core")+$" {p.SolidCore.Width} × {p.SolidCore.Height} px · 3/3",12));
        if (settings.Data["brush_calibration_points"] is JsonArray points && points.Count > 0)
            foreach (var point in points.OfType<JsonArray>().Where(x => x.Count >= 3))
                samples.Children.Add(Text($"{point[0]} → {point[1]} / {point[2]} px", 12));
        else samples.Children.Add(Text(T("Вимірювань ще немає.", "No measurements yet."), 12, Muted));

        page.Children.Add(Card(T("3. Адаптивне малювання", "3. Adaptive painting"), out var painting));
        adaptiveEnabled = new CheckBox { Content = T("Увімкнути адаптивний пензель", "Enable adaptive brush"), IsChecked = settings.Bool("adaptive_brush") };
        painting.Children.Add(adaptiveEnabled);
        readers["adaptive_brush"] = () => adaptiveEnabled.IsChecked == true;
        adaptiveEnabled.Click += (_, _) => Guard(() =>
        {
            if (buildingUi) return;
            ReadSettings(); if (settings.Bool("adaptive_brush")) AdaptiveBrush.Prepare(settings);
            Dirty(); BuildUi();
        });
        AddCombo(painting, "adaptive_max_size", T("Максимальний розмір широкого пензля", "Maximum wide brush Size"), new[] { "3", "10", "20", "40", "60", "100" }, true);
        AddCheck(painting,"adaptive_auto_shape",T("Автоматично вибирати виміряні форми","Automatically choose measured shapes"));
        painting.Children.Add(Text(T("Використовуються лише актуальні вимірювання. Planner враховує повний можливий слід, стабільне ядро та час перемикання. Текстури без ядра не прискорюють точну заливку. Спочатку відкалібруй 1/3/10/20, потім більші Size окремо.", "Only current measurements are used. The planner accounts for the full possible footprint, stable core, and switching time. Textures without a solid core do not accelerate precise fills. Calibrate 1/3/10/20 first, then larger Sizes separately."), 12, Muted));
        painting.Children.Add(Text(T("Режим вмикає точне покриття та прозорість 1. Підтверджені тестом швидкості лінії з Shift сумісні з адаптивним пензлем. Після зміни полотна, кольорового режиму або форми повтори калібрування.", "This mode sets Precision and Opacity 1. Shift lines verified by Speed Probe work with adaptive brushes. Recalibrate after changing Canvas, color mode, or brush shape."), 12, Muted));
        painting.Children.Add(Button(T("Перейти до малювання", "Go to painting"), () => ShowPage("paint")));
        var values=new StackPanel();page.Children.Add(new Expander{Header=T("Точні значення розміру, інтервалу та прозорості","Exact Size / Interval / Opacity values"),Content=values});
        AddCheck(values,"auto_brush_size",T("Автоматичний розмір пензля","Automatic brush size"));
        AddNumber(values,"brush_size_value",Option("control","size"));AddNumber(values,"interval_value",Option("control","interval"));
        AddNumber(values,"paint_opacity_value",Option("control","opacity"));AddCheck(values,"use_fixed_opacity",T("Фіксована прозорість","Fixed opacity"));
        values.Children.Add(Text(T("У точному режимі розмір задає профіль руху, інтервал = 0.01. Ручний розмір діє з вимкненими точними параметрами й авторозміром. Адаптивний режим задає розмір кожного штриха.","In Precision with precision controls, the movement profile sets Size and Interval is 0.01. Manual Size applies with precision controls and automatic sizing disabled. Adaptive mode sets each stroke's Size."),12,Muted));
        page.Children.Add(Button(T("Перейти до тесту швидкості", "Go to Speed Probe"), ShowSpeedSetup));
    }

    private void RefreshAdaptiveStatus()
    {
        RefreshSpeedStatus();
        var problem = AdaptiveBrush.SetupProblem(settings);
        var current = AdaptiveBrush.CalibrationCurrent(settings);
        adaptivePreparation.Text = problem is null ? T("✓ Полотно, керування й палітра захоплені.", "✓ Canvas, controls, and palette are captured.") : T(problem);
        adaptiveCalibrate.IsEnabled = problem is null && !Painting;
        var selected=settings.Text("brush_calibration_size","1/3/10/20");
        double[] requested=double.TryParse(selected,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var parsed)
            &&BrushFootprints.Sizes.Contains(parsed)?[parsed]:[1,3,10,20];
        adaptiveRetry.IsEnabled=problem is null&&!Painting&&BrushSignalDiagnostics.RetrySizes(settings,requested).Length>0;
        // A legacy/stale enabled setting must remain possible to turn off.
        adaptiveEnabled.IsEnabled = !Painting && (settings.Bool("adaptive_brush") || current && problem is null);
        adaptiveEnabled.ToolTip = current ? null : T("Спочатку натисни «Калібрувати автоматично».", "Click Calibrate automatically first.");
        var hasSamples = settings.Data["brush_calibration_points"] is JsonArray a && a.Count > 0;
        var measured=BrushFootprints.Read(settings);
        adaptiveStatus.Text = current ? T("✓ Калібрування актуальне", "✓ Calibration is current") : hasSamples
            ? T("Параметри змінилися — потрібне нове калібрування", "Settings changed — recalibration needed")
            : T("Пензель ще не відкалібрований", "Brush is not calibrated yet");
        if(!current&&measured.Count>0&&!measured.Any(p=>p.Size==1&&p.SolidCore.Valid))adaptiveStatus.Text=T($"Виміряні Size: {string.Join(", ",measured.Select(p=>p.Size))}. Для адаптивного режиму бракує Size 1 зі стабільним ядром.",
            $"Measured Sizes: {string.Join(", ",measured.Select(p=>p.Size))}. Adaptive mode still needs Size 1 with a stable core.");
        adaptiveStatus.Foreground = current ? Success : Warning;
        adaptiveResult.Text = adaptiveFailure.Length > 0 ? string.Join("\n",adaptiveFailure.Split('\n').Select(line=>T(line))) : current
            ? T("Готово. Очисти полотно перед початком малювання.", "Ready. Clear Canvas before START.") : "";
        adaptiveSummary.Text = settings.Bool("adaptive_brush") && current
            ? T("Адаптивний режим: увімкнено", "Adaptive mode: enabled")
            : T("Адаптивний режим: вимкнено або потрібне калібрування", "Adaptive mode: disabled or needs calibration");
    }

    private void BuildBrushStates(StackPanel panel)
    {
        var diagnostics=BrushSignalDiagnostics.Read(settings);
        var speed=SpeedCalibration.Read(settings);
        foreach(double size in new double[]{1,3,10,20}.Union(diagnostics.Select(r=>r.Size)).Union(BrushFootprints.Read(settings).Select(r=>r.Size)).Order())
        {
            var attempt=diagnostics.FirstOrDefault(r=>r.Size==size);var profile=BrushFootprints.Find(settings,size);
            var state=attempt?.State??(profile is {SolidCore.Valid:true}?BrushSignalState.Verified:(BrushSignalState?)null);
            var row=new Grid{Margin=new(0,5,0,5),Tag=$"brush-state:{size}"};
            row.ColumnDefinitions.Add(new(){Width=new GridLength(78)});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
            var label=Text($"Size {size}",12);label.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(label);
            var values=new StackPanel();Grid.SetColumn(values,1);row.Children.Add(values);
            var chip=new StatusChip();chip.Set(state switch{
                BrushSignalState.Verified=>T("Підтверджено · 3/3","Verified · 3/3"),
                BrushSignalState.WeakRepeatable=>T("Слабкий повторюваний слід · 3/3","Weak repeatable trace · 3/3"),
                BrushSignalState.Rejected=>T("Відхилено","Rejected"),BrushSignalState.Stale=>T("Застаріло","Stale"),
                _=>T("Не виміряно","Not measured")},state==BrushSignalState.Verified?Success:state==BrushSignalState.Rejected?Danger:Warning);
            values.Children.Add(chip);
            if(profile is {SolidCore.Valid:true})
            {
                bool wide=profile.Size>1&&profile.SolidCore.Width>=2&&profile.SolidCore.Height>=2;
                var coreChip=new StatusChip{Tag=$"brush-wide:{size}"};
                coreChip.Set(size==1?T("Базовий пензель","Base brush"):
                    wide?T("Ядро придатне для широкого планування","Core eligible for wide planning"):
                    T("Ядро надто вузьке для широкого прискорення","Core too narrow for wide acceleration"),size==1||wide?Success:Warning);
                coreChip.ToolTip=T($"Стабільне ядро: {profile.SolidCore.Width} × {profile.SolidCore.Height} px. Для широкого планування обидва розміри мають бути ≥2 px. Швидкість руху перевіряється окремо.",
                    $"Stable core: {profile.SolidCore.Width} × {profile.SolidCore.Height} px. Wide planning requires both dimensions ≥2 px. Motion speed is tested separately.");
                values.Children.Add(coreChip);
            }
            if(attempt is not null)
            {
                string detail=T($"Контраст {attempt.Samples.Min(s=>s.Contrast.PeakDelta)}–{attempt.Samples.Max(s=>s.Contrast.PeakDelta)}/80 · шум ≤{attempt.Samples.Max(s=>s.NoisePeak)} · збіг масок {attempt.SpatialAgreement:P0}",
                    $"Contrast {attempt.Samples.Min(s=>s.Contrast.PeakDelta)}–{attempt.Samples.Max(s=>s.Contrast.PeakDelta)}/80 · noise ≤{attempt.Samples.Max(s=>s.NoisePeak)} · mask agreement {attempt.SpatialAgreement:P0}");
                values.Children.Add(Text(detail,11,Muted));
                if(attempt.State!=BrushSignalState.Stale&&attempt.Samples.Any(s=>s.Color is {Changed:>0,Passed:false}))
                    values.Children.Add(Text(T("Слід не відповідає напрямку вибраного кольору.","Trace does not match the selected color direction."),11,Danger));
                chip.ToolTip=T("Слабкий слід — діагностика, а не підтверджене суцільне покриття. Він не вмикає Adaptive чи Speed Probe.",
                    "A weak trace is diagnostic evidence, not verified solid coverage. It does not enable Adaptive or Speed Probe.");
                if(state!=BrushSignalState.Verified&&profile is {SolidCore.Valid:true})values.Children.Add(Text(T("Попередня актуальна маска збережена.","Previous current mask retained."),11,Muted));
            }
            var routes=speed?.Samples.Where(s=>s.Size==size).ToArray()??[];
            values.Children.Add(Text(routes.Length==0?T("Швидкість: не перевірено","Speed: not tested"):
                T("Швидкість: ","Speed: ")+string.Join(" / ",routes.Select(s=>$"{(s.Vertical?"V":"H")} {s.Method} {s.SafeMs} ms")),11,Muted));
            panel.Children.Add(row);
        }
    }
}
