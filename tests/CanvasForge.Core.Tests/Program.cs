using System.Text.Json.Nodes;
using CanvasForge.Core;

var passed = 0;
void Test(string name, Action action)
{
    action();
    Console.WriteLine("PASS " + name);
    passed++;
}

void Assert(bool condition, string message = "Assertion failed")
{
    if (!condition)
        throw new Exception(message);
}

Settings Config(ColorMode mode, int width = 16, int height = 12)
{
    var s = Settings.Defaults();
    s.Set("color_mode", mode == ColorMode.HexDirect ? "HEX Direct" : "Rust Palette");
    s.Set("cell_px", 1);
    s.Set("fit_mode", "fit whole");
    s.Set("skin_assist", false);
    s.Set("edge_preserve", false);
    s.Set("preblur", 0);
    s.Set("hex_max_colors", "128");
    var c = s.Calibration;
    c.SetRect("canvas", new(-5, 10, width - 5, height + 10));
    s.SetCalibration(c);
    return s;
}

PixelImage Fixture(int w = 16, int h = 12)
{
    var im = new PixelImage(w, h);
    for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
            im.Set(y * w + x, x < w / 3 ? new(243, 198, 183) : x < w * 2 / 3 ? new(125, 72, 57) : new(21, 26, 34));
    return im;
}

Test("HEX Direct uses source RGB independently of Rust palette", () =>
{
    var s = Config(ColorMode.HexDirect);
    s.SetPalette([new(new(0, 255, 0), new(0, 0), "main")]);
    var im = Fixture();
    var p = Planner.Build(im, s);
    Assert(p.Palette.All(e => e.Source == "hex" && e.ClickPoint is null));
    Assert(p.Palette.Select(e => e.Color).ToHashSet().SetEquals(new[] { new Rgb(243, 198, 183), new Rgb(125, 72, 57), new Rgb(21, 26, 34) }));
    Assert(p.ColorCount == 3);
    Assert(p.Error < .01);
});
Test("Rust Palette includes Quick Colors and never generates HEX", () =>
{
    var s = Config(ColorMode.RustPalette);
    var palette = new[]
    {
        new PaletteEntry(new(10, 10, 10), new(1, 2), "main"),
        new PaletteEntry(new(250, 250, 250), new(3, 4), "main"),
        new PaletteEntry(new(243, 198, 183), new(5, 6), "quick")
    };
    s.SetPalette(palette);
    var p = Planner.Build(Fixture(), s);
    Assert(p.Palette.SequenceEqual(palette));
    Assert(p.Indices.All(x => x >= 0 && x < palette.Length));
    Assert(p.Counts.ContainsKey(2));
});
Test("Preview and stroke groups reproduce the actual plan", () =>
{
    var s = Config(ColorMode.HexDirect);
    var p = Planner.Build(Fixture(), s);
    var rebuilt = Enumerable.Repeat(-1, p.Indices.Length).ToArray();
    foreach (var(color, strokes)in p.Strokes)
        foreach (var st in strokes)
        {
            if (st.Y1 == st.Y2)
                for (var x = st.X1; x <= st.X2; x++)
                    rebuilt[st.Y1 * p.Width + x] = color;
            else
                for (var y = st.Y1; y <= st.Y2; y++)
                    rebuilt[y * p.Width + st.X1] = color;
        }

    Assert(rebuilt.SequenceEqual(p.Indices));
    for (var i = 0; i < p.Indices.Length; i++)
        Assert(p.Preview.Color(i) == p.Palette[p.Indices[i]].Color);
});
Test("Transparency remains unpainted, including background optimization", () =>
{
    var im = Fixture();
    for (var i = 0; i < 20; i++)
        im.Rgba[i * 4 + 3] = 0;
    var s = Config(ColorMode.HexDirect);
    s.Set("background_fill", true);
    s.Set("background_mode", "auto");
    var p = Planner.Build(im, s);
    Assert(p.Indices.Take(20).All(x => x == -1));
    Assert(p.BackgroundColor is null);
    Assert(p.Preview.Rgba.Take(80).Where((_, i) => i % 4 == 3).All(x => x == 0));
});
Test("Fit square retains complete composition and transparent margins", () =>
{
    var s = Config(ColorMode.HexDirect, 24, 12);
    s.Set("fit_mode", "fit square");
    var p = Planner.Build(Fixture(), s);
    Assert(p.Indices.Where((_, i) => i % 24 < 6 || i % 24 >= 18).All(x => x == -1));
});
Test("Physical precision coverage has no gaps and no extra pixels", () =>
{
    var random = new Random(19);
    for (var n = 0; n < 60; n++)
    {
        var w = random.Next(2, 12);
        var h = random.Next(2, 12);
        var grid = Enumerable.Range(0, w * h).Select(_ => random.Next(-1, 4)).ToArray();
        var grouped = Planner.Group(grid, w, h, true);
        var r = new ScreenRect(-37, 11, random.Next(30, 90), random.Next(60, 120));
        var xb = Coverage.Partition(r.Left, r.Right, w);
        var yb = Coverage.Partition(r.Top, r.Bottom, h);
        foreach (var(color, strokes)in grouped)
        {
            var pixels = new HashSet<(int, int)>();
            foreach (var line in Coverage.Expand(strokes, r, w, h, 1))
            {
                if (line.Y1 == line.Y2)
                    for (var x = line.X1; x <= line.X2; x++)
                        pixels.Add((x, line.Y1));
                else
                    for (var y = line.Y1; y <= line.Y2; y++)
                        pixels.Add((line.X1, y));
            }

            var expected = new HashSet<(int, int)>();
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                    if (grid[y * w + x] == color)
                        for (var sy = yb[y]; sy < yb[y + 1]; sy++)
                            for (var sx = xb[x]; sx < xb[x + 1]; sx++)
                                expected.Add((sx, sy));
            Assert(pixels.SetEquals(expected));
        }
    }
});
Test("Collinear merging preserves exact raster pixels", () =>
{
    var lines = new[]
    {
        new ScreenLine(0, 5, 9, 5),
        new ScreenLine(8, 5, 15, 5),
        new ScreenLine(16, 5, 20, 5),
        new ScreenLine(2, 7, 2, 12)
    };
    var merged = Coverage.Merge(lines);
    Assert(merged.Count == 2);
    Assert(merged.Contains(new(0, 5, 20, 5)));
});
Test("Source, coordinates, mode and click points invalidate RESUME identity", () =>
{
    var im = Fixture();
    var s = Config(ColorMode.HexDirect);
    var entries = new[]
    {
        new PaletteEntry(new(1, 2, 3), new(4, 5), "main")
    };
    var hash = PlanIdentity.Compute(im, s, entries);
    var changed = im.Clone();
    changed.Rgba[0]++;
    Assert(hash != PlanIdentity.Compute(changed, s, entries));
    var c = s.Clone();
    c.Set("color_mode", "Rust Palette");
    Assert(hash != PlanIdentity.Compute(im, c, entries));
    Assert(hash != PlanIdentity.Compute(im, s, [entries[0] with { ClickPoint = new(7, 8) }]));
});
Test("Python JSON imports coordinates and preserves unknown settings", () =>
{
    var path = Path.Combine(Path.GetTempPath(), "canvasforge-test-" + Guid.NewGuid() + ".json");
    try
    {
        File.WriteAllText(path, "{\"color_mode\":\"Palette Edition\",\"cell_px\":3,\"calibration\":{\"canvas_left\":-100,\"canvas_top\":10,\"canvas_right\":100,\"canvas_bottom\":110,\"hex_verified\":1},\"rust_palette\":[[10,20,30]],\"palette_click_points\":[[4,5]],\"custom_future_setting\":{\"value\":42}} ");
        var s = Settings.Load(path);
        Assert(s.Mode == ColorMode.RustPalette);
        Assert(s.Calibration.Rect("canvas").Left == -100);
        Assert(s.Palette()[0].Color == new Rgb(10, 20, 30));
        var clone = s.Clone();
        clone.Set("cell_px", 7);
        Assert(s.Int("cell_px") == 3);
        s.Save(path);
        Assert(Settings.Load(path).Data["custom_future_setting"]!["value"]!.GetValue<int>() == 42);
    }
    finally
    {
        File.Delete(path);
    }
});
Test("Size curve retains the RC8.4 low-range calibration", () =>
{
    Assert(Math.Abs(ControlCurve.Fraction("size", 2) - .0031) < 1e-8);
    Assert(Math.Abs(ControlCurve.Fraction("size", 3) - .0062) < 1e-8);
    Assert(ControlCurve.Fraction("interval", .01) == 0);
    Assert(ControlCurve.Fraction("opacity", 1) == 1);
});
Test("Slider maximum correctly distinguishes Opacity 1 from Size 1", () =>
{
    Assert(ControlCurve.IsMaximum("opacity", 1));
    Assert(ControlCurve.IsMaximum("interval", 1));
    Assert(ControlCurve.IsMaximum("size", 100));
    Assert(!ControlCurve.IsMaximum("size", 1));
    Assert(!ControlCurve.IsMaximum("interval", .01));
});
Test("Explicit HEX color caps are respected with dithering", () =>
{
    var s = Config(ColorMode.HexDirect, 24, 18);
    s.Set("hex_max_colors", "8");
    s.Set("dither", true);
    var im = new PixelImage(24, 18);
    for (var y = 0; y < 18; y++)
        for (var x = 0; x < 24; x++)
            im.Set(y * 24 + x, new((byte)(x * 10), (byte)(y * 14), (byte)((x + y) * 6)));
    var p = Planner.Build(im, s);
    Assert(p.Palette.Length <= 8);
    Assert(p.Indices.All(x => x >= 0 && x < p.Palette.Length));
});
Test("HEX Direct can actually paint more than 64 colors", () =>
{
    var s = Config(ColorMode.HexDirect, 32, 24);
    s.Set("hex_max_colors", "128");
    var im = new PixelImage(32, 24);
    for (var y = 0; y < 24; y++)
        for (var x = 0; x < 32; x++)
            im.Set(y * 32 + x, new((byte)(x * 8), (byte)(y * 10), (byte)((x * 7 + y * 13) % 256)));
    var p = Planner.Build(im, s);
    Assert(p.ColorCount > 64);
    Assert(p.Palette.Length <= 128);
    Assert(p.Strokes.Count > 64);
});
Test("Opaque background fill and foreground reproduce every cell", () =>
{
    var s = Config(ColorMode.HexDirect);
    s.Set("background_fill", true);
    s.Set("background_mode", "auto");
    var p = Planner.Build(Fixture(), s);
    Assert(p.BackgroundColor is not null);
    var grid = Enumerable.Repeat(p.BackgroundColor!.Value, p.Indices.Length).ToArray();
    foreach (var(color, strokes)in p.Strokes)
        foreach (var st in strokes)
            if (st.Y1 == st.Y2)
                for (var x = st.X1; x <= st.X2; x++)
                    grid[st.Y1 * p.Width + x] = color;
            else
                for (var y = st.Y1; y <= st.Y2; y++)
                    grid[y * p.Width + st.X1] = color;
    Assert(grid.SequenceEqual(p.Indices));
});
Test("Cancellation interrupts planning", () =>
{
    var s = Config(ColorMode.HexDirect);
    using var cancel = new CancellationTokenSource();
    cancel.Cancel();
    try
    {
        Planner.Build(Fixture(), s, null, cancel.Token);
        throw new Exception("Did not cancel");
    }
    catch (OperationCanceledException)
    {
    }
});
Test("Small-region cleanup does not fill transparent cells", () =>
{
    int[] grid = [0, 0, -1, 0, 1, -1, 0, 0, -1];
    Planner.Cleanup(grid, 3, 3, 1, 2, CancellationToken.None);
    Assert(grid[2] == -1 && grid[5] == -1 && grid[8] == -1);
    Assert(grid[4] == 0);
});

Test("Stress: 120 random plans preserve indices, alpha, coordinates and finite ETA", () =>
{
    var rng = new Random(104);
    for (var trial = 0; trial < 120; trial++)
    {
        var width = rng.Next(2, 40); var height = rng.Next(2, 40);
        var image = new PixelImage(width, height);
        for (var i = 0; i < width * height; i++)
            image.Set(i, Rgb.FromKey(rng.Next(0x1000000)), (byte)(rng.Next(5) == 0 ? 0 : 255));
        var mode = trial % 2 == 0 ? ColorMode.HexDirect : ColorMode.RustPalette;
        var cfg = Config(mode, width, height);
        cfg.Set("hex_max_colors", (1 + trial % 16).ToString());
        cfg.Set("dither", trial % 3 == 0);
        cfg.Set("fit_mode", new[] { "fit whole", "fit square", "crop", "smart" }[trial % 4]);
        cfg.Set("smooth_passes", trial % 2);
        cfg.Set("min_region", 1 + trial % 4);
        cfg.Set("preblur", trial % 3 == 0 ? .5 : 0);
        cfg.Set("speed_profile", "Safe");
        cfg.SetPalette(Enumerable.Range(0, 8).Select(i => new PaletteEntry(Rgb.FromKey(rng.Next(0x1000000)), new ScreenPoint(i, i), "main")));
        var plan = Planner.Build(image, cfg);
        Assert(plan.Indices.All(i => i == -1 || i >= 0 && i < plan.Palette.Length));
        Assert(double.IsFinite(plan.Error));
        var eta = Coverage.EstimateSeconds(plan, cfg);
        Assert(double.IsFinite(eta) && eta >= cfg.Int("start_delay"));
        var rect = cfg.Calibration.Rect("canvas");
        foreach (var line in Coverage.Build(plan, cfg).Values.SelectMany(x => x))
            Assert(line.X1 >= rect.Left && line.X2 < rect.Right && line.Y1 >= rect.Top && line.Y2 < rect.Bottom);
        for (var i = 0; i < plan.Indices.Length; i++)
            if (plan.Indices[i] < 0) Assert(plan.Preview.Alpha(i) == 0);
    }
});
Test("Stress: 50 repeated settings saves reload exact palette and identity", () =>
{
    var dir = Path.Combine(Path.GetTempPath(), "canvasforge-stress-" + Guid.NewGuid());
    Directory.CreateDirectory(dir);
    try
    {
        var path = Path.Combine(dir, "config.json");
        for (var i = 0; i < 50; i++)
        {
            var cfg = Config(ColorMode.RustPalette);
            cfg.SetPalette([new(new(12, 34, 56), new(i, -i), "quick")]);
            cfg.Save(path);
            var read = Settings.Load(path);
            Assert(read.Palette().SequenceEqual(cfg.Palette()));
            Assert(read.Data.ToJsonString() == cfg.Data.ToJsonString());
            Assert(!File.Exists(path + ".tmp"));
        }
        foreach (var invalid in new[] { "", "{", "[]", "null", "{\"cell_px\":0}" })
        {
            File.WriteAllText(path, invalid);
            var rejected = false;
            try { Settings.Load(path); } catch (System.Text.Json.JsonException) { rejected = true; }
            catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "Invalid config accepted");
        }
    }
    finally { Directory.Delete(dir, true); }
});
Test("Cancellation during 1024-pixel planning completes within 5 seconds", () =>
{
    var cfg = Config(ColorMode.HexDirect, 1024, 1024);
    cfg.Set("hex_max_colors", "256"); cfg.Set("median_cleanup", true);
    using var cancel = new CancellationTokenSource(50);
    var watch = System.Diagnostics.Stopwatch.StartNew();
    var cancelled = false;
    try { Planner.Build(Fixture(256, 256), cfg, null, cancel.Token); }
    catch (OperationCanceledException) { cancelled = true; }
    Assert(cancelled && watch.Elapsed.TotalSeconds < 5, "Cancellation stalled");
});
Test("Huge image and 8-million-cell planning limit reject before allocation", () =>
{
    var rejected = false;
    try { _ = new PixelImage(100_000, 100_000); } catch (ArgumentOutOfRangeException) { rejected = true; }
    Assert(rejected);
    rejected = false;
    try { Planner.Build(Fixture(), Config(ColorMode.HexDirect, 16384, 16384)); }
    catch (InvalidOperationException) { rejected = true; }
    Assert(rejected);
});

Test("Overflowing palette grids reject without entering huge loops", () =>
{
    var cal = Config(ColorMode.HexDirect).Calibration;
    cal.SetRect("palette", new(0, 0, 100, 100));
    Assert(cal.GridCenters("palette", 65536, 65536).Count == 0);
    Assert(cal.GridCenters("palette", int.MaxValue, 2).Count == 0);
});
Test("Nonfinite timing, enormous delays and malformed HEX caps reject cleanly", () =>
{
    foreach (var (key, value) in new[] { ("input_frame_delay_ms", "NaN"), ("cycle_delay_ms", "Infinity"), ("stroke_speed", "-Infinity"), ("preblur", "1e300"), ("start_delay", "1e300"), ("hex_max_colors", "oops"), ("hex_max_colors", "0") })
    {
        var cfg = Config(ColorMode.HexDirect); cfg.Set(key, value);
        var rejected = false;
        try { cfg.Validate(); } catch (InvalidDataException) { rejected = true; }
        Assert(rejected, key + " accepted");
    }
});

Test("Fill Canvas crops an opaque border background without removing it", () =>
{
    var im = new PixelImage(100, 80);
    for (var y = 0; y < im.Height; y++)
        for (var x = 0; x < im.Width; x++)
            im.Set(y * im.Width + x, Rgb.White);
    for (var y = 20; y < 60; y++)
        for (var x = 30; x < 70; x++)
            im.Set(y * im.Width + x, new Rgb(10, 10, 10));

    var cropped = ImageProcessing.CropOpaqueSubject(im, CancellationToken.None);
    Assert(cropped.Width < im.Width && cropped.Height < im.Height, "Opaque background was not cropped");
    Assert(cropped.Width >= 40 && cropped.Height >= 40, "Subject was clipped");
    Assert(cropped.Color((cropped.Height / 2) * cropped.Width + cropped.Width / 2) == new Rgb(10, 10, 10), "Subject center changed");
    Assert(cropped.Alpha(0) == 255, "Crop unexpectedly removed the preserved background");
});

Test("Tiny, thin and fully transparent images survive background and crop processing", () =>
{
    foreach (var (w, h) in new[] { (1, 1), (1, 31), (31, 1), (2, 2), (4, 4), (8, 16) })
        foreach (var transparent in new[] { false, true })
        {
            var im = Fixture(w, h);
            if (transparent) for (var i = 0; i < w * h; i++) im.Rgba[i * 4 + 3] = 0;
            var cfg = Config(ColorMode.HexDirect, 24, 24);
            cfg.Set("remove_bg", true); cfg.Set("fill_subject", true);
            var plan = Planner.Build(im, cfg);
            Assert(plan.Indices.Length == plan.Width * plan.Height);
            if (transparent) Assert(plan.Indices.All(i => i == -1));
        }
});
Test("Large noisy image completes quantization, dithering and all speed estimates", () =>
{
    var rng = new Random(105);
    var im = new PixelImage(384, 128);
    for (var i = 0; i < im.Width * im.Height; i++) im.Set(i, Rgb.FromKey(rng.Next(0x1000000)));
    var cfg = Config(ColorMode.HexDirect, 384, 128);
    cfg.Set("hex_max_colors", "32"); cfg.Set("dither", true);
    var plan = Planner.Build(im, cfg);
    Assert(plan.ColorCount <= 32 && plan.Indices.All(i => i >= 0));
    foreach (var speed in SpeedProfile.All)
        Assert(double.IsFinite(Coverage.EstimateSeconds(plan, cfg, speed.Name)));
});

Test("HEX readback skips valid but rotated values and accepts later exact match", () =>
{
    var read = new HexReadback(new(255, 51, 51));
    Assert(!read.Observe("#F3333F"));
    Assert(!read.Observe("__CanvasForgeReadback__"));
    Assert(read.Observe("  #ff3333  "));
    Assert(read.LastValid == "FF3333");
});
Test("HEX readback rejects null, partial, duplicate prefix and non-HEX text", () =>
{
    foreach (var invalid in new string?[] { null, "", "F3333", "##FF3333", "#FF33333", "GG3333", "12 AC5F" })
        Assert(HexReadback.Normalize(invalid) is null);
    foreach (var target in new[] { new Rgb(0,0,0), new Rgb(255,255,255), new Rgb(18,172,95), new Rgb(3,151,226) })
    {
        var read = new HexReadback(target);
        Assert(read.Observe(target.Hex));
        Assert(read.Observe("#" + target.Hex.ToLowerInvariant()));
    }
});
Test("HEX source retains RGB channels and leading zeroes for every test color", () =>
{
    foreach (var rgb in new[] { new Rgb(255,51,51), new Rgb(18,172,95), new Rgb(3,151,226), new Rgb(0,0,0), new Rgb(255,255,255) })
    {
        var im = new PixelImage(4,4);
        for (var i = 0; i < 16; i++) im.Set(i, rgb);
        var cfg = Config(ColorMode.HexDirect, 4, 4);
        var plan = Planner.Build(im, cfg);
        Assert(plan.Palette.Length == 1 && plan.Palette[0].Color == rgb);
        Assert(plan.Palette[0].Color.Hex.Length == 6);
    }
});

Test("HEX retries honor zero through five rather than always using six", () =>
{
    for (var i = 0; i <= 5; i++) Assert(HexReadback.AttemptCount(i) == i + 1);
    Assert(HexReadback.AttemptCount(-1) == 1 && HexReadback.AttemptCount(999) == 6);
});
Test("HEX diagnostics distinguish copy marker, no text, malformed and wrong color", () =>
{
    var read = new HexReadback(new(255,51,51));
    Assert(read.Status(HexReadback.Marker) == "copy_failed");
    Assert(read.Status(null) == "no_text");
    Assert(read.Status("F3333") == "invalid_text");
    Assert(read.Status("F3333F") == "mismatch");
    Assert(read.Status("#FF3333") == "match");
});
Test("Clipboard contention retries and permanent failure prevents subsequent paste", () =>
{
    var calls = 0; var waits = 0;
    var value = ClipboardRetry.Run(() => { if (++calls < 3) throw new System.ComponentModel.Win32Exception(); return "FF3333"; }, () => waits++);
    Assert(value == "FF3333" && calls == 3 && waits == 2);
    var pasted = false; calls = 0;
    try
    {
        ClipboardRetry.Run<bool>(() => { calls++; throw new System.ComponentModel.Win32Exception(); }, () => { });
        pasted = true;
    }
    catch (System.ComponentModel.Win32Exception) { }
    Assert(!pasted && calls == 3);
});

Test("HEX controls remain separate and missing HEX calibration never uses palette coordinates", () =>
{
    var cfg = Config(ColorMode.HexDirect);
    var original = cfg.Calibration;
    original.SetRect("size_track", new(10,20,100,30));
    cfg.SetCalibration(original);
    Assert(!cfg.HexControlsReady);
    var rejected = false;
    try { cfg.PaintCalibration(); } catch (InvalidOperationException) { rejected = true; }
    Assert(rejected);
    var hex = new Calibration(new JsonObject());
    foreach (var key in new[] { "size_track", "interval_track", "opacity_track", "brush_shapes" })
        hex.SetRect(key, new(10,120,100,130));
    hex.SetPoint("size_min", new(10,125));
    cfg.Data["hex_controls"] = hex.Data.DeepClone();
    Assert(cfg.HexControlsReady);
    Assert(cfg.PaintCalibration().Rect("size_track").Top == 120);
    Assert(cfg.PaintCalibration().Point("size_min") == new ScreenPoint(10,125));
    Assert(cfg.Calibration.Rect("size_track").Top == 20);
    Assert(cfg.PaintCalibration().Rect("canvas") == original.Rect("canvas"));
    cfg.Set("color_mode", "Rust Palette");
    Assert(cfg.PaintCalibration().Rect("size_track").Top == 20);
});
Test("English translations cover capture, progress and dynamic errors", () =>
{
    var examples = new Dictionary<string, string>
    {
        ["Автоматизація"] = "Automation",
        ["Клікни потрібну точку. ESC — скасувати."] = "Click the required point. ESC — cancel.",
        ["Готово"] = "Done",
        ["Підбір кольорів…"] = "Matching colors…",
        ["Старт через 5 с…"] = "Starting in 5 s…",
        ["Rust не підтвердив HEX FF3333. Малювання зупинено."] = "Rust did not confirm HEX FF3333. Painting stopped.",
        ["Не знайдено повзунок size. Повтори захоплення його зеленої смуги."] = "Could not find the size slider. Capture its green track again.",
        ["Не підтверджено opacity. Перевір калібрування."] = "opacity was not confirmed. Check calibration."
    };
    foreach (var (uk, en) in examples) Assert(CanvasForge.App.Translations.Get(uk) == en, uk);
    Assert(CanvasForge.App.Translations.Get("My image — малюнок.png") == "My image — малюнок.png");
    Assert(CanvasForge.App.Translations.Get("English") == "English");
    using var stream = typeof(CanvasForge.App.Translations).Assembly.GetManifestResourceStream("CanvasForge.Translations")!;
    var map = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string>>(stream)!;
    foreach (var value in map.Values)
        Assert(!System.Text.RegularExpressions.Regex.IsMatch(value, "[А-Яа-яІіЇїЄєҐґ]"), "Untranslated dictionary value: " + value);
});
Test("Input delay defaults, bounds, and ETA agree", () =>
{
    var cfg = Settings.Defaults();
    Assert(StrokeTiming.Frame(cfg) == .020);
    var speed = SpeedProfile.All.Last();
    var oldTime = StrokeTiming.Estimate(cfg, speed, 100, false);
    cfg.Set("input_frame_delay_ms", 16);
    cfg.Validate();
    Assert(Math.Abs(oldTime - StrokeTiming.Estimate(cfg, speed, 100, false) - .012) < 1e-9);
    foreach (var invalid in new[] { "0", "15", "101", "NaN" })
    {
        cfg.Set("input_frame_delay_ms", invalid);
        try { cfg.Validate(); throw new Exception("Invalid delay accepted"); }
        catch (InvalidDataException) { }
    }
});
Test("Experimental timing uses 1 ms while Stable preserves legacy timings", () =>
{
    var cfg = Settings.Defaults();
    foreach (var speed in SpeedProfile.All)
    {
        Assert(!StrokeTiming.Experimental(cfg));
        Assert(StrokeTiming.Settle(cfg, speed) == Math.Max(.005, speed.StartDelay));
        Assert(StrokeTiming.EndHold(cfg, speed) == Math.Max(.020, speed.UpDelay));
    }
    cfg.Set("input_engine", "Experimental 1 ms");
    cfg.Validate();
    foreach (var speed in SpeedProfile.All)
    {
        Assert(StrokeTiming.Frame(cfg) == .001);
        Assert(StrokeTiming.Settle(cfg, speed) == .001);
        Assert(StrokeTiming.EndHold(cfg, speed) == .001);
        Assert(StrokeTiming.Release(cfg) == .001);
        Assert(Math.Abs(StrokeTiming.Estimate(cfg, speed, 0, false) - .004) < 1e-9);
        var travel = Math.Ceiling(100.0 / speed.Pitch) * speed.PointDelay;
        Assert(Math.Abs(StrokeTiming.Estimate(cfg, speed, 100, false) - (.004 + travel)) < 1e-9);
        Assert(Math.Abs(StrokeTiming.Estimate(cfg, speed, 100, true) - (.004 + cfg.Number("stroke_speed", .028))) < 1e-9);
    }
    cfg.Set("cycle_delay_ms", 30);
    Assert(StrokeTiming.Release(cfg) == .030);
    cfg.Set("input_engine", "Stable");
    Assert(StrokeTiming.Frame(cfg) == .020);
    cfg.Set("input_engine", "unknown");
    try { cfg.Validate(); throw new Exception("Unknown engine accepted"); }
    catch (InvalidDataException) { }
});
Test("Speed patch control estimates follow Stable and Experimental paths", () =>
{
    var cfg = Settings.Defaults();
    Assert(Math.Abs(StrokeTiming.ClickEstimate(cfg) - .100) < 1e-9);
    Assert(Math.Abs(StrokeTiming.SliderChangeEstimate(cfg) - .363) < 1e-9);
    Assert(Math.Abs(StrokeTiming.ColorDelay(cfg) - .100) < 1e-9);
    Assert(Math.Abs(StrokeTiming.HexChangeEstimate(cfg) - 1.8) < 1e-9);
    cfg.Set("input_engine", "Experimental 1 ms");
    Assert(Math.Abs(StrokeTiming.ClickEstimate(cfg) - .018) < 1e-9);
    Assert(Math.Abs(StrokeTiming.SliderChangeEstimate(cfg) - .069) < 1e-9);
    Assert(Math.Abs(StrokeTiming.ColorDelay(cfg) - .030) < 1e-9);
    Assert(Math.Abs(StrokeTiming.HexChangeEstimate(cfg) - .65) < 1e-9);
    cfg.Set("hex_readback_every", 0);
    try { cfg.Validate(); throw new Exception("Invalid HEX readback interval accepted"); }
    catch (InvalidDataException) { }
});
Test("Experimental timing keeps planned pixels and changes the ETA", () =>
{
    var cfg = Config(ColorMode.HexDirect);
    var plan = Planner.Build(Fixture(), cfg);
    var stable = Coverage.Build(plan, cfg);
    var eta = Coverage.EstimateSeconds(plan, cfg);
    cfg.Set("input_engine", "Experimental 1 ms");
    var experimental = Coverage.Build(plan, cfg);
    Assert(stable.Count == experimental.Count);
    foreach (var (key, lines) in stable) Assert(lines.SequenceEqual(experimental[key]));
    Assert(Coverage.EstimateSeconds(plan, cfg) < eta);
});
Test("Adaptive strokes stay within colors and preserve every baseline center", () =>
{
    const int w = 384, h = 320;
    var cfg = Settings.Defaults();
    cfg.Set("color_mode", "Rust Palette"); cfg.Set("speed_profile", "Safe");
    cfg.Set("coverage_mode", "Precision"); cfg.Set("force_precision_controls", true);
    var cal = cfg.Calibration; cal.SetRect("canvas", new(-30, 20, w - 30, h + 20)); cfg.SetCalibration(cal);
    cfg.Set("brush_calibration_points", new double[][] { [1, 3, 1], [3, 5, 3], [10, 21, 13], [20, 35, 23] });
    cfg.Set("brush_calibration_context", AdaptiveBrush.Context(cfg));
    var indices = Enumerable.Repeat(0, w * h).ToArray();
    for (int y = 40; y < 80; y++) for (int x = 60; x < 90; x++) indices[y * w + x] = -1;
    for (int y = 10; y < 130; y++) indices[y * w + 135] = 1;
    var plan = new PaintPlan { Width = w, Height = h, Mode = ColorMode.RustPalette, Indices = indices,
        Palette = [], Preview = new PixelImage(w,h), Strokes = Planner.Group(indices,w,h,true),
        Counts = indices.Where(x => x >= 0).GroupBy(x=>x).ToDictionary(x=>x.Key,x=>x.Count()), Identity = "adaptive-test" };
    var baseline = Coverage.Build(plan,cfg);
    cfg.Set("adaptive_brush", true);
    var adaptive = AdaptiveBrush.Build(plan,cfg);
    Assert(adaptive.Values.SelectMany(x=>x).Any(x=>x.Size > 0), "No wide strokes");
    Assert(adaptive.Values.Sum(x=>x.Count) < baseline.Values.Sum(x=>x.Count), "No reduction");
    Console.WriteLine($"Adaptive fixture actions: {baseline.Values.Sum(x=>x.Count)} -> {adaptive.Values.Sum(x=>x.Count)}");
    foreach (var (color, ops) in adaptive)
    {
        var filled = new HashSet<(int,int)>();
        foreach (var op in ops)
        {
            var l=op.Line; int n=Math.Max(Math.Abs(l.X2-l.X1),Math.Abs(l.Y2-l.Y1));
            int dx=Math.Sign(l.X2-l.X1),dy=Math.Sign(l.Y2-l.Y1);
            for(int k=0;k<=n;k++)
            {
                int x=l.X1+k*dx+30,y=l.Y1+k*dy-20;
                for(int yy=y-op.OuterRadius;yy<=y+op.OuterRadius;yy++)
                    for(int xx=x-op.OuterRadius;xx<=x+op.OuterRadius;xx++)
                        Assert(xx>=0 && yy>=0 && xx<w && yy<h && indices[yy*w+xx]==color,"Wide brush crosses a boundary");
                for(int yy=y-op.FillRadius;yy<=y+op.FillRadius;yy++)
                    for(int xx=x-op.FillRadius;xx<=x+op.FillRadius;xx++) filled.Add((xx,yy));
            }
        }
        foreach(var l in baseline[color])
        {
            int n=Math.Max(Math.Abs(l.X2-l.X1),Math.Abs(l.Y2-l.Y1));
            for(int k=0;k<=n;k++) Assert(filled.Contains((l.X1+k*Math.Sign(l.X2-l.X1)+30,l.Y1+k*Math.Sign(l.Y2-l.Y1)-20)),"Baseline coverage lost");
        }
    }
    var second=AdaptiveBrush.Build(plan,cfg);
    foreach(var key in adaptive.Keys) Assert(adaptive[key].SequenceEqual(second[key]),"Resume order changed");
    cfg.Set("adaptive_brush",false);
    var legacy=AdaptiveBrush.Build(plan,cfg);
    foreach(var key in baseline.Keys) Assert(legacy[key].Select(x=>x.Line).SequenceEqual(baseline[key]));
});
Test("Adaptive brush rejects missing or stale calibration and translucent painting", () =>
{
    var cfg=Settings.Defaults(); cfg.Set("adaptive_brush",true);
    void Reject() { try { AdaptiveBrush.Validate(cfg); throw new Exception("Invalid adaptive settings accepted"); } catch(InvalidOperationException) {} }
    Reject();
    cfg.Set("paint_opacity_value",.5); Reject();
    cfg.Set("paint_opacity_value",1); cfg.Set("coverage_mode","Fast"); Reject();
    cfg.Set("coverage_mode","Precision"); cfg.Set("force_precision_controls",true);
    cfg.Set("color_mode","Rust Palette");
    var cal=cfg.Calibration; cal.SetRect("canvas",new(10,10,510,510)); cfg.SetCalibration(cal);
    cfg.Set("brush_calibration_points",new double[][] { [1,3,1], [10,21,13] });
    cfg.Set("brush_calibration_context",AdaptiveBrush.Context(cfg)); AdaptiveBrush.Validate(cfg);
    cal.SetRect("canvas",new(10,10,520,510)); cfg.SetCalibration(cal); Reject();
});
Test("CoordinateRebase is identity when the window has not moved", () =>
{
    var data = new JsonObject { ["canvas_left"] = 100, ["canvas_top"] = 200, ["canvas_right"] = 300, ["canvas_bottom"] = 400, ["session_client_x"] = 50, ["session_client_y"] = 60 };
    var rebase = new CoordinateRebase(50, 60, 96, 50, 60, 96);
    Assert(rebase.IsIdentity, "expected identity");
    Assert(rebase.ApplyTo(data) == 0, "identity must not touch anything");
    Assert((int)data["canvas_left"]! == 100 && (int)data["session_client_x"]! == 50);
});
Test("CoordinateRebase translates flat keys and palette pairs, skipping session keys", () =>
{
    var data = new JsonObject { ["canvas_left"] = 100, ["canvas_top"] = 200, ["size_min_x"] = 500, ["size_min_y"] = 600, ["session_client_x"] = 10, ["session_client_y"] = 20, ["session_dpi"] = 96 };
    var pairs = new JsonArray { new JsonArray { 100, 200 }, new JsonArray { 300, 400 } };
    var rebase = new CoordinateRebase(10, 20, 96, 110, 220, 96); // +100 x, +200 y
    Assert(!rebase.IsIdentity);
    Assert(rebase.ApplyTo(data) == 4, "only the four coordinate keys change");
    Assert((int)data["canvas_left"]! == 200, "left shifted by +100");
    Assert((int)data["canvas_top"]! == 400, "top shifted by +200");
    Assert((int)data["size_min_x"]! == 600 && (int)data["size_min_y"]! == 800);
    Assert((int)data["session_client_x"]! == 10 && (int)data["session_client_y"]! == 20, "baseline preserved");
    Assert(rebase.ApplyTo(pairs) == 2);
    Assert((int)pairs[0]![0]! == 200 && (int)pairs[0]![1]! == 400);
    Assert((int)pairs[1]![0]! == 400 && (int)pairs[1]![1]! == 600);
});
Test("CoordinateRebase scales around the client origin on DPI change", () =>
{
    var data = new JsonObject { ["canvas_left"] = 200, ["canvas_top"] = 300 };
    // baseline origin (100,100) at 96 DPI, now (100,100) at 192 DPI => 2x scale around origin
    var rebase = new CoordinateRebase(100, 100, 96, 100, 100, 192);
    Assert(Math.Abs(rebase.Scale - 2) < 1e-9);
    rebase.ApplyTo(data);
    Assert((int)data["canvas_left"]! == 300, "100 + (200-100)*2");
    Assert((int)data["canvas_top"]! == 500, "100 + (300-100)*2");
});
Test("Calibration stores and reads the Rust session baseline", () =>
{
    var cal = new Calibration(new JsonObject());
    Assert(cal.SessionClient is null, "no baseline before capture");
    cal.SetSession(new ScreenPoint(123, 456), 144);
    Assert(cal.SessionClient == new ScreenPoint(123, 456));
    Assert(cal.SessionDpi == 144);
    cal.SetSession(new ScreenPoint(0, 0), 0);
    Assert(cal.SessionClient == new ScreenPoint(0, 0), "zero is a valid client origin");
    Assert(cal.SessionDpi == 96, "invalid dpi normalizes to 96");
});

Settings SessionFixture()
{
    var s = Config(ColorMode.RustPalette);
    var c = s.Calibration;
    c.SetRect("canvas", new(100, 200, 300, 350));
    c.SetRect("palette", new(400, 200, 440, 360));
    c.SetRect("size_track", new(400, 500, 620, 510));
    c.SetPoint("size_min", new(400, 505));
    c.SetPoint("size_anchor_3", new(415, 505));
    c.SetPoint("hex_input", new(460, 370));
    c.SetSession(new(10, 20), 96, new(1280, 720));
    s.SetCalibration(c);
    var hex = new Calibration(new JsonObject());
    foreach (var key in new[] { "size_track", "interval_track", "opacity_track", "brush_shapes" })
        hex.SetRect(key, new(700, 500, 900, 510));
    hex.SetPoint("size_anchor_3", new(715, 505));
    s.Data["hex_controls"] = hex.Data.DeepClone();
    s.SetPalette([new(new(243, 198, 183), new(410, 210), "main"), new(new(21, 26, 34), new(430, 330), "quick")]);
    s.Set("brush_calibration_points", new double[][] { [1, 3, 1], [10, 21, 13] });
    s.Set("brush_calibration_context", AdaptiveBrush.Context(s));
    return s;
}

Test("Missing or malformed baseline coordinates are never treated as an origin", () =>
{
    var c = new Calibration(new JsonObject { ["session_client_x"] = 0 });
    Assert(c.SessionClient is null);
    c.Data["session_client_y"] = "broken";
    Assert(c.SessionClient is null);
    c.Data["session_client_y"] = -20;
    Assert(c.SessionClient == new ScreenPoint(0, -20));
});
Test("Session alignment translates every coordinate family and only once", () =>
{
    var s = SessionFixture();
    var samples = s.Data["brush_calibration_points"]!.ToJsonString();
    var colors = s.Data["rust_palette"]!.ToJsonString();
    var moved = CalibrationSession.Align(s, new(-10, -20), 96, new(1280, 720));
    Assert(!moved.IsIdentity);
    Assert(s.Calibration.Rect("canvas") == new ScreenRect(80, 160, 280, 310));
    Assert(s.Calibration.Rect("palette") == new ScreenRect(380, 160, 420, 320));
    Assert(s.Calibration.Point("size_min") == new ScreenPoint(380, 465));
    Assert(s.Calibration.Point("size_anchor_3") == new ScreenPoint(395, 465));
    Assert(s.Calibration.Point("hex_input") == new ScreenPoint(440, 330));
    var hex = new Calibration((JsonObject)s.Data["hex_controls"]!);
    Assert(hex.Rect("size_track") == new ScreenRect(680, 460, 880, 470));
    Assert(hex.Point("size_anchor_3") == new ScreenPoint(695, 465));
    Assert(s.Palette()[0].ClickPoint == new ScreenPoint(390, 170));
    Assert(s.Palette()[1].ClickPoint == new ScreenPoint(410, 290));
    Assert(s.Calibration.SessionClient == new ScreenPoint(-10, -20));
    Assert(s.Calibration.SessionSize == new ScreenSize(1280, 720));
    Assert(s.Data["brush_calibration_points"]!.ToJsonString() == samples);
    Assert(s.Data["rust_palette"]!.ToJsonString() == colors);
    var once = s.Data.ToJsonString();
    Assert(CalibrationSession.Align(s, new(-10, -20), 96, new(1280, 720)).IsIdentity);
    Assert(s.Data.ToJsonString() == once, "same frame translated twice");
});
Test("Zero-origin sessions still translate after Rust moves", () =>
{
    var s = SessionFixture();
    var c = s.Calibration; c.SetSession(new(0, 0), 96); s.SetCalibration(c);
    CalibrationSession.Align(s, new(50, -30), 96);
    Assert(s.Calibration.Rect("canvas") == new ScreenRect(150, 170, 350, 320));
});
Test("Partial recapture preserves alignment of the other captured regions", () =>
{
    var s = SessionFixture();
    CalibrationSession.Align(s, new(110, 70), 96, new(1280, 720));
    var c = s.Calibration;
    c.SetRect("canvas", new(205, 255, 405, 405));
    s.SetCalibration(c);
    Assert(s.Calibration.Rect("palette") == new ScreenRect(500, 250, 540, 410));
    Assert(s.Palette()[0].ClickPoint == new ScreenPoint(510, 260));
    CalibrationSession.Align(s, new(120, 80), 96, new(1280, 720));
    Assert(s.Calibration.Rect("canvas") == new ScreenRect(215, 265, 415, 415));
    Assert(s.Palette()[0].ClickPoint == new ScreenPoint(520, 270));
});
Test("DPI change rejects the entire session without mutating coordinates", () =>
{
    var s = SessionFixture();
    var before = s.Data.ToJsonString();
    try { CalibrationSession.Align(s, new(100, 200), 144, new(1280, 720)); throw new Exception("DPI change accepted"); }
    catch (InvalidOperationException e) { Assert(e.Message == CalibrationSession.DpiChangedMessage); }
    Assert(s.Data.ToJsonString() == before);
});
Test("Window resize rejects the entire session without mutating coordinates", () =>
{
    var s = SessionFixture();
    var before = s.Data.ToJsonString();
    try { CalibrationSession.Align(s, new(100, 200), 96, new(1920, 1080)); throw new Exception("Resize accepted"); }
    catch (InvalidOperationException e) { Assert(e.Message == CalibrationSession.SizeChangedMessage); }
    Assert(s.Data.ToJsonString() == before);
});
Test("Legacy sessions acquire dimensions on their first alignment", () =>
{
    var s = SessionFixture();
    s.Calibration.Data.Remove("session_client_width");
    s.Calibration.Data.Remove("session_client_height");
    CalibrationSession.Align(s, new(10, 20), 96, new(1280, 720));
    Assert(s.Calibration.SessionSize == new ScreenSize(1280, 720));
});
Test("Reset removes every captured geometry but keeps painting preferences", () =>
{
    var s = SessionFixture();
    s.Set("language", "English"); s.Set("speed_profile", "Safe");
    CalibrationSession.Reset(s);
    foreach (var key in new[] { "calibration", "hex_controls", "rust_palette", "palette_click_points", "palette_sources", "brush_calibration_points", "brush_calibration_context" })
        Assert(!s.Data.ContainsKey(key), "stale capture survived: " + key);
    Assert(s.Text("language") == "English" && s.Text("speed_profile") == "Safe");
    CalibrationSession.Align(s, new(20, 40), 144, new(1920, 1080));
    Assert(s.Calibration.SessionDpi == 144 && !s.Calibration.Rect("canvas").Valid);
});
Test("Adaptive calibration remains valid after same-DPI translation", () =>
{
    var s = SessionFixture(); s.Set("adaptive_brush", true);
    AdaptiveBrush.Validate(s);
    var context = AdaptiveBrush.Context(s);
    CalibrationSession.Align(s, new(-200, 80), 96, new(1280, 720));
    Assert(AdaptiveBrush.Context(s) == context);
    AdaptiveBrush.Validate(s);
    var c = s.Calibration; var r = c.Rect("size_track");
    c.SetRect("size_track", r with { Right = r.Right + 10 }); s.SetCalibration(c);
    try { AdaptiveBrush.Validate(s); throw new Exception("Changed track dimensions accepted"); }
    catch (InvalidOperationException) { }
});
Test("Matching legacy adaptive calibration upgrades before translation", () =>
{
    var s = SessionFixture(); s.Set("adaptive_brush", true);
    var r = s.Calibration.Rect("canvas");
    s.Set("brush_calibration_context", $"v1:{r.Width}:{r.Height}:{s.Text("color_mode")}:{s.Int("brush_shape_slot", 3)}:{s.Text("brush_shape")}:" + s.PaintCalibration().Rect("size_track"));
    CalibrationSession.Align(s, new(110, 220), 96, new(1280, 720));
    Assert(s.Text("brush_calibration_context").StartsWith("v2:"));
    AdaptiveBrush.Validate(s);
});
Test("Stale legacy adaptive calibration is never upgraded", () =>
{
    var s = SessionFixture(); s.Set("adaptive_brush", true);
    s.Set("brush_calibration_context", "v1:stale");
    CalibrationSession.Align(s, new(110, 220), 96, new(1280, 720));
    Assert(s.Text("brush_calibration_context") == "v1:stale");
    try { AdaptiveBrush.Validate(s); throw new Exception("Stale legacy calibration accepted"); }
    catch (InvalidOperationException) { }
});
Test("Interface-only settings preserve the RESUME identity", () =>
{
    var s = SessionFixture(); var image = Fixture(); var palette = s.Palette().ToArray();
    var hash = PlanIdentity.Compute(image, s, palette);
    s.Set("language", "English");
    Assert(hash == PlanIdentity.Compute(image, s, palette));
    foreach (var key in new[] { "smooth_preview", "auto_insert_preview", "transfer_simulator", "minimize" })
    {
        s.Set(key, !s.Bool(key));
        Assert(hash == PlanIdentity.Compute(image, s, palette), "interface setting invalidated identity: " + key);
    }
});
Test("Painting settings and captured coordinates still invalidate RESUME", () =>
{
    var s = SessionFixture(); var image = Fixture(); var palette = s.Palette().ToArray();
    var hash = PlanIdentity.Compute(image, s, palette);
    foreach (var (key, value) in new[] { ("cell_px", 2), ("brush_shape_slot", 4) })
    {
        var changed = s.Clone(); changed.Set(key, value);
        Assert(hash != PlanIdentity.Compute(image, changed, palette));
    }
    var moved = s.Clone(); CalibrationSession.Align(moved, new(20, 30), 96, new(1280, 720));
    Assert(hash != PlanIdentity.Compute(image, moved, moved.Palette().ToArray()));
});
Console.WriteLine($"ALL {passed} TESTS PASSED");
