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
        hex.Children.Add(Text(T("Відкрий HEX-палітру в Rust і захопи форми пензля та три зелені смуги. Координати зберігаються окремо від звичайної палітри.", "Open the HEX palette in Rust and capture brush shapes and the three green slider tracks. These coordinates are stored separately."), 12, Muted));
        var manual = new StackPanel();
        page.Children.Add(new Expander { Header = T("⚙ Ручне калібрування", "⚙ Manual calibration"), Content = manual });
        manual.Children.Add(Card("Палітра і прев’ю", out var pal));
        pal.Children.Add(AsyncButton(T("Обвести палітру 4×16", "Capture palette 4×16"), () => Capture("palette", "PALETTE 4×16")));
        pal.Children.Add(AsyncButton("Quick Colors", () => Capture("quick", "QUICK COLORS — 1×10")));
        pal.Children.Add(AsyncButton(T("Поточний color swatch (опційно)", "Current color swatch (optional)"), () => Capture("swatch", "COLOR SWATCH")));
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
        manual.Children.Add(Card("Точне калібрування controls", out var controls));
        foreach (var(key, title)in new[]
        {
            ("hard_brush", "Круглий"),
            ("square_brush", "Квадратний"),
        }

        )
            controls.Children.Add(AsyncButton(T(title), () => CapturePoint(key, T(title))));
        foreach (var kind in new[]
        {
            "size",
            "interval",
            "opacity"
        }

        )
            controls.Children.Add(AsyncButton(kind.ToUpperInvariant(), () => Capture(kind + "_track", kind.ToUpperInvariant() + " — " + T("лише доріжка повзунка", "slider track only"))));
        controls.Children.Add(AsyncButton(T("Захопти Size anchors 1/3/10/20", "Capture Size anchors 1/3/10/20"), CaptureSizeAnchors));
        controls.Children.Add(AsyncButton(T("Перевірити Rust controls", "Test Rust controls"), TestControls));
        controls.Children.Add(AsyncButton(T("Калібрувати пензель 1/3/10/20", "Calibrate brush 1/3/10/20"), CalibrateBrush));
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

    private ScreenRect? Select(PixelImage shot, ScreenRect screen, string title, bool point = false, bool live = false)
    {
        var selector = new CaptureWindow(shot, screen, T(title), point, live ? plan?.Preview : null, English);
        return selector.ShowDialog() == true ? selector.Selected : null;
    }

    private async Task Capture(string key, string title)
    {
        try
        {
            var(screen, shot) = await CaptureShot();
            var rect = Select(shot, screen, title, false, key == "canvas");
            if (rect is null)
                return;
            PrepareCaptureFrame();
            var cal = settings.Calibration;
            cal.SetRect(key, rect.Value);
            PostCapture(cal, key);
            settings.SetCalibration(cal);
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

    private async Task CaptureSizeAnchors()
    {
        if (!settings.Calibration.Rect("size_track").Valid)
            throw new InvalidOperationException(T("Спершу захопи доріжку повзунка SIZE.", "Capture the SIZE slider track first."));
        try
        {
            foreach (var size in new[] { 1, 3, 10, 20 })
            {
                if (MessageBox.Show(T(
                    $"У Rust встанови Size = {size}, потім натисни OK і познач центр повзунка Size. Скасувати — пропустити решту розмірів.",
                    $"In Rust set Size = {size}, then press OK and mark the center of the Size slider thumb. Cancel to skip the remaining sizes."),
                    "Size anchors", MessageBoxButton.OKCancel) != MessageBoxResult.OK)
                    break;
                var (screen, shot) = await CaptureShot();
                var rect = Select(shot, screen, $"SIZE {size} — " + T("центр повзунка", "slider thumb center"), true);
                if (rect is null)
                    break;
                PrepareCaptureFrame();
                var cal = settings.Calibration;
                cal.SetPoint("size_anchor_" + size, new(rect.Value.Left, rect.Value.Top));
                settings.SetCalibration(cal);
                Dirty();
            }

            Save();
        }
        finally
        {
            Show();
            Activate();
            UpdateReady();
        }
    }

    private static void PostCapture(Calibration cal, string key)
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
            var inset = Math.Max(2, (int)Math.Round(r.Width * .012));
            cal.SetPoint(kind + "_min", new(r.Left + inset, r.Center.Y));
            cal.SetPoint(kind + "_max", new(Math.Max(r.Left + inset + 1, r.Right - inset), r.Center.Y));
        }
    }

    private async Task CaptureHexControls()
    {
        try
        {
            var (screen, shot) = await CaptureShot();
            Calibration? cal = null;
            var steps = new[] { ("brush_shapes", "HEX — 7 brush shapes"), ("size_track", "HEX SIZE — green slider track"), ("interval_track", "HEX INTERVAL — green slider track"), ("opacity_track", "HEX OPACITY — green slider track") };
            for (var i = 0; i < steps.Length; i++)
            {
                var (key, title) = steps[i];
                var rect = Select(shot, screen, $"{i + 1}/{steps.Length} {title}");
                if (rect is null) return;
                if (cal is null)
                {
                    PrepareCaptureFrame();
                    cal = new Calibration((System.Text.Json.Nodes.JsonObject)settings.Calibration.Data.DeepClone());
                }
                cal.SetRect(key, rect.Value);
                PostCapture(cal, key);
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
                ("size_track", "SIZE — slider track"),
                ("interval_track", "INTERVAL — slider track"),
                ("opacity_track", "OPACITY — slider track")
            };
            Calibration? cal = null;
            for (var i = 0; i < steps.Length; i++)
            {
                var(key, title) = steps[i];
                var r = Select(shot, screen, $"{i + 1}/{steps.Length} {title}", false, key == "canvas");
                if (r is null)
                    break;
                if (cal is null)
                {
                    PrepareCaptureFrame();
                    cal = settings.Calibration;
                }
                cal.SetRect(key, r.Value);
                PostCapture(cal, key);
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
        ReadSettings();
        var target = AlignRustForTest();
        var cal = settings.PaintCalibration();
        var r = cal.Rect("canvas");
        if (!r.Valid || r.Width < 240 || r.Height < 240 || cal.Point("size_min") is null)
            throw new InvalidOperationException(T("Захопи Canvas від 240×240 px і повзунок Size.", "Capture a Canvas of at least 240×240 px and the Size slider."));
        if (settings.Int("brush_shape_slot", 3) is not (3 or 4))
            throw new InvalidOperationException(T("Вибери суцільний круглий пензель (3) або квадратний (4).", "Choose the solid round brush (3) or square brush (4)."));
        var origin = cal.SessionClient!.Value;
        var size = cal.SessionSize!.Value;
        var dpi = cal.SessionDpi;
        if (MessageBox.Show(T("Калібрування намалює 4 точки розмірами 1/3/10/20. Потрібні чистий Canvas і контрастний колір. Після тесту очисти Canvas. Продовжити?", "Calibration draws 4 dots at sizes 1/3/10/20. Use a clean Canvas and contrasting color. Clear Canvas afterwards. Continue?"), "Brush calibration", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        if (Native.FindRustAt(r.Center) != target) throw new InvalidOperationException("Canvas is outside Rust.");
        void CheckFrame()
        {
            if (!Native.IsRust(target) || Native.GetForegroundWindow() != target)
                throw new InvalidOperationException("Rust lost focus.");
            if (Native.DpiOf(target) != dpi)
                throw new InvalidOperationException(CalibrationSession.DpiChangedMessage);
            if (Native.ClientOrigin(target) != origin || Native.ClientSize(target) != size)
                throw new InvalidOperationException(T("Вікно Rust змінило розмір або положення. Повтори захоплення."));
        }
        settings.Data.Remove("brush_calibration_context");
        Save();
        Hide();
        try
        {
            await Task.Delay(1500);
            Native.SetForegroundWindow(target);
            var points = await Task.Run(() =>
            {
                var measured = new List<double[]>();
                var sizes = new[] { 1.0, 3, 10, 20 };
                for (int i = 0; i < sizes.Length; i++)
                {
                    if (Native.Down(0x1B)) throw new OperationCanceledException();
                    CheckFrame();
                    var s = settings.Clone();
                    s.Set("adaptive_brush", false);
                    s.Set("coverage_mode", "Fast");
                    s.Set("force_precision_controls", false);
                    s.Set("auto_brush_size", false);
                    s.Set("brush_size_value", sizes[i]);
                    s.Set("use_fixed_opacity", true);
                    s.Set("paint_opacity_value", 1);
                    var worker = new Painter(s, target, ResumePath, LogPath, _ => { }, CancellationToken.None);
                    worker.ApplyControls();
                    CheckFrame();
                    var p = new ScreenPoint(r.Left + r.Width * (i % 2 == 0 ? 1 : 3) / 4, r.Top + r.Height * (i < 2 ? 1 : 3) / 4);
                    int radius = Math.Min(120, Math.Min(r.Width, r.Height) / 4 - 4);
                    var area = new ScreenRect(p.X - radius, p.Y - radius, p.X + radius + 1, p.Y + radius + 1);
                    var before = Native.Screenshot(area);
                    Native.SetCursorPos(p.X, p.Y);
                    Native.Mouse(false);
                    try { Thread.Sleep(80); }
                    finally { Native.Mouse(true); }
                    Thread.Sleep(180);
                    CheckFrame();
                    var after = Native.Screenshot(area);
                    bool Changed(int x, int y)
                    {
                        var a = before.Color(y * area.Width + x);
                        var b = after.Color(y * area.Width + x);
                        return Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B))) > 12;
                    }
                    int outer = -1;
                    for (int y = 0; y < area.Height; y++) for (int x = 0; x < area.Width; x++)
                        if (Changed(x, y)) outer = Math.Max(outer, Math.Max(Math.Abs(x - radius), Math.Abs(y - radius)));
                    if (outer < 0 || outer >= radius - 2 || !Changed(radius, radius))
                        throw new InvalidOperationException("Brush measurement failed or was clipped. Use a clean Canvas and contrasting color.");
                    int inner = 0;
                    for (int a = 1; a <= outer; a++)
                    {
                        bool full = true;
                        for (int k = -a; k <= a; k++)
                            full &= Changed(radius + k, radius - a) && Changed(radius + k, radius + a)
                                && Changed(radius - a, radius + k) && Changed(radius + a, radius + k);
                        if (!full) break;
                        inner = a;
                    }
                    measured.Add([sizes[i], outer * 2 + 1, inner * 2 + 1]);
                }
                return measured;
            });
            settings.Set("brush_calibration_points", points);
            settings.Set("brush_calibration_context", AdaptiveBrush.Context(settings));
            Dirty();
            SetStatus("Brush calibration OK: " + string.Join(", ", points.Select(x => $"{x[0]} → {x[1]} px")));
        }
        finally
        {
            Native.Release();
            Show();
            Activate();
        }
    }
}
