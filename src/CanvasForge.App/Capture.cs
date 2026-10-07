using System.Windows;
using System.Windows.Controls;
using CanvasForge.Core;

namespace CanvasForge.App;
internal sealed partial class MainWindow
{
    private void BuildCapture()
    {
        var page = FormContent();
        pages["capture"] = Scroll(page);
        page.Children.Add(Text(T("Захоплення Rust"), 24));
        page.Children.Add(Card("Налаштування Rust", out var hero));
        hero.Children.Add(Text(T("Відкрий редактор картини Rust. Далі Одна кнопка виконає всі перевірки. Якщо області ще не захоплені, виділи їх у семи кроках.", "Open the Rust painting editor. One button runs all checks. If regions are missing, select them in seven steps."), 12, Muted));
        AddAutomaticSetup(hero,true);
        hero.Children.Add(AsyncButton(T("Лише захопити області", "Capture regions only"), CaptureWizard));
        hero.Children.Add(AsyncButton(T("Лише змінити полотно", "Change Canvas only"), () => Capture("canvas", T("ПОЛОТНО", "CANVAS"))));
        page.Children.Add(Card(T("Полотно / палітра", "Canvas / palette"), out var info));
        captureStatus = Text("—", 11, Muted);
        info.Children.Add(captureStatus);
        page.Children.Add(Card("Числові поля розміру, інтервалу й прозорості", out var controls));
        page.Children.Add(Card(Option("color_mode","HEX Direct"), out var hex));
        hex.Children.Add(Text(T("Захопи поле з шістьма HEX-цифрами, потім виконай тест. Символ # вводити не потрібно.", "Capture the field with six HEX digits, then run the test. Do not include #."), 12, Muted));
        hex.Children.Add(AsyncButton(T("1. Захопити HEX", "1. Capture HEX"), () => Capture("hex", T("HEX — 6 цифр", "HEX — 6 digits"))));
        hex.Children.Add(AsyncButton(T("2. Тест HEX (3 кольори)", "2. Test HEX (3 colors)"), TestHex));
        hex.Children.Add(AsyncButton(T("3. Пензель і повзунки HEX", "3. HEX brush and sliders"), CaptureHexControls));
        hex.Children.Add(Text(T("Відкрий HEX-палітру Rust і захопи форми пензля та кожну зелену смугу разом із числовим полем справа. Робочі межі визначаються автоматично.", "Open the HEX palette in Rust and capture brush shapes and each complete green bar including its numeric field. Interactive boundaries are detected automatically."), 12, Muted));
        var manual = new StackPanel();
        page.Children.Add(new Expander { Header = T("Ручне калібрування", "Manual calibration"), Content = manual });
        manual.Children.Add(Card("Палітра і прев’ю", out var pal));
        pal.Children.Add(AsyncButton(T("Обвести палітру 4×16", "Capture palette 4×16"), () => Capture("palette", T("ПАЛІТРА 4×16", "PALETTE 4×16"))));
        pal.Children.Add(AsyncButton(T("Швидкі кольори", "Quick Colors"), () => Capture("quick", T("ШВИДКІ КОЛЬОРИ — 1×10", "QUICK COLORS — 1×10"))));
        pal.Children.Add(AsyncButton(T("Зразок активного кольору (необов’язково)", "Active color swatch (optional)"),
            () => Capture(settings.Mode == ColorMode.HexDirect ? "swatch" : "palette_swatch", T("ЗРАЗОК КОЛЬОРУ", "COLOR SWATCH"))));
        pal.Children.Add(Button(T("Показати вставку", "Show insertion"), ShowInsertion));
        manual.Children.Add(Card("Додаткові області", out var extra));
        foreach (var(key, title)in new[]
        {
            ("tool_row", T("Інструменти: пензель / гумка / піпетка", "Tools: brush / eraser / eyedropper")),
            ("brush_shapes", "Форми пензля"),
            ("top_toolbar", "Верхня панель"),
            ("save_cancel", T("Зберегти / скасувати", "Save / Cancel")),
            ("full_ui", "Повний інтерфейс Rust")
        }

        )
            extra.Children.Add(AsyncButton(T(title), () => Capture(key, T(title))));
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
            controls.Children.Add(AsyncButton(Option("control",kind), () => Capture(kind + "_track", Option("control",kind) + " — " + T("повзунок із числом, обведи із запасом", "slider and number, select with extra space"))));
        controls.Children.Add(Text(T("Числа вводяться напряму. Ручні опорні точки розміру більше не потрібні.", "Values are entered directly. Manual Size anchors are no longer needed."), 11, Muted));
        controls.Children.Add(AsyncButton(T("Перевірити керування Rust", "Test Rust controls"), TestControls));
        controls.Children.Add(Button(T("Калібрування пензля", "Brush calibration"), () => ShowPage("adaptive")));
    }

    private IntPtr captureWindow;
    private ScreenPoint captureOrigin;
    private ScreenRect captureWindowRect;
    private int captureDpi;
    private ScreenSize captureSize;

    private async Task<(ScreenRect Screen, PixelImage Shot)> CaptureShot()
    {
        if (Painting && !setupRunning)
            throw new InvalidOperationException("STOP before capture.");
        ReadSettings();
        Hide();
        await Task.Delay(1500,SetupToken);
        captureWindow = Native.FindRust();
        if (!Native.IsRust(captureWindow) || !Native.GetWindowRect(captureWindow, out var bounds))
            throw new InvalidOperationException(T("Не знайдено вікно Rust. Переконайся, що гра відкрита, і повтори захоплення."));
        Native.SetForegroundWindow(captureWindow);
        if(Native.GetForegroundWindow()!=captureWindow)throw new InvalidOperationException(T("Не вдалося передати фокус Rust. Повернись у гру і повтори тест."));
        await Task.Delay(300,SetupToken);
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
            var steps = new[] { ("brush_shapes", T("HEX — 7 форм пензля", "HEX — 7 brush shapes")),
                ("size_track", "HEX " + Option("control","size")+" — " + T("повзунок із числом, обведи із запасом", "slider and number, select with extra space")),
                ("interval_track", "HEX " + Option("control","interval")+" — " + T("повзунок із числом, обведи із запасом", "slider and number, select with extra space")),
                ("opacity_track", "HEX " + Option("control","opacity")+" — " + T("повзунок із числом, обведи із запасом", "slider and number, select with extra space")) };
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
            var (screen,shot)=await CaptureShot();
            var steps=new[]
            {
                ("canvas",T("ПОЛОТНО","CANVAS")),
                (settings.Mode==ColorMode.HexDirect?"hex":"palette",settings.Mode==ColorMode.HexDirect?T("HEX — 6 цифр","HEX — 6 digits"):T("ПАЛІТРА 4×16","PALETTE 4×16")),
                ("tool_row",T("3 інструменти: пензель, гумка, піпетка","3 tools: brush, eraser, eyedropper")),
                ("brush_shapes",T("7 форм пензля","7 brush shapes")),
                ("size_track",Option("control","size")+" — "+T("смуга разом із числом","bar including its number")),
                ("interval_track",Option("control","interval")+" — "+T("смуга разом із числом","bar including its number")),
                ("opacity_track",Option("control","opacity")+" — "+T("смуга разом із числом","bar including its number"))
            };
            var selected=new List<(string Key,ScreenRect Rect)>();
            for(int i=0;i<steps.Length;i++)
            {
                SetupToken.ThrowIfCancellationRequested();var (key,title)=steps[i];
                var rect=Select(shot,screen,$"{i+1}/{steps.Length} {title}",false,key=="canvas",key.EndsWith("_track"));
                if(rect is null){if(setupRunning)throw new OperationCanceledException();return;}
                selected.Add((key,rect.Value));
            }
            // Commit all seven selections together. Cancelling preserves the old capture.
            var captured=new Calibration(new());
            foreach(var (key,rect) in selected){captured.SetRect(key,rect);PostCapture(captured,key,shot,screen);}
            PrepareCaptureFrame();
            var cal=new Calibration((System.Text.Json.Nodes.JsonObject)settings.Calibration.Data.DeepClone());
            foreach(var entry in captured.Data)cal.Data[entry.Key]=entry.Value?.DeepClone();
            if(settings.Mode==ColorMode.RustPalette)cal.SetRect("quick",default);
            settings.SetCalibration(cal);
            if(settings.Mode==ColorMode.HexDirect)settings.Data["hex_controls"]=cal.Data.DeepClone();
            else RefreshPalette(shot,screen);
            var canvas=cal.Rect("canvas");
            capturedCanvas=ImageProcessing.Crop(shot,canvas.Left-screen.Left,canvas.Top-screen.Top,canvas.Right-screen.Left,canvas.Bottom-screen.Top);
            Dirty();Save();
        }
        finally {if(!closing){Show();Activate();UpdateReady();}}
        if(!setupRunning)await BuildPlan();
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
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(timeout.Token,SetupToken);
        try
        {
            await Task.Delay(1500,linked.Token);
            using var worker = new Painter(settings, target, ResumePath, LogPath, _ =>
            {
            }, linked.Token);
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
            SetStatus(ok ? T("HEX підтверджено", "HEX verified") : T("Перевірка HEX не пройдена", "HEX verification failed"));
            if (!ok)
                throw new InvalidOperationException(T("Rust не підтвердив усі тестові HEX-кольори. Перевір поле й записи hex_readback у логу.", "Rust did not confirm all test HEX colors. Check the field and hex_readback log."));
        }
        catch(OperationCanceledException e) when(timeout.IsCancellationRequested&&!SetupToken.IsCancellationRequested)
        {
            File.AppendAllText(LogPath,System.Text.Json.JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,action="hex_test_timeout",details=new{budgetSeconds=60}})+Environment.NewLine);
            throw new TimeoutException(T("Тест HEX перевищив ліміт 60 секунд. Перевір фокус Rust і повтори тест.","HEX test exceeded its 60-second budget. Check Rust focus and retry."),e);
        }
        finally
        {
            Native.Release();
            if(!closing){Show();Activate();UpdateReady();}
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
            await Task.Delay(1500,SetupToken);
            Native.SetForegroundWindow(target);
            var s = settings.Clone();
            s.Set("verify_controls", true);
            using var worker = new Painter(s, target, ResumePath, LogPath, _ =>
            {
            }, SetupToken);
            await Task.Run(worker.ApplyControls);
            SetStatus(T("Керування Rust перевірено.", "Rust controls verified."));
        }
        finally
        {
            Native.Release();
            if(!closing){Show();Activate();}
        }
    }

    private Task CalibrateBrush()=>CalibrateBrushSizes(false);
    private Task CalibrateFailedBrush()=>CalibrateBrushSizes(true);
    private async Task CalibrateBrushSizes(bool retryOnly)
    {
        if (Painting && !setupRunning) return;
        lastBrushProfiles=null;
        ReadSettings();
        var problem = AdaptiveBrush.SetupProblem(settings);
        if (problem is not null) throw new InvalidOperationException(T(problem));
        var target = AlignRustForTest();
        var cal = settings.PaintCalibration();
        var r = settings.Calibration.Rect("canvas");
        var origin = cal.SessionClient!.Value;
        var size = cal.SessionSize!.Value;
        var dpi = cal.SessionDpi;
        var selection=settings.Text("brush_calibration_size","1/3/10/20");
        double[] sizes=selection=="1/3/10/20"?[1,3,10,20]:[double.Parse(selection,System.Globalization.CultureInfo.InvariantCulture)];
        if(setupRunning&&!sizes.Contains(1))sizes=[1,..sizes];
        if(retryOnly)sizes=BrushSignalDiagnostics.RetrySizes(settings,sizes);
        if(sizes.Length==0){SetStatus(T("Усі вибрані Size вже підтверджені.","All selected Sizes are already verified."));return;}
        var tiles=setupWorkspace?.Brush(sizes)??BrushFootprints.Tiles(r,sizes); // Reject insufficient space before any input.
        if (!setupRunning && !ShowMessage(T($"Відкрий чисте полотно. Буде {tiles.Count} крапок: по три незалежні вимірювання кожного Size. Не рухай мишу; ESC — скасувати. Після калібрування очисти полотно. Почати?", $"Open a clean Canvas. {tiles.Count} dots will be drawn: three independent measurements per Size. Do not move the mouse; ESC cancels. Clear Canvas afterwards. Start?"), T("Автоматичне калібрування", "Automatic calibration"), true)) return;
        if (Native.FindRustAt(r.Center) != target) throw new InvalidOperationException("Canvas is outside Rust.");
        void CheckFrame()
        {
            SetupToken.ThrowIfCancellationRequested();
            if (Native.Down(0x1B)) throw new OperationCanceledException();
            if (!Native.IsRust(target) || Native.GetForegroundWindow() != target) throw new InvalidOperationException("Rust lost focus.");
            if (Native.DpiOf(target) != dpi) throw new InvalidOperationException(CalibrationSession.DpiChangedMessage);
            if (Native.ClientOrigin(target) != origin || Native.ClientSize(target) != size)
                throw new InvalidOperationException(T("Вікно Rust змінило розмір або положення. Повтори захоплення."));
        }
        AdaptiveBrush.Prepare(settings);
        settings.Set("adaptive_brush", false);
        Dirty();
        BuildUi();
        SetEditing(false);
        Hide();
        double currentSize = 1;
        int currentRepeat=0;
        string diagnosticPath="";
        try
        {
            await Task.Delay(1500,SetupToken);
            Native.SetForegroundWindow(target);
            var batch = await Task.Run(() =>
            {
                CheckFrame();
                using var worker = new Painter(AdaptiveBrush.CalibrationSettings(settings, sizes[0]), target, ResumePath, LogPath, _ => { }, SetupToken);
                worker.VerifyControlLayout();
                var color = worker.SelectCalibrationColor(Native.Median(new(r.Left + r.Width / 4 - 4, r.Top + r.Height / 4 - 4, r.Left + r.Width / 4 + 5, r.Top + r.Height / 4 + 5)));
                var measurements=new BrushCalibrationBatch(settings,settings.Int("brush_shape_slot",3),sizes);
                var diagnostic=Path.Combine(folder,"brush-calibration","run-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));
                diagnosticPath=diagnostic;
                Directory.CreateDirectory(diagnostic);
                try
                {
                foreach(var tile in tiles.OrderBy(t=>t.Size).ThenBy(t=>t.Repeat))
                {
                    if(!measurements.ShouldMeasure(tile.Size))continue;
                    currentSize = tile.Size;
                    currentRepeat=tile.Repeat+1;
                    CheckFrame();
                    worker.PrepareBrushCalibration(tile.Size); CheckFrame();
                    var p=tile.Command;var area=tile.Area;
                    // Calibration dots use the same guarded movement and settled,
                    // cursor-free capture as spatial controls and coverage audits.
                    PixelImage? background=null;
                    var before = worker.StableProbeShot(area,(_,_,_)=>{},(previous,_)=>background=previous); CheckFrame();
                    var stem=$"shape-{settings.Int("brush_shape_slot",3)}-size-{tile.Size}-repeat-{tile.Repeat+1}";
                    Images.Save(before,Path.Combine(diagnostic,stem+"-before.png"));
                    BrushDotTrace motion;
                    try{motion=worker.ProbeDot(p);}
                    catch(Exception e)
                    {
                        File.WriteAllText(Path.Combine(diagnostic,stem+"-metrics.json"),System.Text.Json.JsonSerializer.Serialize(new{
                            shape=settings.Int("brush_shape_slot",3),size=tile.Size,repeat=tile.Repeat+1,command=p,tile.Area,inputFailure=e.Message}));
                        throw;
                    }
                    var after = worker.StableProbeShot(area,(_,_,_)=>{}); CheckFrame();
                    Images.Save(after,Path.Combine(diagnostic,stem+"-after.png"));
                    // Recapture must not hide a clipped footprint or scene change.
                    // Weak contrast can be diagnosed; strict geometry errors abort.
                    try{BrushFootprints.Measure(before,after,new(p.X-area.Left,p.Y-area.Top),tile.Size,measurements.Reference(tile.Size));}
                    catch(BrushContrastException){}
                    var confirmed=BrushColorGuard.Confirm(before,after,color,()=>
                    {
                        Images.Save(after,Path.Combine(diagnostic,stem+"-suspect-after.png"));
                        CheckFrame();var next=worker.RecaptureBrushShot(area);CheckFrame();return next;
                    });
                    after=confirmed.Image;
                    Images.Save(after,Path.Combine(diagnostic,stem+"-after.png"));
                    if(background is null)throw new InvalidOperationException("Background measurement is unavailable.");
                    Images.Save(background,Path.Combine(diagnostic,stem+"-background.png"));
                    var signal=measurements.Record(tile.Size,tile.Repeat+1,background,before,after,new(p.X-area.Left,p.Y-area.Top),color);
                    File.WriteAllText(Path.Combine(diagnostic,stem+"-metrics.json"),System.Text.Json.JsonSerializer.Serialize(new{
                        shape=settings.Int("brush_shape_slot",3),size=tile.Size,repeat=tile.Repeat+1,command=p,tile.Area,
                        contrast=BrushFootprints.Contrast(before,after),backgroundNoise=BrushFootprints.Contrast(background,before),requestedColor=color.Hex,
                        colorCheck=confirmed.Final,initialColorCheck=confirmed.Initial,recaptured=confirmed.Retried,motion,geometry=signal.Geometry}));
                    var detail=new{shape=settings.Int("brush_shape_slot",3),size=tile.Size,repeat=tile.Repeat+1,command=p,tile.Area,
                        signal,motion,color=color.Hex,
                        initialColorCheck=confirmed.Initial,recaptured=confirmed.Retried};
                    File.WriteAllText(Path.Combine(diagnostic,stem+".json"),System.Text.Json.JsonSerializer.Serialize(detail));
                    File.AppendAllText(LogPath, System.Text.Json.JsonSerializer.Serialize(new { time = DateTimeOffset.UtcNow, action = "brush_measurement", details = detail }) + Environment.NewLine);
                }
                }
                finally {worker.RestoreLastStrokeCursor();}
                File.WriteAllText(Path.Combine(diagnostic,"profiles.json"),System.Text.Json.JsonSerializer.Serialize(measurements.Profiles));
                File.WriteAllText(Path.Combine(diagnostic,"result.json"),System.Text.Json.JsonSerializer.Serialize(new{measurements.Profiles,measurements.Rejected,measurements.Diagnostics}));
                foreach(var rejected in measurements.Rejected)
                    File.AppendAllText(LogPath,System.Text.Json.JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,action="brush_calibration_rejected",details=rejected})+Environment.NewLine);
                return measurements;
            });
            lastBrushProfiles=batch.Profiles.ToArray();
            if(batch.Profiles.Count>0)BrushFootprints.Save(settings,batch.Profiles);
            BrushSignalDiagnostics.Save(settings,batch.Diagnostics);
            var points=BrushFootprints.Read(settings).OrderBy(p=>p.Size).Select(p=>new double[]{p.Size,p.Reach*2+1,
                Math.Max(1,SpeedCalibration.Footprint(settings,p.Size).Inner*2+1)}).ToArray();
            if(batch.Profiles.Count>0)
            {
                settings.Set("brush_calibration_points", points);
                settings.Set("brush_calibration_context", AdaptiveBrush.Context(settings));
            }
            settings.Set("adaptive_brush",batch.Rejected.Count==0&&AdaptiveBrush.CalibrationCurrent(settings));
            adaptiveFailure=string.Join("\n",batch.Rejected.Select(f=>f.Reason=="Background is unstable."
                ?T($"Size {f.Size}: фон нестабільний; цей Size не збережено.",$"Size {f.Size}: background is unstable; this Size was not saved.")
                :f.Reason=="Dot does not match requested color direction."
                ?T($"Size {f.Size}, повтор {f.Repeat}/3: зміни пікселів не відповідають вибраному кольору. Можливий сторонній слід; цей Size не збережено.",
                    $"Size {f.Size}, repeat {f.Repeat}/3: pixel changes do not match the selected color direction. Possible capture artifact; this Size was not saved.")
                :f.Contrast is { } c
                ?T($"Size {f.Size}, повтор {f.Repeat}/3: контраст {c.PeakDelta}/255, потрібно {c.RequiredDelta}; змінених пікселів {c.ChangedPixels}. Цей Size не збережено.",
                    $"Size {f.Size}, repeat {f.Repeat}/3: contrast {c.PeakDelta}/255, required {c.RequiredDelta}; changed pixels {c.ChangedPixels}. This Size was not saved.")
                :T($"Size {f.Size}: три вимірювання неузгоджені; цей Size не збережено.",
                    $"Size {f.Size}: three measurements are inconsistent; this Size was not saved.")));
            if(batch.Rejected.Any(f=>f.Size==1))adaptiveFailure+="\n"+(settings.Int("brush_shape_slot",3)==3
                ?T("Для адаптивного режиму потрібен підтверджений Size 1. Спробуй суцільний квадратний пензель №4 і калібруй лише Size 1 на чистому полотні.",
                    "Adaptive mode requires a verified Size 1. Try solid square brush 4 and calibrate Size 1 alone on a clean Canvas.")
                :T("Ця форма не підтвердила Size 1. Переглянь знімки й вимірювання; інші збережені Size можна перевіряти окремо.",
                    "This shape did not verify Size 1. Review its snapshots and measurements; other saved Sizes can be tested separately."));
            var noCore=batch.Profiles.Where(p=>!p.SolidCore.Valid).Select(p=>p.Size).ToArray();
            if(noCore.Length>0)adaptiveFailure+="\n"+T($"Size {string.Join(", ",noCore)}: слід виміряно, але стабільного ядра немає. Непрозорі частини трьох крапок не збігаються; ці Size не вмикають адаптивне прискорення.",
                $"Size {string.Join(", ",noCore)}: trace measured, but no stable core. The opaque parts of the three dots do not overlap; these Sizes do not enable adaptive acceleration.");
            File.AppendAllText(LogPath,System.Text.Json.JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,action="brush_calibration_complete",details=new{
                version=BuildInfo.Version,shape=settings.Int("brush_shape_slot",3),savedSizes=batch.Profiles.Select(p=>p.Size),rejected=batch.Rejected,signals=batch.Diagnostics,
                noSolidCoreSizes=noCore,adaptiveReady=AdaptiveBrush.CalibrationCurrent(settings),diagnostics=diagnosticPath}})+Environment.NewLine);
            Dirty();
            SetStatus(noCore.Length>0
                ?T($"Виміряно {batch.Profiles.Count} Size; без стабільного ядра: {string.Join(", ",noCore)}. Переглянь три крапки в розділі «Пензель». Очисти полотно.",
                    $"Measured {batch.Profiles.Count} Sizes; no stable core: {string.Join(", ",noCore)}. Review the three dots on the Brush page. Clear Canvas.")
                :batch.Rejected.Count==0
                ?T("Три вимірювання збережено. Форми без стабільного ядра не використовуються для прискорення. Очисти полотно перед START.", "Three measurements saved. Shapes without a stable core are excluded from acceleration. Clear Canvas before START.")
                :T($"Калібрування часткове: збережено {batch.Profiles.Count} Size, відхилено {batch.Rejected.Count}. Кожен збережений Size має 3/3 вимірювання. Переглянь причини в розділі «Пензель»; очисти полотно.",
                    $"Partial calibration: {batch.Profiles.Count} Sizes saved, {batch.Rejected.Count} rejected. Each saved Size has 3/3 measurements. See reasons on the Brush page; clear Canvas."));
        }
        catch (OperationCanceledException)
        {
            adaptiveFailure = T("Калібрування скасовано. Очисти полотно й повтори.", "Calibration cancelled. Clear Canvas and retry.");
            SetStatus(adaptiveFailure);
            if(setupRunning)throw;
        }
        catch (Exception e)
        {
            adaptiveFailure = T("Не вдалося виміряти розмір", "Could not measure Size") + " " + currentSize + ": " + T(e.Message);
            if(e is BrushContrastException low)
                adaptiveFailure=T($"Size {currentSize}, повтор {currentRepeat}/3: контраст крапки {low.Metrics.PeakDelta}/255, потрібно {low.Metrics.RequiredDelta}/255; змінених пікселів {low.Metrics.ChangedPixels}. Знімки й вимірювання збережено. Очисти полотно й повтори. Профілі не оновлено.",
                    $"Size {currentSize}, repeat {currentRepeat}/3: dot contrast {low.Metrics.PeakDelta}/255, required {low.Metrics.RequiredDelta}/255; changed pixels {low.Metrics.ChangedPixels}. Snapshots and measurements saved. Clear Canvas and retry. Profiles were not updated.");
            throw new InvalidOperationException(adaptiveFailure, e);
        }
        finally
        {
            Native.Release(); Save(); if(!closing){BuildUi(); ShowPage("adaptive"); SetEditing(true); Show(); Activate();}
        }
    }
}
