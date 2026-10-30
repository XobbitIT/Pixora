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
    private Button adaptiveBaseCalibrate=new();
    private TextBlock workingCalibrationStatus=new();
    private Button brushDiagnosticButton = new();
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
        workingCalibrationStatus=Text("",13);calibration.Children.Add(workingCalibrationStatus);
        calibration.Children.Add(Text(T("Size — значення в Rust, а не діаметр у пікселях. Три незалежні крапки визначають слід від координати миші. Контрольне нанесення в ту саму точку перевіряє насичення; ядро береться лише з першого відбитка.", "Size is the Rust control value, not a diameter in pixels. Three independent dots measure the footprint relative to the mouse command. Another application at the same point checks saturation; the core uses only the first imprint."), 12, Muted));
        AddCombo(calibration,"brush_calibration_size",T("Розміри для калібрування","Sizes to calibrate"),SetupBrushSelection.Options);
        calibration.Children.Add(Text(T("Комбінована перевірка: 2/3/5/7/10/15/20, три відбитки кожного розміру.",
            "Combined calibration: 2/3/5/7/10/15/20, three imprints per Size."),12,Muted));
        adaptiveCalibrate = CheckButton(T("Калібрувати автоматично", "Calibrate automatically"), BeginBrushCalibration, true);
        calibration.Children.Add(adaptiveCalibrate);
        adaptiveRetry=CheckButton(T("Повторити лише невдалі вибрані Size","Retry only failed selected Sizes"),CalibrateFailedBrush);
        adaptiveRetry.Tag="brush-retry-failed";calibration.Children.Add(adaptiveRetry);
        calibration.Children.Add(Text(T("Під час тесту не рухай мишу. ESC — скасувати. Після завершення очисти полотно.", "Do not move the mouse during the test. ESC cancels. Clear Canvas afterwards."), 11, Muted));
        calibration.Children.Add(Text(T("Кожен Size зберігається після 3/3 вимірювань. Мінімальний розмір — 2; типовий — 3. Для малювання потрібне стабільне ядро.",
            "Each Size is saved after 3/3 measurements. Minimum Size is 2; default is 3. Painting requires a stable core."),12,Muted));
        adaptiveResult = Text("", 12); calibration.Children.Add(adaptiveResult);
        brushDiagnosticButton=Button(T("Відкрити знімки калібрування","Open calibration snapshots"),ShowBrushDiagnostics);
        brushDiagnosticButton.Tag="brush-diagnostics";brushDiagnosticButton.HorizontalAlignment=HorizontalAlignment.Left;
        calibration.Children.Add(brushDiagnosticButton);
        var states=new StackPanel();calibration.Children.Add(states);
        BuildBrushStates(states);
        var samples = new StackPanel();
        calibration.Children.Add(new Expander { Header = T("Виміряні розміри пензля", "Measured brush sizes"), Content = samples });
        samples.Children.Add(Text(T("Розмір → діаметр крапки / суцільне покриття (px)", "Size → dot diameter / solid coverage (px)"), 11, Muted));
        foreach(var p in BrushFootprints.Read(settings,true).Where(p=>BrushFootprints.CalibrationSizes.Contains(p.Size)))
            samples.Children.Add(Text(T($"Форма {p.ShapeSlot}",$"Shape {p.ShapeSlot}")+$" · Size {p.Size} → {p.SafetyBounds.Width} × {p.SafetyBounds.Height} px · "+T("ядро","core")+$" {p.SolidCore.Width} × {p.SolidCore.Height} px · 3/3",12));
        if (settings.Data["brush_calibration_points"] is JsonArray points && points.Count > 0)
            foreach (var point in points.OfType<JsonArray>().Where(x => x.Count >= 3&&double.TryParse(x[0]?.ToString(),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var size)&&BrushFootprints.CalibrationSizes.Contains(size)))
                samples.Children.Add(Text($"{point[0]} → {point[1]} / {point[2]} px", 12));
        else samples.Children.Add(Text(T("Вимірювань ще немає.", "No measurements yet."), 12, Muted));

        page.Children.Add(Card(T("3. Адаптивне малювання", "3. Adaptive painting"), out var painting));
        painting.Children.Add(Text(T("Adaptive використовує стабільний Rapid без з’єднаних швидких проходів. Великі пензлі допускаються лише після перевірки руху H/V.",
            "Adaptive uses stable Rapid without connected fast sweeps. Wide brushes require verified H/V movement."),12,Muted));
        adaptiveBaseCalibrate=CheckButton(T("Перевірити Size 2 (необов’язково)","Measure Size 2 (optional)"),CalibrateAdaptiveBase);
        adaptiveBaseCalibrate.Tag="adaptive-base-calibration";painting.Children.Add(adaptiveBaseCalibrate);
        painting.Children.Add(Text(T("Комбінує підтверджені розміри: великі для заливки, найменший для деталей. Мінімальний Size — 2; типовий — 3.","Combines verified Sizes: large brushes for fills, the smallest for details. Minimum Size is 2; default is 3."),12,Muted));
        adaptiveStatus=Text("",13);painting.Children.Add(adaptiveStatus);
        var baseState=new StackPanel();BuildBrushStates(baseState,true);painting.Children.Add(baseState);
        adaptiveEnabled = new CheckBox { Content = T("Увімкнути адаптивний пензель", "Enable adaptive brush"), IsChecked = settings.Bool("adaptive_brush") };
        painting.Children.Add(adaptiveEnabled);
        readers["adaptive_brush"] = () => adaptiveEnabled.IsChecked == true;
        adaptiveEnabled.Click += (_, _) => Guard(() =>
        {
            if (buildingUi) return;
            ReadSettings();
            if (settings.Bool("adaptive_brush") && !AdaptiveBrush.CalibrationCurrent(settings))
            {
                settings.Set("adaptive_brush", false); adaptiveEnabled.IsChecked = false; Save();
                SetStatus(T("Потрібен хоча б один актуальний Size зі стабільним ядром у вибраному діапазоні. Відкалібруй Size 3.",
                    "At least one current Size with a stable core is needed within the selected range. Calibrate Size 3."));
                UpdateReady(); return;
            }
            if (settings.Bool("adaptive_brush")) AdaptiveBrush.Prepare(settings);
            Dirty(); BuildUi();
        });
        AddCombo(painting, "adaptive_max_size", T("Максимальний розмір широкого пензля", "Maximum wide brush Size"), SetupBrushSelection.SizeOptions.Where(size=>size!="1").ToArray(), true);
        AddCheck(painting,"adaptive_auto_shape",T("Автоматично вибирати виміряні форми","Automatically choose measured shapes"));
        painting.Children.Add(Text(T("Повний можливий слід залишається всередині свого кольору. Вузькі ділянки, куди підтверджені пензлі не поміщаються, показуються перед стартом. Текстури без стабільного ядра не використовуються.", "The full possible footprint stays inside its color. Narrow areas that verified brushes cannot fit are shown before START. Textures without a stable core are excluded."), 12, Muted));
        painting.Children.Add(Text(T("Режим використовує точне покриття. Інтервал 0.01 і прозорість 1.00 встанови вручну в Rust; Pixora їх не змінює й не перевіряє. Після зміни полотна, кольорового режиму або форми повтори калібрування.", "This mode uses Precision. Set Interval 0.01 and Opacity 1.00 manually in Rust; Pixora does not change or verify them. Recalibrate after changing Canvas, color mode or brush shape."), 12, Muted));
        painting.Children.Add(Button(T("Перейти до малювання", "Go to painting"), () => ShowPage("paint")));
        var values=new StackPanel();page.Children.Add(new Expander{Header=T("Розмір пензля","Brush Size"),Content=values});
        AddCheck(values,"auto_brush_size",T("Автоматичний розмір пензля","Automatic brush size"));
        AddNumber(values,"brush_size_value",Option("control","size"));
        values.Children.Add(Text(T("Мінімальний Size — 2, типовий — 3. Adaptive використовує лише підтверджені розміри зі стабільним ядром.","Minimum Size is 2; default is 3. Adaptive uses only verified Sizes with a stable core."),12,Muted));
        page.Children.Add(Button(T("Перейти до тесту швидкості", "Go to Speed Probe"), ShowSpeedSetup));
    }

    private double[] SelectedCalibrationSizes()
    {
        try{return SetupBrushSelection.Sizes(settings.Text("brush_calibration_size","3"),false);}
        // The visible ComboBox falls back to its first option, Size 3.
        catch(ArgumentException){return [3];}
    }
    private void RefreshAdaptiveStatus()
    {
        RefreshSpeedStatus();
        var problem = AdaptiveBrush.SetupProblem(settings);
        var current = AdaptiveBrush.CalibrationCurrent(settings);
        double workingSize=PaintTimingPlan.DefaultSize(settings);
        bool workingReady=SpeedCalibration.BrushReady(settings,workingSize);
        workingCalibrationStatus.Text="Size "+workingSize+" · "+(workingReady?T("слід підтверджений","footprint verified"):T("слід не підтверджений","footprint unverified"));
        workingCalibrationStatus.Foreground=workingReady?Success:Warning;
        adaptivePreparation.Text = problem is null ? T("✓ Полотно, керування й палітра захоплені.", "✓ Canvas, controls, and palette are captured.") : T(problem);
        adaptiveCalibrate.IsEnabled = !Painting;
        adaptiveCalibrate.ToolTip = problem is null ? null : T(problem);
        adaptiveBaseCalibrate.IsEnabled=!Painting;adaptiveBaseCalibrate.ToolTip=problem is null?null:T(problem);
        var requested=SelectedCalibrationSizes();
        adaptiveRetry.IsEnabled=problem is null&&!Painting&&BrushSignalDiagnostics.RetrySizes(settings,requested).Length>0;
        brushDiagnosticButton.IsEnabled=!Painting&&LatestBrushDiagnostics() is not null;
        // A legacy/stale enabled setting must remain possible to turn off.
        adaptiveEnabled.IsEnabled = !Painting;
        adaptiveEnabled.ToolTip = current ? T("Комбінує лише підтверджені Size від 2.","Combines only verified Sizes of 2 or larger.") : T("Відкалібруй Size 3 або інший Size зі стабільним ядром.", "Calibrate Size 3 or another Size with a stable core.");
        var hasSamples = settings.Data["brush_calibration_points"] is JsonArray a && a.Count > 0;
        var measured=DrawingWorkflow.PaintingProfiles(settings,settings.Bool("adaptive_auto_shape"));
        adaptiveStatus.Text = current ? T("✓ Калібрування актуальне", "✓ Calibration is current") : hasSamples
            ? T("Параметри змінилися — потрібне нове калібрування", "Settings changed — recalibration needed")
            : T("Пензель ще не відкалібрований", "Brush is not calibrated yet");
        if(current)adaptiveStatus.Text=T("Готові Size: ","Ready Sizes: ")+string.Join(", ",measured.Where(p=>p.SolidCore.Valid&&p.Size<=settings.Number("adaptive_max_size",20)).Select(p=>p.Size));
        adaptiveStatus.Foreground = current ? Success : Warning;
        adaptiveResult.Text = adaptiveFailure.Length > 0 ? string.Join("\n",adaptiveFailure.Split('\n').Select(line=>T(line))) : current
            ? T("Готово. Очисти полотно перед початком малювання.", "Ready. Clear Canvas before START.") : "";
        adaptiveSummary.Text = settings.Bool("adaptive_brush") && current
            ? T("Адаптивний режим: увімкнено", "Adaptive mode: enabled")
            : T("Адаптивний режим: вимкнено або потрібне калібрування", "Adaptive mode: disabled or needs calibration");
    }

    private async Task BeginBrushCalibration()
    {
        ReadSettings();
        if (AdaptiveBrush.SetupProblem(settings) is { } problem)
        { ShowPage("capture"); SetStatus(T(problem)); return; }
        await CalibrateBrush();
    }

    private void BuildBrushStates(StackPanel panel,bool adaptiveBase=false)
    {
        var diagnostics=BrushSignalDiagnostics.Read(settings);
        var speed=SpeedCalibration.Read(settings);
        foreach(double size in (adaptiveBase?new double[]{2}:SelectedCalibrationSizes().Union(diagnostics.Select(r=>r.Size)).Union(BrushFootprints.Read(settings).Select(r=>r.Size)).Where(size=>BrushFootprints.CalibrationSizes.Contains(size))).Order())
        {
            var attempt=diagnostics.FirstOrDefault(r=>r.Size==size);var profile=BrushFootprints.Find(settings,size);
            var state=BrushSignalDiagnostics.State(settings,settings.Int("brush_shape_slot",3),size);
            var row=new Grid{Margin=new(0,5,0,5),Tag=$"brush-state:{size}"};
            row.ColumnDefinitions.Add(new(){Width=new GridLength(78)});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
            var label=Text($"Size {size}",12);label.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(label);
            var values=new StackPanel();Grid.SetColumn(values,1);row.Children.Add(values);
            var chip=new StatusChip();chip.Set(state switch{
                BrushSignalState.Verified=>T("Підтверджено · 3/3","Verified · 3/3"),
                BrushSignalState.WeakRepeatable=>T("Слабкий повторюваний слід · 3/3","Weak repeatable trace · 3/3"),
                BrushSignalState.NoSolidCore=>T("Слід виміряно · немає стабільного ядра","Trace measured · no stable core"),
                BrushSignalState.Rejected=>T("Відхилено","Rejected"),BrushSignalState.Stale=>T("Застаріло","Stale"),
                _=>T("Не виміряно","Not measured")},state==BrushSignalState.Verified?Success:state==BrushSignalState.Rejected?Danger:Warning);
            values.Children.Add(chip);
            if(profile is {SolidCore.Valid:true})
            {
                var coreChip=new StatusChip{Tag=$"brush-wide:{size}"};
                coreChip.Set(size==2?T("Базовий пензель","Base brush"):
                    T($"Підтверджена маска · {profile.SolidPixels} px",$"Certified mask · {profile.SolidPixels} px"),Success);
                coreChip.ToolTip=T($"Прямокутник ядра: {profile.SolidCore.Width} × {profile.SolidCore.Height} px; суцільна маска: {profile.SolidPixels} px. План використовує всю підтверджену маску без її розширення. Прискорення залежить від зображення; швидкість руху перевіряється окремо.",
                    $"Core rectangle: {profile.SolidCore.Width} × {profile.SolidCore.Height} px; solid mask: {profile.SolidPixels} px. Planning uses the whole certified mask without expanding it. Speedup depends on the image; motion speed is tested separately.");
                values.Children.Add(coreChip);
                if(size>3)
                {
                    bool moving=BrushMotionEvidence.Current(settings,profile);
                    var actual=StrokeProfiles.Read(settings).Where(p=>p.DotId==profile.Id).ToArray();
                    values.Children.Add(Text(actual.Length>0?T("Підтверджені штрихи: ","Verified strokes: ")+string.Join(" / ",actual.Select(p=>$"{(p.Vertical?"V":"H")} {p.Length} px")):
                        moving?T("Рух H/V підтверджено","H/V motion verified"):
                        T("Крапки підтверджені; рух не підтверджено. Цей Size не використовується в Adaptive.","Dots verified; motion unverified. This Size is excluded from Adaptive."),11,moving?Success:Warning));
                    if(!moving&&BrushMotionEvidence.Diagnostic(settings,profile) is { } proof)
                        foreach(var axis in proof.Axes.Where(a=>!a.Passed))
                            values.Children.Add(Text((axis.Vertical?"V: ":"H: ")+MotionFailure(axis.Reason),11,Warning));
                }
            }
            if(profile is not null)
                foreach(var diagnostic in StrokeProfiles.Diagnostics(settings).Where(d=>d.DotId==profile.Id&&d.Reason!="verified"))
                    values.Children.Add(Text((diagnostic.Vertical?"V: ":"H: ")+MotionFailure(diagnostic.Reason),11,Warning));
            if(attempt is not null)
            {
                string detail=T($"Контраст {attempt.Samples.Min(s=>s.Contrast.PeakDelta)}–{attempt.Samples.Max(s=>s.Contrast.PeakDelta)}/80 · шум ≤{attempt.Samples.Max(s=>s.NoisePeak)} · збіг масок {attempt.SpatialAgreement:P0}",
                    $"Contrast {attempt.Samples.Min(s=>s.Contrast.PeakDelta)}–{attempt.Samples.Max(s=>s.Contrast.PeakDelta)}/80 · noise ≤{attempt.Samples.Max(s=>s.NoisePeak)} · mask agreement {attempt.SpatialAgreement:P0}");
                values.Children.Add(Text(detail,11,Muted));
                if(state!=BrushSignalState.Stale)
                {
                    var repeats=new StackPanel();
                    foreach(var sample in attempt.Samples)
                    {
                        bool noPaint=sample.Contrast.ChangedPixels==0;
                        var repeatText=Text(T($"Повтор {sample.Repeat}: {sample.Contrast.PeakDelta}/{sample.Contrast.RequiredDelta} · {sample.Contrast.ChangedPixels} змінених px",
                            $"Repeat {sample.Repeat}: {sample.Contrast.PeakDelta}/{sample.Contrast.RequiredDelta} · {sample.Contrast.ChangedPixels} changed px")
                            +(noPaint?T(" · немає нового сліду"," · no new trace"):""),11,noPaint?Danger:Muted);
                        if(sample.Geometry is { } geometry)
                            repeatText.ToolTip=T($"Непрозорих пікселів: {geometry.SolidPixels}; центр від координати команди: ({geometry.CenterX:0.##}, {geometry.CenterY:0.##}) px. Маска не зміщується до візуального центру.",
                                $"Opaque pixels: {geometry.SolidPixels}; center from the command: ({geometry.CenterX:0.##}, {geometry.CenterY:0.##}) px. The mask is not shifted to its visual center.");
                        repeats.Children.Add(repeatText);
                        if(sample.LocalColor is {Passed:false} local)
                        {
                            int delta=RustSlider.Delta(local.Reference,local.ConfirmedReference);
                            repeats.Children.Add(Text(T($"Повтор {sample.Repeat}: зміна після контрольного нанесення {delta}/12; стабільних пікселів {local.StablePixels}.",
                                $"Repeat {sample.Repeat}: change after the second application {delta}/12; stable pixels {local.StablePixels}."),11,Warning));
                        }
                    }
                    if(state==BrushSignalState.Verified)values.Children.Add(new Expander{Header=T("Три вимірювання","Three measurements"),Content=repeats});
                    else values.Children.Add(repeats);
                }
                if(state==BrushSignalState.NoSolidCore)values.Children.Add(Text(T("Спільне ядро крапок не підтверджене. Придатність штриха визначається окремою перевіркою руху.",
                    "A shared dot core was not confirmed. Stroke eligibility is determined by a separate movement check."),11,Warning));
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
    private string MotionFailure(string reason)=>reason switch
    {
        "stroke_source_unverified"=>T("Активний колір не підтверджений.","Active color was not verified."),
        "stroke_background_unstable"=>T("Фон змінюється між кадрами.","Background changes between frames."),
        "stroke_paint_changes_on_repeat"=>T("Повторний прохід змінює колір лінії.","Another pass changes the line color."),
        "stroke_heldout_outside_possible" or "stroke_clipped_or_scene_changed"=>T("Незалежна лінія виходить за виміряний слід. Для малювання використай Standard.","The independent line exceeds the measured footprint. Use Standard for painting."),
        "stroke_heldout_solid_missing" or "stroke_common_solid_missing" or "stroke_solid_missing"=>T("У незалежних лініях немає спільного суцільного сліду. Для малювання використай Standard.","Independent lines have no shared solid footprint. Use Standard for painting."),
        "stroke_training_color_changed"=>T("Колір ліній у різних місцях не збігається в межах допуску 12.","Line colors at different positions do not agree within tolerance 12."),
        "source_unverified"=>T("Активний колір не підтверджений.","Active color was not verified."),
        "background_unstable"=>T("Фон змінюється між кадрами.","Background changes between frames."),
        "moving_footprint_outside_dot_mask"=>T("Лінія виходить за виміряний слід крапки.","The line extends beyond the measured dot footprint."),
        "moving_solid_missing"=>T("У лінії є прогалини або нерівномірний колір.","The line contains gaps or uneven color."),
        "moving_paint_changes_on_repeat"=>T("Повторний прохід змінює колір лінії.","Another pass changes the line color."),
        "contrast_insufficient" or "stroke_contrast_insufficient"=>T("Контраст лінії нижчий за 80.","Line contrast is below 80."),
        "insufficient_clean_space"=>T("Бракує чистого місця для перевірки руху. Повтори цей Size окремо.","Not enough clean space for a movement check. Recheck this Size separately."),
        _=>T("Суцільний слід руху не підтверджений.","Solid moving paint was not verified.")
    };
}
