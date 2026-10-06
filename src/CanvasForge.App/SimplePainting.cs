using System.Windows;
using System.Windows.Controls;
using CanvasForge.Core;

namespace CanvasForge.App;

internal sealed partial class MainWindow
{
    private bool SimpleMode => DrawingWorkflow.Simple(settings);
    private Settings EffectiveSettings => DrawingWorkflow.Effective(settings);
    private Button modeButton = new();
    private Button simpleSetupButton = new();
    private string SimpleProblem(string problem) => problem switch
    {
        "Захопи полотно." => T(problem, "Select Canvas."),
        "Захопи поле HEX." => T(problem, "Select the HEX field."),
        "Захопи палітру Rust 4×16." => T(problem, "Select the Rust 4×16 palette."),
        "Повтори захоплення палітри Rust 4×16." => T(problem, "Select the Rust 4×16 palette again."),
        _ => T(problem, "Select Canvas and colors for the current Rust window.")
    };

    internal void SetDrawingMode(bool advanced)
    {
        if (Painting) return;
        ReadSettings();
        settings.Set("drawing_mode", advanced ? "Advanced" : "Simple");
        currentPage = "paint";
        Dirty(false); BuildUi();
        SetStatus(advanced
            ? T("Розширений режим: калібрування, адаптивний пензель, швидкість і аудит.", "Advanced mode: calibration, adaptive brush, speed and audit.")
            : T("Відкрий зображення, вибери полотно та кольори Rust, потім натисни «Малювати».", "Open an image, select Rust Canvas and colors, then press Paint."));
    }

    private void BuildSimplePaint()
    {
        var page = new Grid { Margin = new Thickness(0, 0, 0, 8), AllowDrop = true };
        page.RowDefinitions.Add(new() { Height = GridLength.Auto });
        page.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        pages["paint"] = page;
        page.Children.Add(Text(T("Малюй у три кроки", "Paint in three steps"), 24));
        page.DragOver += (_, e) => { e.Effects = !Painting && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        page.Drop += async (_, e) =>
        {
            e.Handled = true;
            if (!Painting && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
                await LoadImageFile(files[0]);
        };
        contentArea = new Grid(); contentArea.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        contentArea.ColumnDefinitions.Add(new() { Width = new GridLength(340) });
        Grid.SetRow(contentArea, 1); page.Children.Add(contentArea);
        var left = new Grid { Margin = new Thickness(0, 0, 12, 0) };
        left.RowDefinitions.Add(new() { Height = GridLength.Auto }); left.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        contentArea.Children.Add(left);
        var previews = new Grid(); Grid.SetRow(previews, 1); left.Children.Add(previews); BuildPreviews(previews);
        var rightHost = new DockPanel(); Grid.SetColumn(rightHost, 1); contentArea.Children.Add(rightHost);
        var right = new StackPanel(); var scroll = Scroll(right);
        left.Children.Add(Card(T("1. Зображення", "1. Image"), out var image));
        image.Children.Add(Button(T("Відкрити зображення", "Open image"), OpenImage, true));
        image.Children.Add(Text(T("Або перетягни файл сюди.", "Or drop an image file here."), 12, Muted));
        AddCombo(image, "cell_px", T("Деталізація", "Detail"), new[] { "1", "3", "5", "8" }, true);
        right.Children.Add(Card(T("2. Полотно Rust", "2. Rust Canvas"), out var setup));
        AddCombo(setup, "color_mode", T("Кольори", "Colors"), new[] { "Rust Palette", "HEX Direct" }, true);
        simpleSetupButton = AsyncButton(T("Вибрати полотно й кольори", "Select Canvas and colors"), CaptureSimpleWizard);
        simpleSetupButton.Tag = "simple-setup"; setup.Children.Add(simpleSetupButton);
        ready = Text("", 13); setup.Children.Add(ready);
        var brush = new StackPanel();
        brush.Children.Add(Text(T("У Rust вибери суцільний квадратний пензель (4-й). Впиши: розмір 1, інтервал 0.01, прозорість 1. Ці значення потрібні також після паузи, якщо ти змінював пензель у грі.",
            "In Rust select the solid square brush (4th). Enter Size 1, Interval 0.01, Opacity 1. Restore these values after a pause if you changed the brush in game."), 13));
        setup.Children.Add(new Expander { Header = T("Як налаштувати пензель", "How to set the brush"), Content = brush, Tag = "simple-brush-help" });
        var paintCard = Card(T("3. Малювання", "3. Paint"), out var controls);
        DockPanel.SetDock(paintCard, Dock.Bottom); rightHost.Children.Add(paintCard); rightHost.Children.Add(scroll);
        controls.Children.Add(Text(T("У грі: квадратний пензель, розмір 1, інтервал 0.01, прозорість 1.", "In game: square brush, Size 1, Interval 0.01, Opacity 1."), 12, Muted));
        startButton = AsyncButton(T("Малювати", "Paint"), () => Start(false), true); controls.Children.Add(startButton);
        var actions = new UniformGridCompat(3);
        resumeButton = AsyncButton(T("Продовжити", "Resume"), () => Start(true));
        pauseButton = Button(T("Пауза", "Pause"), () => { if (painter is not null) painter.Paused = !painter.Paused; });
        stopButton = Button(T("Зупинити", "Stop"), () => paintCancel?.Cancel()); stopButton.Background = Danger;
        foreach (var action in new[] { resumeButton, pauseButton, stopButton }) { action.Padding = new Thickness(5, 9, 5, 9); actions.Add(action); }
        controls.Children.Add(actions.Panel);
        progressBar = new() { Height = 8, Maximum = 100, Foreground = Accent, Background = Input, Margin = new Thickness(0, 12, 0, 6) };
        controls.Children.Add(progressBar); progressLabel = Text("0%", 12, Muted); controls.Children.Add(progressLabel);
        eta = Text("", 12, Muted); controls.Children.Add(eta);
        controls.Children.Add(Text(T("F6 — пауза · ESC — зупинити", "F6 — pause · ESC — stop"), 12, Muted));
        modeButton.ToolTip = T("Адаптивний пензель, прискорення та перевірка покриття доступні в розширеному режимі. Завершення команд саме по собі не підтверджує покриття.",
            "Adaptive brush, acceleration and coverage verification are available in advanced mode. Completed commands alone do not verify coverage.");
        stats = new TextBlock(); swatches = new();
    }

    private void BuildSimpleCapture()
    {
        var page = FormContent(); pages["capture"] = Scroll(page);
        page.Children.Add(Text(T("Полотно Rust", "Rust Canvas"), 24));
        page.Children.Add(Card(T("Два виділення — і можна малювати", "Two selections to start painting"), out var setup));
        setup.Children.Add(Text(T("Відкрий редактор картини Rust. Виділи внутрішню область полотна, потім палітру 4×16 або поле HEX. Тести швидкості й калібрування пензля для цього режиму не потрібні.",
            "Open Rust's painting editor. Select the inside of Canvas, then the 4×16 palette or HEX field. Speed tests and brush calibration are not required for this mode."), 13));
        setup.Children.Add(AsyncButton(T("Вибрати полотно й кольори", "Select Canvas and colors"), CaptureSimpleWizard, true));
        setup.Children.Add(AsyncButton(T("Змінити лише полотно", "Change Canvas only"), () => Capture("canvas", T("ПОЛОТНО", "CANVAS"))));
        setup.Children.Add(AsyncButton(T("Змінити лише кольори", "Change colors only"), () => Capture(settings.Mode == ColorMode.HexDirect ? "hex" : "palette",
            settings.Mode == ColorMode.HexDirect ? T("Поле HEX — 6 цифр", "HEX field — 6 digits") : T("Палітра 4×16", "Palette 4×16"))));
        captureStatus = Text("", 13); setup.Children.Add(captureStatus);
        setup.Children.Add(Button(T("До малювання", "Back to painting"), () => ShowPage("paint")));
    }

    private async Task CaptureSimpleWizard()
    {
        try
        {
            var (screen, shot) = await CaptureShot();
            var canvas = Select(shot, screen, T("1/2 · Виділи внутрішнє полотно, без рамки", "1/2 · Select the inside of Canvas, without the frame"), live: true);
            if (canvas is null) { SetStatus(T("Вибір скасовано.", "Selection cancelled.")); return; }
            var key = settings.Mode == ColorMode.HexDirect ? "hex" : "palette";
            var colors = Select(shot, screen, key == "hex" ? T("2/2 · Виділи поле HEX із шістьма цифрами", "2/2 · Select the HEX field with six digits")
                : T("2/2 · Виділи всю палітру 4×16", "2/2 · Select the whole 4×16 palette"));
            if (colors is null) { SetStatus(T("Вибір скасовано. Попередні області збережено.", "Selection cancelled. Previous regions retained.")); return; }
            PrepareCaptureFrame();
            var cal = new Calibration((System.Text.Json.Nodes.JsonObject)settings.Calibration.Data.DeepClone());
            cal.SetRect("canvas", canvas.Value); cal.SetRect(key, colors.Value); PostCapture(cal, key, shot, screen);
            if (key == "palette") cal.SetRect("quick", default);
            settings.SetCalibration(cal);
            if (key == "palette") RefreshPalette(shot, screen);
            capturedCanvas = ImageProcessing.Crop(shot, canvas.Value.Left - screen.Left, canvas.Value.Top - screen.Top, canvas.Value.Right - screen.Left, canvas.Value.Bottom - screen.Top);
            Dirty();
        }
        finally { Show(); Activate(); UpdateReady(); }
        currentPage = "paint"; ShowPage("paint"); await BuildPlan();
    }

    private void UpdateSimpleReady()
    {
        var s = EffectiveSettings;
        string? missing = source is null ? T("Відкрий зображення.", "Open an image.") : DrawingWorkflow.SimpleSetupProblem(s) is { } problem ? SimpleProblem(problem) : null;
        if (numberErrors.Count > 0) missing = numberErrors.Values.First();
        ready.Text = missing ?? T("Готово. Перевір пензель у грі й натисни «Малювати».", "Ready. Check the in-game brush and press Paint.");
        ready.Foreground = missing is null ? Success : Warning;
        badge.Text = missing is null ? T("ГОТОВО", "READY") : T("КРОК 1 АБО 2", "STEP 1 OR 2"); badge.Foreground = ready.Foreground; badge.ToolTip = ready.Text;
        startButton.IsEnabled = !Painting && missing is null; startButton.ToolTip = missing;
        var resume = ResumeProblem(); resumeButton.IsEnabled = !Painting && missing is null && resume is null; resumeButton.ToolTip = resume;
        pauseButton.IsEnabled = stopButton.IsEnabled = Painting; simpleSetupButton.IsEnabled = !Painting;
        workflowChips["image"].Set(source is null ? T("Очікує", "Pending") : T("Готово", "Ready"), source is null ? Warning : Success);
        var problemSetup = DrawingWorkflow.SimpleSetupProblem(s);
        workflowChips["rust"].Set(problemSetup is null ? T("Готово", "Ready") : T("Очікує", "Pending"), problemSetup is null ? Success : Warning);
        captureStatus.Text = problemSetup is null ? T("Полотно й кольори збережені. Повторні тести не потрібні.", "Canvas and colors are saved. No repeated tests required.") : SimpleProblem(problemSetup);
    }
}
