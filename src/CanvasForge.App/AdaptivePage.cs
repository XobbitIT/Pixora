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
    private string adaptiveFailure = "";
    private TabControl brushTabs = new();
    private void ShowSpeedSetup(){ShowPage("adaptive");brushTabs.SelectedIndex=1;}

    private void BuildAdaptive()
    {
        var page = new StackPanel();
        var wrapper=new StackPanel();pages["adaptive"] = Scroll(wrapper);
        wrapper.Children.Add(Text(T("Пензель і швидкість", "Brush and speed"), 24));
        var speedPage=new StackPanel();
        brushTabs=new TabControl{Margin=new Thickness(0,12,0,0)};
        brushTabs.Items.Add(new TabItem{Header=T("Пензель","Brush"),Content=page});
        brushTabs.Items.Add(new TabItem{Header=T("Speed Probe і аудит","Speed Probe and audit"),Content=speedPage});wrapper.Children.Add(brushTabs);
        page.Children.Add(Card(T("1. Підготовка", "1. Preparation"), out var preparation));
        adaptivePreparation = Text("", 12); preparation.Children.Add(adaptivePreparation);
        preparation.Children.Add(Button(T("Захопити Canvas і керування Rust", "Capture Canvas and Rust controls"), () => ShowPage("capture")));
        preparation.Children.Add(Text(T("Форма пензля для калібрування", "Brush shape for calibration"), 11, Muted));
        var shapes = new UniformGridCompat(2);
        foreach (var (name, slot, label) in new[] { ("Round", 3, T("Суцільний круглий", "Solid round")), ("Square", 4, T("Суцільний квадратний", "Solid square")) })
        {
            var selected = settings.Int("brush_shape_slot", 3) == slot;
            shapes.Add(Button((selected ? "✓ " : "") + label, () =>
            {
                if (selected) return;
                ReadSettings(); settings.Set("brush_shape", name); settings.Set("brush_shape_slot", slot);
                settings.Set("adaptive_brush", false); adaptiveFailure = ""; Dirty(); BuildUi();
            }, selected));
        }
        preparation.Children.Add(shapes.Panel);
        preparation.Children.Add(Text(T("Режим кольорів: ", "Color mode: ") + settings.Text("color_mode") + ". "
            + T("Змінюється в розділі «Малювання».", "Change it on the Painting page."), 11, Muted));

        page.Children.Add(Card(T("2. Автоматичне калібрування", "2. Automatic calibration"), out var calibration));
        adaptiveStatus = Text("", 13); calibration.Children.Add(adaptiveStatus);
        calibration.Children.Add(Text(T("Відкрий чистий Canvas. Програма сама вибере контрастний колір, введе 1 → 3 → 10 → 20 і виміряє чотири крапки. Ручні точки повзунка не потрібні.", "Open a clean Canvas. Pixora selects a contrasting color, enters 1 → 3 → 10 → 20, and measures four dots. No manual slider points are needed."), 12, Muted));
        adaptiveCalibrate = AsyncButton(T("Калібрувати автоматично", "Calibrate automatically"), CalibrateBrush, true);
        calibration.Children.Add(adaptiveCalibrate);
        calibration.Children.Add(Text(T("Під час тесту не рухай мишу. ESC — скасувати. Після завершення очисти Canvas.", "Do not move the mouse during the test. ESC cancels. Clear Canvas afterwards."), 11, Muted));
        adaptiveResult = Text("", 12); calibration.Children.Add(adaptiveResult);
        var samples = new StackPanel();
        calibration.Children.Add(new Expander { Header = T("Виміряні розміри пензля", "Measured brush sizes"), Content = samples });
        samples.Children.Add(Text(T("Size → діаметр крапки / суцільне покриття (px)", "Size → dot diameter / solid coverage (px)"), 11, Muted));
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
        AddCombo(painting, "adaptive_max_size", T("Максимальний Size широкого пензля", "Maximum wide brush Size"), new[] { "10", "20" }, true);
        painting.Children.Add(Text(T("10 — дрібніші широкі штрихи; 20 — для великих однорідних ділянок. Краї завжди завершує тонкий пензель.", "10 uses smaller wide strokes; 20 suits large uniform areas. A fine brush always finishes edges."), 11, Muted));
        painting.Children.Add(Text(T("Режим вмикає Precision та Opacity 1. Підтверджені Speed Probe лінії Shift сумісні з адаптивним пензлем. Після зміни Canvas, кольорового режиму або форми повтори калібрування.", "This mode sets Precision and Opacity 1. Shift lines verified by Speed Probe work with adaptive brushes. Recalibrate after changing Canvas, color mode, or brush shape."), 12, Muted));
        painting.Children.Add(Button(T("Перейти до малювання", "Go to painting"), () => ShowPage("paint")));
        var values=new StackPanel();page.Children.Add(new Expander{Header=T("Точні значення Size / Interval / Opacity","Exact Size / Interval / Opacity values"),Content=values});
        AddCheck(values,"auto_brush_size",T("Автоматичний розмір пензля","Automatic brush size"));
        AddNumber(values,"brush_size_value","Size");AddNumber(values,"interval_value","Interval");
        AddNumber(values,"paint_opacity_value","Opacity");AddCheck(values,"use_fixed_opacity",T("Фіксована прозорість","Fixed opacity"));
        values.Children.Add(Text(T("У Precision з точними параметрами Size задає профіль руху, Interval = 0.01. Ручний Size діє з вимкненими точними параметрами й авторозміром. Адаптивний режим задає Size кожного штриха.","In Precision with precision controls, the movement profile sets Size and Interval is 0.01. Manual Size applies with precision controls and automatic sizing disabled. Adaptive mode sets each stroke's Size."),12,Muted));
        BuildSpeedSections(speedPage);
    }

    private void RefreshAdaptiveStatus()
    {
        RefreshSpeedStatus();
        var problem = AdaptiveBrush.SetupProblem(settings);
        var current = AdaptiveBrush.CalibrationCurrent(settings);
        adaptivePreparation.Text = problem is null ? T("✓ Canvas, керування й палітра захоплені.", "✓ Canvas, controls, and palette are captured.") : T(problem);
        adaptiveCalibrate.IsEnabled = problem is null && !Painting;
        // A legacy/stale enabled setting must remain possible to turn off.
        adaptiveEnabled.IsEnabled = !Painting && (settings.Bool("adaptive_brush") || current && problem is null);
        adaptiveEnabled.ToolTip = current ? null : T("Спочатку натисни «Калібрувати автоматично».", "Click Calibrate automatically first.");
        var hasSamples = settings.Data["brush_calibration_points"] is JsonArray a && a.Count > 0;
        adaptiveStatus.Text = current ? T("✓ Калібрування актуальне", "✓ Calibration is current") : hasSamples
            ? T("Параметри змінилися — потрібне нове калібрування", "Settings changed — recalibration needed")
            : T("Пензель ще не відкалібрований", "Brush is not calibrated yet");
        adaptiveStatus.Foreground = current ? Success : Warning;
        adaptiveResult.Text = adaptiveFailure.Length > 0 ? adaptiveFailure : current
            ? T("Готово. Очисти Canvas перед START.", "Ready. Clear Canvas before START.") : "";
        adaptiveSummary.Text = settings.Bool("adaptive_brush") && current
            ? T("Адаптивний режим: увімкнено", "Adaptive mode: enabled")
            : T("Адаптивний режим: вимкнено або потрібне калібрування", "Adaptive mode: disabled or needs calibration");
    }
}
