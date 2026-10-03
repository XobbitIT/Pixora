using System.Windows;
using System.Windows.Controls;
using CanvasForge.Core;

namespace CanvasForge.App;
internal sealed partial class MainWindow
{
    private void BuildCapture()
    {
        var page = new StackPanel();
        pages["capture"] = Scroll(page);
        page.Children.Add(Text(T("Захоплення Rust"), 24));
        page.Children.Add(Card("Налаштування Rust", out var hero));
        hero.Children.Add(Text(T("Відкрий редактор картини Rust. Далі Pixora проведе через 7 коротких кроків. Перед запуском вибери пензель у грі.", "Open the Rust painting editor. Pixora will guide you through 7 short steps. Select the brush in game before starting."), 12, Muted));
        hero.Children.Add(AsyncButton(T("Почати налаштування", "Start setup"), CaptureWizard, true));
        hero.Children.Add(AsyncButton(T("Лише змінити Canvas", "Change Canvas only"), () => Capture("canvas", "CANVAS")));
        page.Children.Add(Card("Canvas / Палітра", out var info));
        captureStatus = Text("—", 11, Muted);
        captureStatus.FontFamily = new("Consolas");
        info.Children.Add(captureStatus);
        page.Children.Add(Card("HEX Direct", out var hex));
        hex.Children.Add(Text(T("Захопи поле з шістьма HEX-цифрами, потім виконай тест. Символ # вводити не потрібно.", "Capture the field with six HEX digits, then run the test. Do not include #."), 12, Muted));
        hex.Children.Add(AsyncButton(T("1. Захопити HEX", "1. Capture HEX"), () => Capture("hex", "HEX — 6 digits")));
        hex.Children.Add(AsyncButton(T("2. Тест HEX (3 кольори)", "2. Test HEX (3 colors)"), TestHex));
        hex.Children.Add(AsyncButton(T("3. Пензель і повзунки HEX", "3. HEX brush and sliders"), CaptureHexControls));
        hex.Children.Add(Text(T("Відкрий HEX-палітру Rust і захопи форми пензля та кожну зелену смугу разом із числовим полем справа. Робочі межі визначаються автоматично.", "Open the HEX palette in Rust and capture brush shapes and each complete green bar including its numeric field. Interactive boundaries are detected automatically."), 12, Muted));
        var manual = new StackPanel();
        page.Children.Add(new Expander { Header = T("⚙ Ручне калібрування", "⚙ Manual calibration"), Content = manual });
        manual.Children.Add(Card("Палітра і прев’ю", out var pal));
        pal.Children.Add(AsyncButton(T("Обвести палітру 4×16", "Capture palette 4×16"), () => Capture("palette", "PALETTE 4×16")));
        pal.Children.Add(AsyncButton("Quick Colors", () => Capture("quick", "QUICK COLORS — 1×10")));
        pal.Children.Add(AsyncButton(T("Поточний color swatch (опційно)", "Current color swatch (optional)"),
            () => Capture(settings.Mode == ColorMode.HexDirect ? "swatch" : "palette_swatch", "COLOR SWATCH")));
        pal.Children.Add(Button(T("Показати вставку", "Show insertion"), ShowInsertion));
        manual.Children.Add(Card("Додаткові області", out var extra));
        foreach (var(key, title)in new[]
        {
            ("brush_shapes", "Форми пензля"),
            ("top_toolbar", "Верхня панель"),
            ("save_cancel", "Save / Cancel"),
            ("full_ui", "Повний інтерфейс Rust")
        }

        )
            extra.Children.Add(AsyncButton(T(title), () => Capture(key, T(title))));
        page.Children.Insert(3, Card("Числові поля Size / Interval / Opacity", out var controls));
        foreach (var(key, title)in new[]
        {
            ("hard_brush", "Круглий"),
            ("square_brush", "Квадратний"),
        }

        )
            extra.Children.Add(AsyncButton(T(title), () => CapturePoint(key, T(title))));
        foreach (var kind in new[]
        {
            "size",
            "interval",
            "opacity"
        }

        )
            controls.Children.Add(AsyncButton(kind.ToUpperInvariant(), () => Capture(kind + "_track", kind.ToUpperInvariant() + " — " + T("повзунок із числом, обведи із запасом", "slider and number, select with extra space"))));
        controls.Children.Add(Text(T("Числа вводяться напряму. Ручні Size anchors більше не потрібні.", "Values are entered directly. Manual Size anchors are no longer needed."), 11, Muted));
        controls.Children.Add(AsyncButton(T("Перевірити Rust controls", "Test Rust controls"), TestControls));
        controls.Children.Add(Button(T("◉ Адаптивний режим і калібрування", "◉ Adaptive mode and calibration"), () => ShowPage("adaptive")));
    }

    private IntPtr captureWindow;
    private ScreenPoint captureOrigin;
    private ScreenRect captureWindowRect;
    private int captureDpi;
    private ScreenSize captureSize;

    private async Task<(ScreenRect Screen, PixelImage Shot)> CaptureShot()
    {
        if (Painting)
            throw new InvalidOperationException("STOP before capture.");
        ReadSettings();
        Hide();
        await Task.Delay(1500);
        captureWindow = Native.FindRust();
        if (!Native.IsRust(captureWindow) || !Native.GetWindowRect(captureWindow, out var bounds))
            throw new InvalidOperationException(T("Не знайдено вікно Rust. Переконайся, що гра відкрита, і повтори захоплення."));
        captureOrigin = Native.ClientOrigin(captureWindow);
        captureWindowRect = bounds.ToScreen();
        captureDpi = Native.DpiOf(captureWindow);
        captureSize = Native.ClientSize(captureWindow);
        var screen = Native.VirtualScreen;
        var shot = await Task.Run(() => Native.Screenshot(screen));
        return (screen, shot);
    }

    private void PrepareCaptureFrame()
    {
        // Commit a selected region against the frame that produced its screenshot.
        // Cancellation leaves the existing calibration intact.
        if (!Native.IsRust(captureWindow) || !Native.GetWindowRect(captureWindow, out var bounds)
            || bounds.ToScreen() != captureWindowRect || Native.ClientOrigin(captureWindow) != captureOrigin
            || Native.DpiOf(captureWindow) != captureDpi)
            throw new InvalidOperationException(T("Вікно Rust змінило розмір або положення. Повтори захоплення."));
        var cal = settings.Calibration;
        if (cal.SessionClient is not null && (cal.SessionDpi != captureDpi
            || cal.SessionSize is { } previous && previous != captureSize))
            CalibrationSession.Reset(settings);
        CalibrationSession.Align(settings, captureOrigin, captureDpi, captureSize);
    }

    private ScreenRect? Select(PixelImage shot, ScreenRect screen, string title, bool point = false, bool live = false, bool slider = false)
    {
        var selector = new CaptureWindow(shot, screen, T(title), point, live ? plan?.Preview : null, English, slider);
        return selector.ShowDialog() == true ? selector.Selected : null;
    }

    private async Task Capture(string key, string title)
    {
        try
        {
            var(screen, shot) = await CaptureShot();
            var rect = Select(shot, screen, title, false, key == "canvas", key.EndsWith("_track"));
            if (rect is null)
                return;
            PrepareCaptureFrame();
            var paintControl = key.EndsWith("_track") && settings.Mode == ColorMode.HexDirect;
            var cal = new Calibration((System.Text.Json.Nodes.JsonObject)(paintControl
                ? settings.PaintCalibration() : settings.Calibration).Data.DeepClone());
            cal.SetRect(key, rect.Value);
            PostCapture(cal, key, shot, screen);
            if (paintControl) settings.SetPaintCalibration(cal);
            else settings.SetCalibration(cal);
            if (key is "palette" or "quick")
                RefreshPalette(shot, screen);
            if (key == "canvas")
                capturedCanvas = ImageProcessing.Crop(shot, rect.Value.Left - screen.Left, rect.Value.Top - screen.Top, rect.Value.Right - screen.Left, rect.Value.Bottom - screen.Top);
            Dirty();
        }
        finally
        {
            Show();
            Activate();
            UpdateReady();
        }

        await BuildPlan();
        if (key == "canvas" && settings.Bool("auto_insert_preview", true) && plan is not null)
            ShowInsertion();
    }

    private async Task CapturePoint(string key, string title)
    {
        try
        {
            var(screen, shot) = await CaptureShot();
            var rect = Select(shot, screen, title, true);
            if (rect is null)
                return;
            PrepareCaptureFrame();
            var cal = settings.Calibration;
            cal.SetPoint(key, new(rect.Value.Left, rect.Value.Top));
            settings.SetCalibration(cal);
            Dirty();
        }
        finally
        {
            Show();
            Activate();
            UpdateReady();
        }
    }

    private static void PostCapture(Calibration cal, string key, PixelImage shot, ScreenRect screen)
    {
        if (key == "hex")
        {
            cal.Set("hex_verified", 0);
            cal.SetPoint("hex_field", cal.Rect("hex").Center);
        }

        if (key == "swatch")
            cal.SetPoint("color_swatch", cal.Rect("swatch").Center);
        if (key == "palette")
        {
            cal.Set("palette_cols", 4);
            cal.Set("palette_rows", 16);
        }

        if (key == "quick")
            cal.Set("quick_rows", 10);
        if (key == "tool_row")
        {
            var points = cal.GridCenters(key, 3, 1);
            for (var i = 0; i < 3; i++)
                cal.SetPoint(new[] { "brush_tool", "eraser_tool", "eyedropper_tool" }[i], points[i]);
        }

        if (key == "save_cancel")
        {
            var points = cal.GridCenters(key, 2, 1);
            cal.SetPoint("save_button", points[0]);
            cal.SetPoint("cancel_button", points[1]);
        }

        if (key.EndsWith("_track"))
        {
            var kind = key[..^6];
            var r = cal.Rect(key);
            var hint = new ScreenRect(r.Left - screen.Left, r.Top - screen.Top, r.Right - screen.Left, r.Bottom - screen.Top);
            var read = RustSlider.Capture(shot, hint);
            var track = read.Track;
            r = new(track.Left + screen.Left, track.Top + screen.Top, track.Right + screen.Left, track.Bottom + screen.Top);
            cal.SetRect(key, r);
            var field = read.ValueField;
            cal.SetRect(kind + "_value_field", new(field.Left + screen.Left, field.Top + screen.Top,
                field.Right + screen.Left, field.Bottom + screen.Top));
            cal.SetPoint(kind + "_min", new(r.Left, r.Center.Y));
            cal.SetPoint(kind + "_max", new(r.Right - 1, r.Center.Y));
            if (kind == "size")
                foreach (var value in new[] { 1, 3, 10, 20 })
                {
                    cal.Data.Remove("size_anchor_" + value + "_x");
                    cal.Data.Remove("size_anchor_" + value + "_y");
                }
        }
    }

    private async Task CaptureHexControls()
    {
        try
        {
            var (screen, shot) = await CaptureShot();
            Calibration? cal = null;
            var steps = new[] { ("brush_shapes", "HEX — 7 brush shapes"),
                ("size_track", "HEX SIZE — " + T("повзунок із числом, обведи із запасом", "slider and number, select with extra space")),
                ("interval_track", "HEX INTERVAL — " + T("повзунок із числом, обведи із запасом", "slider and number, select with extra space")),
                ("opacity_track", "HEX OPACITY — " + T("повзунок із числом, обведи із запасом", "slider and number, select with extra space")) };
            for (var i = 0; i < steps.Length; i++)
            {
                var (key, title) = steps[i];
                var rect = Select(shot, screen, $"{i + 1}/{steps.Length} {title}", slider: key.EndsWith("_track"));
                if (rect is null) return;
                if (cal is null)
                {
                    PrepareCaptureFrame();
                    cal = new Calibration((System.Text.Json.Nodes.JsonObject)settings.Calibration.Data.DeepClone());
                }
                cal.SetRect(key, rect.Value);
                PostCapture(cal, key, shot, screen);
            }
            settings.Data["hex_controls"] = cal!.Data.DeepClone();
            Dirty();
            Save();
        }
        finally { Show(); Activate(); UpdateReady(); }
    }

    private async Task CaptureWizard()
    {
        try
        {
            var(screen, shot) = await CaptureShot();
            var steps = new[]
            {
                ("canvas", "CANVAS"),
                ("palette", "PALETTE 4×16"),
                ("quick", "QUICK COLORS 1×10"),
                ("brush_shapes", T("7 форм пензля", "7 brush shapes")),
                ("size_track", "SIZE — " + T("повзунок із числом, обведи із запасом", "slider and number, select with extra space")),
                ("interval_track", "INTERVAL — " + T("повзунок із числом, обведи із запасом", "slider and number, select with extra space")),
                ("opacity_track", "OPACITY — " + T("повзунок із числом, обведи із запасом", "slider and number, select with extra space"))
            };
            Calibration? cal = null;
            for (var i = 0; i < steps.Length; i++)
            {
                var(key, title) = steps[i];
                var r = Select(shot, screen, $"{i + 1}/{steps.Length} {title}", false, key == "canvas", key.EndsWith("_track"));
                if (r is null)
                    break;
                if (cal is null)
                {
                    PrepareCaptureFrame();
                    cal = new Calibration((System.Text.Json.Nodes.JsonObject)settings.Calibration.Data.DeepClone());
                }
                cal.SetRect(key, r.Value);
                PostCapture(cal, key, shot, screen);
                settings.SetCalibration(cal);
                if (key is "palette" or "quick")
                    RefreshPalette(shot, screen);
                if (key == "canvas")
                    capturedCanvas = ImageProcessing.Crop(shot, r.Value.Left - screen.Left, r.Value.Top - screen.Top, r.Value.Right - screen.Left, r.Value.Bottom - screen.Top);
                Save();
            }

            if (cal is null) return;
            Save();
            Dirty();
        }
        finally
        {
            Show();
            Activate();
            UpdateReady();
        }

        await BuildPlan();
    }

    private void RefreshPalette(PixelImage shot, ScreenRect screen)
    {
        var cal = settings.Calibration;
        var entries = new List<PaletteEntry>();
        foreach (var(key, cols, rows, kind)in new[]
        {
            ("palette", 4, 16, "main"),
            ("quick", 1, cal.Get("quick_rows", 10), "quick")
        }

        )
        {
            var rect = cal.Rect(key);
            if (!rect.Valid)
                continue;
            var centers = cal.GridCenters(key, cols, rows);
            foreach (var p in centers)
            {
                var hw = Math.Max(1, (int)(rect.Width / (double)cols * .21));
                var hh = Math.Max(1, (int)(rect.Height / (double)rows * .21));
                var rr = new List<byte>();
                var gg = new List<byte>();
                var bb = new List<byte>();
                for (var y = Math.Max(0, p.Y - screen.Top - hh); y < Math.Min(shot.Height, p.Y - screen.Top + hh + 1); y++)
                    for (var x = Math.Max(0, p.X - screen.Left - hw); x < Math.Min(shot.Width, p.X - screen.Left + hw + 1); x++)
                    {
                        var c = shot.Color(y * shot.Width + x);
                        rr.Add(c.R);
                        gg.Add(c.G);
                        bb.Add(c.B);
                    }

                if (rr.Count == 0)
                    continue;
                rr.Sort();
                gg.Sort();
                bb.Sort();
                var color = new Rgb(rr[rr.Count / 2], gg[gg.Count / 2], bb[bb.Count / 2]);
                if (!entries.Any(e => Math.Max(Math.Abs(e.Color.R - color.R), Math.Max(Math.Abs(e.Color.G - color.G), Math.Abs(e.Color.B - color.B))) <= 3))
                    entries.Add(new(color, p, kind));
            }
        }

        settings.SetPalette(entries);
    }

    private IntPtr AlignRustForTest()
    {
        var target = Native.FindRust();
        if (!Native.IsRust(target))
            target = Native.FindRustAt(settings.Calibration.Rect("canvas").Center);
        if (!Native.IsRust(target))
            throw new InvalidOperationException(T("Не знайдено вікно Rust. Переконайся, що гра відкрита, і повтори захоплення."));
        var moved = CalibrationSession.Align(settings, Native.ClientOrigin(target), Native.DpiOf(target), Native.ClientSize(target));
        if (!moved.IsIdentity) Dirty();
        Save();
        return target;
    }

    private async Task TestHex()
    {
        ReadSettings();
        var target = AlignRustForTest();
        var cal = settings.Calibration;
        var point = cal.HexPoint ?? throw new InvalidOperationException(T("Захопи HEX.", "Capture HEX."));
        if (Native.FindRustAt(point) != target)
            throw new InvalidOperationException("HEX field is outside Rust.");
        cal.Set("hex_verified", 0);
        settings.SetCalibration(cal);
        Save();
        Hide();
        try
        {
            await Task.Delay(1500);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var worker = new Painter(settings, target, ResumePath, LogPath, _ =>
            {
            }, timeout.Token);
            Native.SetForegroundWindow(target);
            if (Native.GetForegroundWindow() != target)
                throw new InvalidOperationException("Не вдалося передати фокус Rust. Повернись у гру і повтори тест.");
            var ok = await Task.Run(() =>
            {
                foreach (var color in new[] { new Rgb(255, 51, 51), new Rgb(18, 172, 95), new Rgb(3, 151, 226) })
                    if (!worker.ApplyHex(color, true)) return false;
                return true;
            });
            cal.Set("hex_verified", ok ? 1 : 0);
            settings.SetCalibration(cal);
            Dirty();
            SetStatus(ok ? "HEX VERIFIED" : "HEX FAILED");
            if (!ok)
                throw new InvalidOperationException(T("Rust не підтвердив усі тестові HEX-кольори. Перевір поле й записи hex_readback у логу.", "Rust did not confirm all test HEX colors. Check the field and hex_readback log."));
        }
        finally
        {
            Native.Release();
            Show();
            Activate();
            UpdateReady();
        }
    }

    private async Task TestControls()
    {
        ReadSettings();
        var target = AlignRustForTest();
        var canvas = settings.Calibration.Rect("canvas");
        if (!canvas.Valid)
            throw new InvalidOperationException("Capture Canvas.");
        if (Native.FindRustAt(canvas.Center) != target)
            throw new InvalidOperationException("Canvas is outside Rust.");
        Hide();
        try
        {
            await Task.Delay(1500);
            Native.SetForegroundWindow(target);
            var s = settings.Clone();
            s.Set("verify_controls", true);
            var worker = new Painter(s, target, ResumePath, LogPath, _ =>
            {
            }, CancellationToken.None);
            await Task.Run(worker.ApplyControls);
            SetStatus(T("Rust controls перевірено.", "Rust controls verified."));
        }
        finally
        {
            Native.Release();
            Show();
            Activate();
        }
    }

    private async Task CalibrateBrush()
    {
        if (Painting) return;
        ReadSettings();
        var problem = AdaptiveBrush.SetupProblem(settings);
        if (problem is not null) throw new InvalidOperationException(T(problem));
        var target = AlignRustForTest();
        var cal = settings.PaintCalibration();
        var r = settings.Calibration.Rect("canvas");
        var origin = cal.SessionClient!.Value;
        var size = cal.SessionSize!.Value;
        var dpi = cal.SessionDpi;
        if (MessageBox.Show(T("Відкрий чистий Canvas. Pixora сама вибере контрастний колір, введе Size 1/3/10/20 і намалює 4 крапки. Не рухай мишу; ESC — скасувати. Після калібрування очисти Canvas. Почати?", "Open a clean Canvas. Pixora selects a contrasting color, enters Size 1/3/10/20, and draws 4 dots automatically. Do not move the mouse; ESC cancels. Clear Canvas afterwards. Start?"), T("Автоматичне калібрування", "Automatic calibration"), MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        if (Native.FindRustAt(r.Center) != target) throw new InvalidOperationException("Canvas is outside Rust.");
        void CheckFrame()
        {
            if (Native.Down(0x1B)) throw new OperationCanceledException();
            if (!Native.IsRust(target) || Native.GetForegroundWindow() != target) throw new InvalidOperationException("Rust lost focus.");
            if (Native.DpiOf(target) != dpi) throw new InvalidOperationException(CalibrationSession.DpiChangedMessage);
            if (Native.ClientOrigin(target) != origin || Native.ClientSize(target) != size)
                throw new InvalidOperationException(T("Вікно Rust змінило розмір або положення. Повтори захоплення."));
        }
        AdaptiveBrush.Prepare(settings);
        settings.Set("adaptive_brush", false);
        settings.Data.Remove("brush_calibration_context");
        settings.Set("brush_calibration_points", Array.Empty<double[]>());
        Dirty();
        BuildUi();
        SetEditing(false);
        Hide();
        double currentSize = 1;
        try
        {
            await Task.Delay(1500);
            Native.SetForegroundWindow(target);
            var points = await Task.Run(() =>
            {
                CheckFrame();
                var colorWorker = new Painter(AdaptiveBrush.CalibrationSettings(settings, 1), target, ResumePath, LogPath, _ => { }, CancellationToken.None);
                var color = colorWorker.SelectCalibrationColor(Native.Median(new(r.Left + r.Width / 4 - 4, r.Top + r.Height / 4 - 4, r.Left + r.Width / 4 + 5, r.Top + r.Height / 4 + 5)));
                var measured = new List<double[]>();
                var sizes = new[] { 1.0, 3, 10, 20 };
                var park = new ScreenPoint(cal.Rect("size_track").Left - 12, cal.Rect("size_track").Center.Y);
                for (int i = 0; i < sizes.Length; i++)
                {
                    currentSize = sizes[i];
                    CheckFrame();
                    var worker = new Painter(AdaptiveBrush.CalibrationSettings(settings, sizes[i]), target, ResumePath, LogPath, _ => { }, CancellationToken.None);
                    worker.ApplyControls(); CheckFrame();
                    var p = new ScreenPoint(r.Left + r.Width * (i % 2 == 0 ? 1 : 3) / 4, r.Top + r.Height * (i < 2 ? 1 : 3) / 4);
                    int radius = Math.Min(240, Math.Min(r.Width, r.Height) / 4 - 4);
                    var area = new ScreenRect(p.X - radius, p.Y - radius, p.X + radius + 1, p.Y + radius + 1);
                    // Rust draws its brush preview in the framebuffer. Park it outside
                    // the measured area in BOTH frames so the ghost cannot hide the dot.
                    Native.SetCursorPos(park.X, park.Y); Thread.Sleep(200); CheckFrame();
                    var before = Native.Screenshot(area);
                    Native.SetCursorPos(p.X, p.Y); Thread.Sleep(100); CheckFrame();
                    Native.Mouse(false);
                    try { Thread.Sleep(140); }
                    finally { Native.Mouse(true); }
                    Native.SetCursorPos(park.X, park.Y); Thread.Sleep(300); CheckFrame();
                    var after = Native.Screenshot(area);
                    BrushMeasurement result;
                    try { result = BrushMeasurement.Read(before, after, new(radius, radius)); }
                    catch (InvalidOperationException)
                    {
                        var diagnostic = Path.Combine(folder, "brush-calibration"); Directory.CreateDirectory(diagnostic);
                        Images.Save(before, Path.Combine(diagnostic, "failed-before.png"));
                        Images.Save(after, Path.Combine(diagnostic, "failed-after.png"));
                        throw;
                    }
                    measured.Add([sizes[i], result.OuterDiameter, result.InnerDiameter]);
                    File.AppendAllText(LogPath, System.Text.Json.JsonSerializer.Serialize(new { time = DateTimeOffset.UtcNow, action = "brush_measurement", details = new { size = sizes[i], result.OuterDiameter, result.InnerDiameter, color = color.Hex } }) + Environment.NewLine);
                }
                return measured;
            });
            settings.Set("brush_calibration_points", points);
            settings.Set("brush_calibration_context", AdaptiveBrush.Context(settings));
            settings.Set("adaptive_brush", true);
            adaptiveFailure = "";
            Dirty();
            SetStatus(T("Калібрування завершено. Адаптивний режим увімкнено; очисти Canvas перед START.", "Calibration complete. Adaptive mode is enabled; clear Canvas before START."));
        }
        catch (OperationCanceledException)
        {
            adaptiveFailure = T("Калібрування скасовано. Очисти Canvas й повтори.", "Calibration cancelled. Clear Canvas and retry.");
            SetStatus(adaptiveFailure);
        }
        catch (Exception e)
        {
            adaptiveFailure = T("Не вдалося виміряти Size", "Could not measure Size") + " " + currentSize + ": " + T(e.Message);
            throw new InvalidOperationException(adaptiveFailure, e);
        }
        finally
        {
            Native.Release(); Save(); BuildUi(); ShowPage("adaptive"); SetEditing(true); Show(); Activate();
        }
    }
}
