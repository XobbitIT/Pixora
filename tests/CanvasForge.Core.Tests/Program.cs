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
Test("Size values use the interactive range without the numeric field", () =>
{
    Assert(Math.Abs(ControlCurve.Fraction("size", 2) - 1.0 / 99) < 1e-8);
    Assert(Math.Abs(ControlCurve.Fraction("size", 3) - 2.0 / 99) < 1e-8);
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
    Assert(oldTime >= StrokeTiming.Estimate(cfg, speed, 100, false));
    Assert(StrokeTiming.Estimate(cfg, speed, 0, false) >= .04 + StrokeTiming.Release(cfg));
    foreach (var invalid in new[] { "0", "15", "101", "NaN" })
    {
        cfg.Set("input_frame_delay_ms", invalid);
        try { cfg.Validate(); throw new Exception("Invalid delay accepted"); }
        catch (InvalidDataException) { }
    }
});
Test("Both engines preserve their configured release and short-stroke hold", () =>
{
    var cfg = Settings.Defaults();
    foreach (var engine in new[] { "Stable", "Experimental 1 ms" })
    {
        cfg.Set("input_engine", engine);
        cfg.Validate();
        foreach (var speed in SpeedProfile.All)
        {
            double minimum=engine=="Stable"?.016:.012;
            Assert(StrokeTiming.Frame(cfg) >= minimum);
            Assert(StrokeTiming.EndHold(cfg, speed) >= minimum);
            Assert(StrokeTiming.Release(cfg) >= minimum);
            Assert(StrokeTiming.Estimate(cfg, speed, 0, false) >= .04 + StrokeTiming.Release(cfg));
        }
    }
    cfg.Set("cycle_delay_ms", 100);
    Assert(StrokeTiming.Release(cfg) == .1);
    cfg.Set("input_engine", "unknown");
    try { cfg.Validate(); throw new Exception("Unknown engine accepted"); }
    catch (InvalidDataException) { }
});
Test("UI clicks, color changes and HEX never inherit fast stroke timing", () =>
{
    var cfg = Settings.Defaults();
    var click = StrokeTiming.ClickEstimate(cfg);
    var slider = StrokeTiming.SliderChangeEstimate(cfg);
    var color = StrokeTiming.ColorDelay(cfg);
    var hex = StrokeTiming.HexChangeEstimate(cfg);
    cfg.Set("input_engine", "Experimental 1 ms");
    Assert(StrokeTiming.ClickEstimate(cfg) == click && click >= .20);
    Assert(StrokeTiming.SliderChangeEstimate(cfg) == slider);
    Assert(StrokeTiming.ColorDelay(cfg) == color && color >= .1);
    Assert(StrokeTiming.HexChangeEstimate(cfg) == hex);
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
Test("Transparent black edges retain black RGB through resampling and filters", () =>
{
    var source = new PixelImage(8, 8);
    for (var y = 2; y < 6; y++) for (var x = 2; x < 6; x++) source.Set(y * 8 + x, new(0, 0, 0));
    var cfg = Settings.Defaults();
    cfg.Set("fill_subject", false); cfg.Set("fit_mode", "fit whole");
    var prepared = ImageProcessing.Prepare(source, 31, 29, cfg, default);
    Assert(Enumerable.Range(0, 31 * 29).Any(i => prepared.Alpha(i) is > 16 and < 255));
    var filtered = ImageProcessing.Filter(prepared, cfg, default);
    for (var i = 0; i < 31 * 29; i++)
    {
        Assert(filtered.Alpha(i) == prepared.Alpha(i));
        if (filtered.Alpha(i) >= 16) Assert(filtered.Color(i) == new Rgb(0, 0, 0), "white or gray matte introduced");
    }
});
Test("Four-color transparent PNG stays four colors with video quality settings", () =>
{
    var source = new PixelImage(64, 64);
    var colors = new[] { new Rgb(255,51,51), new Rgb(51,255,51), new Rgb(51,51,255), new Rgb(0,0,0) };
    for (var k = 0; k < 4; k++)
        for (var y = 8 + k / 2 * 32; y < 24 + k / 2 * 32; y++)
            for (var x = 8 + k % 2 * 32; x < 24 + k % 2 * 32; x++) source.Set(y * 64 + x, colors[k]);
    foreach (var mode in new[] { ColorMode.RustPalette, ColorMode.HexDirect })
    {
        var cfg = Settings.Defaults(); cfg.Set("color_mode", mode == ColorMode.HexDirect ? "HEX Direct" : "Rust Palette");
        cfg.Set("cell_px", 8); cfg.Set("fit_mode", "smart"); cfg.Set("fill_subject", true);
        cfg.Set("hex_max_colors", "Auto"); cfg.Set("speed_profile", "Max Speed");
        var cal = cfg.Calibration; cal.SetRect("canvas", new(465,150,1509,1191)); cfg.SetCalibration(cal);
        cfg.SetPalette(colors.Append(Rgb.White).Select((c,i) => new PaletteEntry(c,new(i,1),"main")).ToArray());
        var plan = Planner.Build(source,cfg);
        var used = plan.Counts.Keys.Select(i=>plan.Palette[i].Color).ToHashSet();
        Assert(used.SetEquals(colors), "transparent edges expanded the palette");
        Assert(plan.Indices.Contains(-1));
    }
});
Test("Transparent hidden RGB cannot contaminate the neighboring foreground", () =>
{
    var source = new PixelImage(7,7);
    for (var i=0;i<49;i++) source.Set(i,new(255,255,255),0);
    source.Set(24,new(12,34,56),180);
    var cfg=Settings.Defaults(); cfg.Set("fill_subject",false); cfg.Set("fit_mode","fit whole");
    var resized=ImageProcessing.Prepare(source,21,21,cfg,default);
    var filtered=ImageProcessing.Filter(resized,cfg,default);
    for (var i=0;i<441;i++) if (filtered.Alpha(i)>=16)
        Assert(RustSlider.Delta(filtered.Color(i),new(12,34,56))<=1);
});
Test("Real Rust slider crops exclude numeric fields and expose recorded mismatches", () =>
{
    foreach (var (name,expected,desired,match) in new[] {
        ("palette-size-min",0d,0d,true), ("palette-size",.024,2d/99,false),
        ("palette-interval",.324,0d,false), ("palette-opacity",1d,1d,true),
        ("hex-size",.032,2d/99,false), ("hex-opacity",.948,1d,false), ("pause-interval",.324,0d,false) })
    {
        using var stream = new System.IO.Compression.GZipStream(File.OpenRead(Path.Combine(AppContext.BaseDirectory,"Fixtures",name+".rgba.gz")),System.IO.Compression.CompressionMode.Decompress);
        using var reader = new BinaryReader(stream);
        var w=reader.ReadInt32();var h=reader.ReadInt32();
        var hint=new ScreenRect(reader.ReadInt32(),reader.ReadInt32(),reader.ReadInt32(),reader.ReadInt32());
        var image=new PixelImage(w,h,reader.ReadBytes(w*h*4));
        var read=RustSlider.Read(image,hint) ?? throw new Exception("Unreadable fixture: "+name);
        Assert(read.Track.Width==250, "numeric field included: "+name+" width="+read.Track.Width+" fraction="+read.Fraction);
        Assert(Math.Abs(read.Fraction-expected)<.0041,name);
        Assert(read.Matches(desired)==match,"mismatch accepted: "+name);
    }
});
Test("Slider detector rejects clipped, blank and unrelated green content", () =>
{
    var image=new PixelImage(160,40);
    Assert(RustSlider.Read(image,new(5,5,150,35)) is null);
    for (var i=0;i<160*40;i++)image.Set(i,new(70,90,40));
    Assert(RustSlider.Read(image,new(5,5,150,35)) is null);
});
Test("Manual HEX Size anchors stay independent of palette calibration", () =>
{
    var cfg=SessionFixture();cfg.Set("color_mode","HEX Direct");
    var regular=cfg.Calibration.Data.ToJsonString();
    var cal=cfg.PaintCalibration();cal.SetPoint("size_anchor_3",new(722,505));cfg.SetPaintCalibration(cal);
    Assert(cfg.PaintCalibration().Point("size_anchor_3")==new ScreenPoint(722,505));
    Assert(cfg.Calibration.Data.ToJsonString()==regular);
});

(PixelImage Image, ScreenRect Hint) SliderFixture(string name)
{
    using var stream = new System.IO.Compression.GZipStream(File.OpenRead(Path.Combine(AppContext.BaseDirectory,"Fixtures",name+".rgba.gz")),System.IO.Compression.CompressionMode.Decompress);
    using var reader = new BinaryReader(stream);
    var w=reader.ReadInt32(); var h=reader.ReadInt32();
    var hint=new ScreenRect(reader.ReadInt32(),reader.ReadInt32(),reader.ReadInt32(),reader.ReadInt32());
    return (new PixelImage(w,h,reader.ReadBytes(w*h*4)),hint);
}
Test("Automatic capture finds a real slider away from the selection centre", () =>
{
    var (image,_) = SliderFixture("beta2-controls");
    var read = RustSlider.Capture(image,new(0,0,image.Width,170));
    Assert(read.Track == new ScreenRect(188,119,438,159));
    Assert(read.ValueField == new ScreenRect(438,119,518,159));
    Assert(read.Fraction == 0);
});
Test("Automatic capture tolerates asymmetric padding and labels", () =>
{
    var (image,_) = SliderFixture("beta2-controls");
    var random=new Random(1736);
    for(var i=0;i<32;i++)
    {
        var read=RustSlider.Capture(image,new(random.Next(0,120),random.Next(0,100),random.Next(520,531),random.Next(161,175)));
        Assert(read.Track == new ScreenRect(188,119,438,159), "Padding changed track: "+read.Track);
        Assert(read.ValueField == new ScreenRect(438,119,518,159), "Padding changed field: "+read.ValueField);
    }
});
Test("Partial neighbouring sliders do not replace the selected complete slider", () =>
{
    var (image,_) = SliderFixture("beta2-controls");
    var interval=RustSlider.Capture(image,new(0,152,image.Width,224));
    Assert(interval.Track == new ScreenRect(188,175,438,215));
    Assert(Math.Abs(interval.Fraction-.016)<.001);
    Assert(!interval.Matches(ControlCurve.Fraction("interval",.01)));
    var opacity=RustSlider.Capture(image,new(0,213,image.Width,307));
    Assert(opacity.Track == new ScreenRect(188,231,438,271));
    Assert(opacity.Matches(1));
});
Test("Automatic capture rejects multiple complete sliders without guessing", () =>
{
    var (image,hint)=SliderFixture("beta2-controls");
    Assert(RustSlider.Find(image,hint).Count==3);
    try { RustSlider.Capture(image,hint); throw new Exception("Ambiguous capture accepted"); }
    catch(InvalidOperationException e) { Assert(e.Message.Contains("кілька повзунків")); }
});
Test("Automatic capture requires complete track and numeric field", () =>
{
    var (image,_)=SliderFixture("beta2-controls");
    foreach(var rect in new[] { new ScreenRect(0,0,438,170),new ScreenRect(200,0,image.Width,170),new ScreenRect(0,125,image.Width,170) })
        Assert(RustSlider.Find(image,rect).Count==0,"Clipped slider accepted");
    Assert(RustSlider.Find(image,new(-10,0,image.Width,170)).Count==0);
    try { RustSlider.Capture(image,new(0,0,438,170)); throw new Exception("Missing numeric field accepted"); }
    catch(InvalidOperationException e) { Assert(e.Message.Contains("Не знайдено повзунок")); }
});
Test("Automatic capture supports scaled empty, partial and full slider fills", () =>
{
    foreach(var scale in new[]{1,2,3}) foreach(var fraction in new[]{0d,.5,1})
    {
        var image=new PixelImage(360*scale,100*scale);
        var track=new ScreenRect(20*scale,35*scale,270*scale,65*scale);
        var field=new ScreenRect(track.Right,track.Top,350*scale,track.Bottom);
        for(var y=track.Top;y<track.Bottom;y++) for(var x=track.Left;x<field.Right;x++)
            image.Set(y*image.Width+x,x>=track.Right?new(58,65,34):x<track.Left+track.Width*fraction?new(120,143,80):new(79,88,53));
        // An unrelated flat green rectangle has no numeric field.
        for(var y=5*scale;y<20*scale;y++) for(var x=60*scale;x<320*scale;x++)image.Set(y*image.Width+x,new(79,88,53));
        var read=RustSlider.Capture(image,new(0,0,image.Width,image.Height));
        Assert(read.Track==track);Assert(read.ValueField==field);Assert(read.Matches(fraction));
    }
});
Test("Legacy runtime track hints automatically recover the numeric field", () =>
{
    var (image,_)=SliderFixture("beta2-controls");
    var read=RustSlider.Read(image,new(188,175,438,215)) ?? throw new Exception("Legacy track unreadable");
    Assert(read.ValueField==new ScreenRect(438,175,518,215));
    Assert(read.Point(0)==new ScreenPoint(188,195));
    Assert(read.Point(1)==new ScreenPoint(437,195));
});
Test("Automatically captured numeric fields follow palette and HEX session rebasing", () =>
{
    var s=SessionFixture();
    var cal=s.Calibration;cal.SetRect("interval_value_field",new(620,500,680,540));s.SetCalibration(cal);
    var hex=new Calibration((JsonObject)s.Data["hex_controls"]!);hex.SetRect("opacity_value_field",new(900,500,960,540));s.Data["hex_controls"]=hex.Data;
    CalibrationSession.Align(s,new(20,40),96,new(1280,720));
    Assert(s.Calibration.Rect("interval_value_field")==new ScreenRect(630,520,690,560));
    Assert(new Calibration((JsonObject)s.Data["hex_controls"]!).Rect("opacity_value_field")==new ScreenRect(910,520,970,560));
});

Test("Numeric controls reject stale clipboard payloads and wrong exact values", () =>
{
    Assert(ControlNumber.Parse("size", ControlNumber.Marker) is null);
    Assert(ControlNumber.Parse("size", "20") == 20);
    Assert(!ControlNumber.Matches(ControlNumber.Parse("size", "19"), 20));
    Assert(!ControlNumber.Matches(null, 20));
    Assert(ControlNumber.Matches(ControlNumber.Parse("interval", "0.01"), .01));
    Assert(!ControlNumber.Matches(ControlNumber.Parse("interval", "0.02"), .01));
});
Test("Numeric controls support decimal comma without accepting malformed text", () =>
{
    Assert(ControlNumber.Parse("opacity", " 0,5 ") == .5);
    Assert(ControlNumber.Format("interval", .01) == "0.01");
    foreach (var invalid in new[] { "NaN", "Infinity", "1e0", "0.0.1", "0,0.1", "Opacity 1", "-1", "1%", "" })
        Assert(ControlNumber.Parse("opacity", invalid) is null, invalid);
    Assert(ControlNumber.Parse("size", "0") is null);
    Assert(ControlNumber.Parse("interval", "0.001") is null);
    Assert(ControlNumber.Parse("opacity", "1.1") is null);
    Assert(ControlNumber.Parse("unknown", "1") is null);
});
Test("Calibration uses four independent exact settings without mutating the user", () =>
{
    var cfg = Settings.Defaults(); cfg.Set("adaptive_brush", true); cfg.Set("paint_opacity_value", .3);
    cfg.Set("line_mode", true); cfg.Set("background_fill", true); cfg.Set("speed_profile", "Max Speed");
    var before = cfg.Data.ToJsonString();
    var recipes = new[] { 1d, 3, 10, 20 }.Select(x => AdaptiveBrush.CalibrationSettings(cfg, x)).ToArray();
    Assert(recipes.Select(x => x.Number("brush_size_value")).SequenceEqual(new[] { 1d, 3, 10, 20 }));
    foreach (var recipe in recipes)
    {
        Assert(!recipe.Bool("adaptive_brush") && !recipe.Bool("auto_brush_size") && !recipe.Bool("force_precision_controls"));
        Assert(!recipe.Bool("line_mode") && !recipe.Bool("background_fill"));
        Assert(recipe.Number("interval_value") == .01 && recipe.Number("paint_opacity_value") == 1);
        Assert(recipe.Text("input_engine") == "Stable");
    }
    Assert(cfg.Data.ToJsonString() == before);
});
Test("Brush measurement ignores disconnected scene noise and measures solid coverage", () =>
{
    var before = new PixelImage(61,61); var after = before.Clone();
    for (int y=20;y<=40;y++) for(int x=20;x<=40;x++) after.Set(y*61+x,new(240,240,240));
    after.Set(0,new(255,255,255)); after.Set(60*61+60,new(255,255,255));
    var measured = BrushMeasurement.Read(before,after,new(30,30));
    Assert(measured.OuterDiameter == 21 && measured.InnerDiameter == 21 && measured.ChangedPixels == 441);
});
Test("Brush measurement rejects missing dots and clipped paint", () =>
{
    var before=new PixelImage(41,41);var after=before.Clone();
    try { BrushMeasurement.Read(before,after,new(20,20)); throw new Exception("Missing dot accepted"); }
    catch(InvalidOperationException e) { Assert(e.Message.Contains("точки кліку")); }
    for(int x=0;x<=20;x++)after.Set(20*41+x,new(255,255,255));
    try { BrushMeasurement.Read(before,after,new(20,20)); throw new Exception("Clipped dot accepted"); }
    catch(InvalidOperationException e) { Assert(e.Message.Contains("виходить")); }
});
Test("Round brush measurement keeps outer diameter and conservative inner coverage", () =>
{
    var before=new PixelImage(61,61);var after=before.Clone();
    for(int y=0;y<61;y++)for(int x=0;x<61;x++)
        if((x-30)*(x-30)+(y-30)*(y-30)<=100)after.Set(y*61+x,new(230,200,170));
    var result=BrushMeasurement.Read(before,after,new(30,30));
    Assert(result.OuterDiameter==21 && result.InnerDiameter==15);
});
Test("Adaptive maximum brush Size limits wide strokes and still preserves baseline centers", () =>
{
    var cfg=Settings.Defaults();var cal=cfg.Calibration;cal.SetRect("canvas",new(0,0,384,320));cfg.SetCalibration(cal);
    cfg.Set("speed_profile","Safe");cfg.Set("adaptive_brush",true);cfg.Set("adaptive_max_size",10);
    cfg.Set("brush_calibration_points",new double[][] { [1,3,1],[3,5,3],[10,21,13],[20,35,23] });
    cfg.Set("brush_calibration_context",AdaptiveBrush.Context(cfg));
    var indices=Enumerable.Repeat(0,384*320).ToArray();
    var plan=new PaintPlan { Width=384,Height=320,Mode=ColorMode.RustPalette,Indices=indices,Palette=[],Preview=new(384,320),
        Strokes=Planner.Group(indices,384,320,true),Counts=new(){{0,indices.Length}},Identity="cap-test" };
    var wide=AdaptiveBrush.Build(plan,cfg)[0];Assert(wide.Any(x=>x.Size==10));Assert(wide.All(x=>x.Size<=10));
    var covered=new bool[indices.Length];
    foreach(var op in wide)
    {
        var line=op.Line;int n=Math.Max(Math.Abs(line.X2-line.X1),Math.Abs(line.Y2-line.Y1));
        for(int k=0;k<=n;k++)
        {
            int x=line.X1+k*Math.Sign(line.X2-line.X1),y=line.Y1+k*Math.Sign(line.Y2-line.Y1);
            for(int yy=y-op.FillRadius;yy<=y+op.FillRadius;yy++) for(int xx=x-op.FillRadius;xx<=x+op.FillRadius;xx++)covered[yy*384+xx]=true;
        }
    }
    foreach(var line in Coverage.Build(plan,cfg)[0])
    {
        int n=Math.Max(Math.Abs(line.X2-line.X1),Math.Abs(line.Y2-line.Y1));
        for(int k=0;k<=n;k++)Assert(covered[(line.Y1+k*Math.Sign(line.Y2-line.Y1))*384+line.X1+k*Math.Sign(line.X2-line.X1)]);
    }
});
Test("Adaptive calibration readiness detects stale geometry and incomplete HEX setup", () =>
{
    var cfg=Settings.Defaults();Assert(!AdaptiveBrush.CalibrationCurrent(cfg));Assert(AdaptiveBrush.SetupProblem(cfg) is not null);
    var cal=cfg.Calibration;cal.SetRect("canvas",new(0,0,400,400));cal.SetRect("size_track",new(500,10,750,50));cfg.SetCalibration(cal);
    cfg.Set("brush_calibration_points",new double[][] { [1,3,1],[10,21,13] });cfg.Set("brush_calibration_context",AdaptiveBrush.Context(cfg));
    Assert(AdaptiveBrush.CalibrationCurrent(cfg));
    cal.SetRect("canvas",new(0,0,401,400));cfg.SetCalibration(cal);Assert(!AdaptiveBrush.CalibrationCurrent(cfg));
    cfg.Set("color_mode","HEX Direct");Assert(!AdaptiveBrush.CalibrationCurrent(cfg));Assert(AdaptiveBrush.SetupProblem(cfg) is not null);
});

Test("Size 1 raster offset is accepted beside the unchanged click pixel", () =>
{
    const int w=41;var centre=new ScreenPoint(20,20);
    var before=new PixelImage(w,w);var after=before.Clone();
    foreach(var (x,y) in new[]{(19,21),(20,21),(19,22),(20,22)})after.Set(y*w+x,new(255,255,255));
    Assert(before.Color(centre.Y*w+centre.X)==after.Color(centre.Y*w+centre.X));
    var measured=BrushMeasurement.Read(before,after,centre);
    Assert(measured.ChangedPixels==4 && measured.SeedOffset==new ScreenPoint(0,1));
    Assert(measured.OuterDiameter==5 && measured.InnerDiameter==1);
});
Test("Small raster offsets retain conservative bounds around the actual click", () =>
{
    for(int dy=-4;dy<=4;dy++)for(int dx=-4;dx<=4;dx++)
    {
        var before=new PixelImage(31,31);var after=before.Clone();after.Set((15+dy)*31+15+dx,new(255,255,255));
        var measured=BrushMeasurement.Read(before,after,new(15,15));
        Assert(measured.SeedOffset==new ScreenPoint(dx,dy));
        Assert(measured.OuterDiameter==2*Math.Max(Math.Abs(dx),Math.Abs(dy))+1);
        Assert(measured.InnerDiameter==1 && measured.ChangedPixels==1);
    }
});
Test("A changed pixel beyond the local seed search cannot satisfy calibration", () =>
{
    var before=new PixelImage(41,41);var after=before.Clone();after.Set(20*41+25,new(255,255,255));
    try{BrushMeasurement.Read(before,after,new(20,20));throw new Exception("Distant scene noise accepted");}
    catch(InvalidOperationException e){Assert(e.Message.Contains("точки кліку"));}
});
Test("A hollow component never claims solid coverage over its unchanged centre", () =>
{
    var before=new PixelImage(41,41);var after=before.Clone();
    for(int dy=-2;dy<=2;dy++)for(int dx=-2;dx<=2;dx++)
        if(dx!=0||dy!=0)after.Set((20+dy)*41+20+dx,new(255,255,255));
    var measured=BrushMeasurement.Read(before,after,new(20,20));
    Assert(measured.InnerDiameter==1 && measured.OuterDiameter==5);
});


PaintPlan SpeedPlan(int[] indices,int w,int h) => new() { Width=w,Height=h,Mode=ColorMode.HexDirect,
    Indices=indices,Palette=[new(new(255,0,0),null,"hex"),new(new(0,255,0),null,"hex"),new(new(0,0,255),null,"hex")],
    Preview=new(w,h),Strokes=Planner.Group(indices,w,h,true),Counts=indices.Where(x=>x>=0).GroupBy(x=>x).ToDictionary(x=>x.Key,x=>x.Count()),Identity="synthetic-speed" };
HashSet<ScreenPoint> Centres(IEnumerable<ScreenLine> lines)
{
    var set=new HashSet<ScreenPoint>();
    foreach(var l in lines)for(int k=0;k<=TransferSchedule.Length(l);k++)set.Add(new(l.X1+k*Math.Sign(l.X2-l.X1),l.Y1+k*Math.Sign(l.Y2-l.Y1)));
    return set;
}
Test("Normal transfer preserves every stroke and its order",()=>
{
    var cfg=Config(ColorMode.HexDirect,80,60);var plan=SpeedPlan(Enumerable.Repeat(0,80*60).ToArray(),80,60);
    var old=AdaptiveBrush.Build(plan,cfg);var batches=TransferSchedule.Build(plan,cfg);
    foreach(var color in old.Keys)Assert(batches[color].All(x=>x.SourceStrokes==1)&&batches[color].Select(x=>x.Segments.Single()).SequenceEqual(old[color].Select(x=>x.Line)));
});
Test("Fast fine paths preserve exact coverage across colors, holes and scaled cells",()=>
{
    var random=new Random(641);
    foreach(int pitch in new[]{1,2,3})for(int sample=0;sample<25;sample++)
    {
        int w=17,h=13;var indices=Enumerable.Range(0,w*h).Select(_=>random.Next(-1,3)).ToArray();
        var cfg=Config(ColorMode.HexDirect,37,29);cfg.Set("speed_profile",pitch==1?"Rapid":pitch==2?"Turbo":"Max Speed");
        cfg.Set("fast_transfer",true);var plan=SpeedPlan(indices,w,h);var old=AdaptiveBrush.Build(plan,cfg);var joined=TransferSchedule.Build(plan,cfg);
        foreach(var color in old.Keys)
        {
            Assert(Centres(old[color].Select(x=>x.Line)).SetEquals(Centres(joined[color].SelectMany(x=>x.Segments))),"Fine coverage changed");
            Assert(joined[color].Sum(x=>x.SourceStrokes)==old[color].Count);
            foreach(var batch in joined[color])for(int i=1;i<batch.Segments.Count;i++)
                Assert(batch.Segments[i-1].X2==batch.Segments[i].X1&&batch.Segments[i-1].Y2==batch.Segments[i].Y1,"Disconnected drag");
        }
    }
});
Test("Fine connections cannot cross an unpainted row",()=>
{
    var cfg=Config(ColorMode.HexDirect,80,3);cfg.Set("fast_transfer",true);
    var indices=Enumerable.Range(0,240).Select(i=>i/80==1?-1:0).ToArray();var plan=SpeedPlan(indices,80,3);
    Assert(TransferSchedule.Build(plan,cfg)[0].Count==2,"Joined through transparency");
});
Test("Batching is bounded and deterministic for RESUME",()=>
{
    var cfg=Config(ColorMode.HexDirect,100,100);cfg.Set("fast_transfer",true);
    var plan=SpeedPlan(Enumerable.Repeat(0,10000).ToArray(),100,100);var a=TransferSchedule.Build(plan,cfg)[0];var b=TransferSchedule.Build(plan,cfg)[0];
    Assert(a.Count==7&&a.Sum(x=>x.SourceStrokes)==100&&a.All(x=>x.SourceStrokes<=16));
    Assert(a.Count==b.Count);for(int i=0;i<a.Count;i++)Assert(a[i].Size==b[i].Size&&a[i].SourceStrokes==b[i].SourceStrokes&&a[i].Segments.SequenceEqual(b[i].Segments));
});
Test("Translucent and patterned brushes keep independent strokes",()=>
{
    var cfg=Config(ColorMode.HexDirect,60,40);cfg.Set("fast_transfer",true);var plan=SpeedPlan(Enumerable.Repeat(0,2400).ToArray(),60,40);
    cfg.Set("paint_opacity_value",.5);Assert(TransferSchedule.Build(plan,cfg)[0].All(x=>x.SourceStrokes==1));
    cfg.Set("paint_opacity_value",1);cfg.Set("brush_shape_slot",1);Assert(TransferSchedule.Build(plan,cfg)[0].All(x=>x.SourceStrokes==1));
    cfg.Set("brush_shape_slot",3);cfg.Set("line_mode",true);cfg.Set("coverage_mode","Fast");
    Assert(!TransferSchedule.Fast(cfg)&&TransferSchedule.Build(plan,cfg)[0].All(x=>x.SourceStrokes==1));
});
Test("Wide connections respect the calibrated footprint and brush-size changes",()=>
{
    var cfg=Config(ColorMode.HexDirect,90,60);cfg.Set("fast_transfer",true);
    // Synthetic tracks with a green obstacle on the otherwise red connector.
    var indices=Enumerable.Repeat(0,90*60).ToArray();indices[24*90+70]=1;var plan=SpeedPlan(indices,90,60);
    var cal=cfg.Calibration;cal.SetRect("canvas",new(0,0,90,60));cfg.SetCalibration(cal);
    var ops=new Dictionary<int,List<BrushStroke>>{{0,[new(new(15,15,70,15),10,4,2),new(new(15,30,70,30),10,4,2),new(new(15,40,70,40),20,7,3)]}};
    var joined=TransferSchedule.Build(plan,cfg,ops)[0];Assert(joined.Count==3,"Wide path crossed color or size boundary");
    indices[24*90+70]=0;joined=TransferSchedule.Build(plan,cfg,ops)[0];Assert(joined.Count==2&&joined[0].SourceStrokes==2);
    foreach(var l in joined[0].Segments)for(int k=0;k<=TransferSchedule.Length(l);k++)
    {
        int x=l.X1+k*Math.Sign(l.X2-l.X1),y=l.Y1+k*Math.Sign(l.Y2-l.Y1);
        for(int yy=y-4;yy<=y+4;yy++)for(int xx=x-4;xx<=x+4;xx++)Assert(xx>=0&&yy>=0&&xx<90&&yy<60&&indices[yy*90+xx]==0);
    }
});
Test("Adaptive fast schedule retains calibrated geometry including fine edges",()=>
{
    var cfg=Config(ColorMode.RustPalette,384,320);var cal=cfg.Calibration;cal.SetRect("canvas",new(0,0,384,320));cfg.SetCalibration(cal);
    cfg.Set("adaptive_brush",true);cfg.Set("fast_transfer",true);cfg.Set("brush_calibration_points",new double[][]{[1,3,1],[3,5,3],[10,21,13],[20,35,23]});
    cfg.Set("brush_calibration_context",AdaptiveBrush.Context(cfg));var indices=Enumerable.Repeat(0,384*320).ToArray();
    for(int y=140;y<160;y++)for(int x=180;x<200;x++)indices[y*384+x]=-1;
    var plan=SpeedPlan(indices,384,320);var original=AdaptiveBrush.Build(plan,cfg);var joined=TransferSchedule.Build(plan,cfg,original);
    Assert(joined[0].Count<original[0].Count);Assert(joined[0].Sum(x=>x.SourceStrokes)==original[0].Count);
    foreach(var size in original[0].Select(x=>x.Size).Distinct())
    {
        var before=Centres(original[0].Where(x=>x.Size==size).Select(x=>x.Line));var after=Centres(joined[0].Where(x=>x.Size==size).SelectMany(x=>x.Segments));
        Assert(before.IsSubsetOf(after));if(size==0)Assert(before.SetEquals(after));
        int radius=original[0].Where(x=>x.Size==size).Max(x=>x.OuterRadius);
        foreach(var p in after.Except(before))for(int yy=p.Y-radius;yy<=p.Y+radius;yy++)for(int xx=p.X-radius;xx<=p.X+radius;xx++)
            Assert(xx>=0&&yy>=0&&xx<384&&yy<320&&indices[yy*384+xx]==0,"New wide path crosses a boundary");
    }
});
Test("Fast timing keeps frame waits and verifies faster numeric input",()=>
{
    var cfg=Settings.Defaults();double numeric=StrokeTiming.SliderChangeEstimate(cfg),hex=StrokeTiming.HexChangeEstimate(cfg);
    cfg.Set("fast_transfer",true);cfg.Set("input_engine","Experimental 1 ms");
    Assert(StrokeTiming.SliderChangeEstimate(cfg)<numeric/2&&StrokeTiming.HexChangeEstimate(cfg)<hex/2);
    Assert(StrokeTiming.ClickHold(cfg)>=2*StrokeTiming.Frame(cfg)&&StrokeTiming.KeyHold(cfg)>=2*StrokeTiming.Frame(cfg));
    Assert(StrokeTiming.HexCommit(cfg)>=cfg.Number("hex_apply_delay_ms")/1000);
    var dot=new PaintBatch(0,new[]{new ScreenLine(1,1,1,1)},1);
    Assert(TransferSchedule.EstimateBatch(cfg,SpeedProfile.Get("Rapid"),dot)>=.04+StrokeTiming.Release(cfg));
});
Test("Fast settings invalidate old checkpoints but preserve adaptive calibration",()=>
{
    var cfg=SessionFixture();var image=Fixture();string identity=PlanIdentity.Compute(image,cfg,[]),context=AdaptiveBrush.Context(cfg);
    cfg.Set("fast_transfer",true);Assert(PlanIdentity.Compute(image,cfg,[])!=identity&&AdaptiveBrush.Context(cfg)==context);
    identity=PlanIdentity.Compute(image,cfg,[]);cfg.Set("fast_path_batch_points",4);Assert(PlanIdentity.Compute(image,cfg,[])!=identity);
    Assert(AdaptiveBrush.CalibrationCurrent(cfg));var calibration=AdaptiveBrush.CalibrationSettings(cfg,10);
    Assert(!calibration.Bool("fast_transfer")&&calibration.Text("input_engine")=="Stable");
});
Test("Fast motion packet validation rejects unsupported values",()=>
{
    foreach(double value in new[]{0d,17,1.5,double.NaN})
    {
        var cfg=Settings.Defaults();if(double.IsFinite(value))cfg.Set("fast_path_batch_points",value);else cfg.Set("fast_path_batch_points","NaN");
        try{cfg.Validate();throw new Exception("Invalid motion packet accepted");}catch(InvalidDataException){}
    }
    foreach(int value in new[]{1,8,16}){var cfg=Settings.Defaults();cfg.Set("fast_path_batch_points",value);cfg.Validate();}
});
Test("Synthetic transfer estimates improve without changing image detail",()=>
{
    foreach(string name in new[]{"solid","four-colors","detail-and-holes"})
    {
        const int w=128,h=128;var indices=new int[w*h];
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)indices[y*w+x]=name=="solid"?0:name=="four-colors"?(x<w/2?0:y<h/2?1:2):((x/8+y/8)%3==0?-1:(x/8+y/8)%3);
        var cfg=Config(ColorMode.HexDirect,768,768);cfg.Set("cell_px",3);cfg.Set("adaptive_brush",false);
        var plan=SpeedPlan(indices,w,h);double normal=Coverage.EstimateSeconds(plan,cfg);int drags=TransferSchedule.Build(plan,cfg).Values.Sum(x=>x.Count);
        string detail=cfg.Data["cell_px"]!.ToJsonString();cfg.Set("fast_transfer",true);double fast=Coverage.EstimateSeconds(plan,cfg);
        var schedule=TransferSchedule.Build(plan,cfg);Assert(fast<normal&&cfg.Data["cell_px"]!.ToJsonString()==detail);
        Console.WriteLine($"SYNTHETIC ESTIMATE {name}: {normal:F2}s -> {fast:F2}s; drags {drags} -> {schedule.Values.Sum(x=>x.Count)} (not measured in Rust)");
    }
});

Test("Actual fast input fills four synthetic blocks rather than their left edges",()=>
{
    const int w=96,h=96;var indices=Enumerable.Repeat(-1,w*h).ToArray();
    for(int y=4;y<92;y++)for(int x=4;x<92;x++)
        if((x<40||x>=56)&&(y<40||y>=56))indices[y*w+x]=(x>=56?1:0)+(y>=56?2:0);
    var cfg=Config(ColorMode.HexDirect,576,576);cfg.Set("fast_transfer",true);cfg.Set("adaptive_brush",false);
    var plan=SpeedPlan(indices,w,h);var source=AdaptiveBrush.Build(plan,cfg);var schedule=TransferSchedule.Build(plan,cfg,source);
    foreach(var color in source.Keys)
    {
        var input=new RecordingStrokeInput();
        foreach(var batch in schedule[color])StrokeMotion.Draw(batch,cfg,SpeedProfile.Get(cfg.Text("speed_profile")),input);
        Assert(input.Painted.SetEquals(Centres(source[color].Select(x=>x.Line))),"Input omitted the interior of a block");
        Assert(input.Downs==schedule[color].Count&&input.Ups==input.Downs&&!input.Held);
    }
});
Test("Long horizontal and vertical drags send every physical pixel in either direction",()=>
{
    var cfg=Settings.Defaults();cfg.Set("fast_transfer",true);
    foreach(var line in new[]{new ScreenLine(-30,7,1100,7),new ScreenLine(1100,7,-30,7),new ScreenLine(9,-20,9,1060),new ScreenLine(9,1060,9,-20)})
    {
        var input=new RecordingStrokeInput();StrokeMotion.Draw(new(0,new[]{line},1),cfg,SpeedProfile.Get("Max Speed"),input);
        Assert(input.Painted.SetEquals(Centres(new[]{line})),"Long drag omitted an intermediate point");
        Assert(input.Packets.All(x=>x.Length<=8));
        for(int i=1;i<input.HeldMoves.Count;i++)Assert(Math.Abs(input.HeldMoves[i].X-input.HeldMoves[i-1].X)+Math.Abs(input.HeldMoves[i].Y-input.HeldMoves[i-1].Y)==1,"Held cursor jumped");
    }
});
Test("Connected turns preserve endpoints with a single mouse press",()=>
{
    var cfg=Settings.Defaults();cfg.Set("fast_transfer",true);
    ScreenLine[] lines=[new(10,10,80,10),new(80,10,80,11),new(80,11,10,11)];
    var input=new RecordingStrokeInput();StrokeMotion.Draw(new(0,lines,2),cfg,SpeedProfile.Get("Rapid"),input);
    Assert(input.Painted.SetEquals(Centres(lines))&&input.Downs==1&&input.Ups==1&&!input.Held);
});
Test("Packet settings alter delivery size without altering the painted path",()=>
{
    var cfg=Settings.Defaults();cfg.Set("fast_transfer",true);ScreenLine line=new(1,2,1002,2);
    var expected=Centres(new[]{line});
    foreach(int size in new[]{1,4,8,16})
    {
        cfg.Set("fast_path_batch_points",size);var input=new RecordingStrokeInput();
        StrokeMotion.Draw(new(0,new[]{line},1),cfg,SpeedProfile.Get("Max Speed"),input);
        Assert(input.Painted.SetEquals(expected)&&input.Packets.All(x=>x.Length<=size));
        Assert(input.WaitDurations.All(x=>x>=.001));
    }
});
Test("Dots keep the minimum hold and game-frame release in both engines",()=>
{
    var cfg=Settings.Defaults();cfg.Set("fast_transfer",true);
    foreach(string engine in new[]{"Stable","Experimental 1 ms"})
    {
        cfg.Set("input_engine",engine);var input=new RecordingStrokeInput();
        StrokeMotion.Draw(new(0,new[]{new ScreenLine(3,4,3,4)},1),cfg,SpeedProfile.Get("Max Speed"),input);
        Assert(input.Painted.SetEquals(new[]{new ScreenPoint(3,4)}));
        Assert(input.UpAt-input.DownAt>=.04-1e-9&&input.Seconds-input.UpAt>=StrokeTiming.Frame(cfg)-1e-9);
    }
});
Test("Motion and pause failures release the held mouse and propagate",()=>
{
    var cfg=Settings.Defaults();cfg.Set("fast_transfer",true);
    foreach(bool failMove in new[]{true,false})
    {
        var input=new RecordingStrokeInput{FailHeldMove=failMove,FailHeldWait=!failMove};
        try{StrokeMotion.Draw(new(0,new[]{new ScreenLine(1,2,300,2)},1),cfg,SpeedProfile.Get("Rapid"),input);throw new Exception("Input failure swallowed");}
        catch(OperationCanceledException){}
        Assert(input.Downs==1&&input.Ups==1&&!input.Held,"Mouse remained held after interruption");
    }
});
Test("Invalid paths are rejected before any input is sent",()=>
{
    foreach(ScreenLine[] lines in new ScreenLine[][]{[],[new(0,0,1,1)],[new(0,0,4,0),new(8,0,12,0)]})
    {
        var input=new RecordingStrokeInput();
        try{StrokeMotion.Draw(new(0,lines,1),Settings.Defaults(),SpeedProfile.Get("Rapid"),input);throw new Exception("Invalid batch accepted");}
        catch(ArgumentException){}
        Assert(input.Packets.Count==0&&input.Downs==0&&input.Ups==0);
    }
});
Test("Fast ETA matches the delays used by the actual stroke executor",()=>
{
    var cfg=Settings.Defaults();cfg.Set("fast_transfer",true);
    foreach(var speed in SpeedProfile.All)foreach(int packet in new[]{1,8,16})foreach(int length in new[]{0,1,7,8,9,257,1024})
    {
        cfg.Set("fast_path_batch_points",packet);
        var batch=new PaintBatch(0,new[]{new ScreenLine(0,0,length,0),new ScreenLine(length,0,length,4)},2);
        var input=new RecordingStrokeInput();StrokeMotion.Draw(batch,cfg,speed,input);
        Assert(Math.Abs(input.Seconds-TransferSchedule.EstimateBatch(cfg,speed,batch))<1e-9,"ETA disagrees with execution");
    }
});
Test("Legacy cursor-jump settings no longer affect identity or motion",()=>
{
    var cfg=Settings.Defaults();cfg.Set("fast_transfer",true);var image=Fixture();string identity=PlanIdentity.Compute(image,cfg,[]);
    foreach(int legacy in new[]{32,256,512})
    {
        cfg.Set("fast_move_span_px",legacy);cfg.Validate();Assert(PlanIdentity.Compute(image,cfg,[])==identity);
        var input=new RecordingStrokeInput();ScreenLine line=new(0,0,512,0);
        StrokeMotion.Draw(new(0,new[]{line},1),cfg,SpeedProfile.Get("Rapid"),input);
        Assert(input.Painted.SetEquals(Centres(new[]{line}))&&input.Packets.All(x=>x.Length<=8));
    }
});

Settings ProbeConfig()
{
    var cfg=SessionFixture();cfg.Set("calibrated_strokes",true);
    var samples=new List<SpeedSample>{new(1,StrokeMethod.Paced,false,12,17,1,32,3,1),new(1,StrokeMethod.Shift,false,8,12,1,32,3,1),new(1,StrokeMethod.Paced,true,20,27,1,32,3,1)};
    cfg.Set("speed_probe_profile",new SpeedProbeProfile(SpeedCalibration.Context(cfg),DateTimeOffset.UtcNow,samples));return cfg;
}
Test("Probe accepts repeated full coverage and rejects unvalidated profiles",()=>
{
    var cfg=ProbeConfig();Assert(SpeedCalibration.Use(cfg));
    foreach(var invalid in new[]{new SpeedSample(1,StrokeMethod.Shift,false,8,8,1,32,3,1),new(1,StrokeMethod.Shift,false,8,12,1,32,2,1),new(1,StrokeMethod.Shift,false,8,12,1,32,3,.99),new(1,StrokeMethod.Paced,false,8,12,64,32,3,1)})
    {cfg.Set("speed_probe_profile",new SpeedProbeProfile(SpeedCalibration.Context(cfg),DateTimeOffset.UtcNow,[invalid]));Assert(!SpeedCalibration.Current(cfg));}
});
Test("Probe context rejects changed samples geometry DPI and color mode",()=>
{
    var cfg=ProbeConfig();string context=SpeedCalibration.Context(cfg);
    cfg.Set("language","English");Assert(SpeedCalibration.Context(cfg)==context);
    foreach(string key in new[]{"color_mode","brush_shape","brush_shape_slot"})
    {var changed=cfg.Clone();changed.Set(key,key=="brush_shape_slot"?"4":key=="color_mode"?"HEX Direct":"Square");Assert(!SpeedCalibration.Current(changed));}
    var cal=cfg.Calibration;cal.SetSession(new(0,0),144,new(1280,720));cfg.SetCalibration(cal);Assert(!SpeedCalibration.Current(cfg));
});
Test("Probe routes are independent of Precision adaptive and legacy Shift flag",()=>
{
    var cfg=ProbeConfig();cfg.Set("adaptive_brush",true);cfg.Set("line_mode",true);AdaptiveBrush.Validate(cfg);
    Assert(Coverage.ShiftLine(cfg,1,new(0,0,32,0)));Assert(!Coverage.ShiftLine(cfg,3,new(0,0,32,0)));
    Assert(!Coverage.ShiftLine(cfg,1,new(0,0,0,32)));Assert(!Coverage.ShiftLine(cfg,1,new(0,0,1,0)));
    cfg.Set("paint_opacity_value",.5);Assert(!SpeedCalibration.Use(cfg));
});
Test("Long calibrated Shift lines split at measured lengths and release between pieces",()=>
{
    var cfg=ProbeConfig();var line=new ScreenLine(0,0,100,0);var sample=SpeedCalibration.Resolve(cfg,1,line)!;
    var input=new RecordingStrokeInput();CalibratedMotion.Draw(line,sample,input);
    Assert(input.Downs==4&&input.Ups==4&&input.ShiftDowns==4&&input.ShiftUps==4&&!input.Held&&!input.ShiftHeld);
    Assert(input.Packets.All(x=>x.Length==1));
    Assert(Math.Abs(input.Seconds-CalibratedMotion.Estimate(line,sample))<1e-9);
});
Test("Paced probe emits individually timed endpoints in both directions",()=>
{
    foreach(var line in new[]{new ScreenLine(50,20,0,20),new ScreenLine(10,60,10,0)})
    {
        var sample=new SpeedSample(1,StrokeMethod.Paced,line.X1==line.X2,20,27,3,32,3,1);var input=new RecordingStrokeInput();
        CalibratedMotion.Draw(line,sample,input);Assert(input.Cursor==new ScreenPoint(line.X2,line.Y2));
        Assert(input.Packets.All(x=>x.Length==1)&&input.WaitDurations.All(x=>x>=.027));
        Assert(Math.Abs(input.Seconds-CalibratedMotion.Estimate(line,sample))<1e-9);
    }
});
Test("Calibrated motion interruptions release both Shift and mouse",()=>
{
    foreach(var method in new[]{StrokeMethod.Paced,StrokeMethod.Shift})
    {
        var input=new RecordingStrokeInput{FailHeldMove=true};
        try{CalibratedMotion.Draw(new(0,0,80,0),new(1,method,false,8,12,1,32,3,1),input);throw new Exception("Failure swallowed");}catch(OperationCanceledException){}
        Assert(!input.Held&&!input.ShiftHeld&&input.Ups==1);if(method==StrokeMethod.Shift)Assert(input.ShiftUps==1);
    }
});
Test("Probe tiles cannot overlap and insufficient clean area is rejected",()=>
{
    var tiles=SpeedCalibration.Tiles(new(20,30,1020,1030),24);Assert(tiles.Count==62);
    foreach(var t in tiles)Assert(t.Area.Left>=20&&t.Area.Top>=30&&t.Area.Right<=1020&&t.Area.Bottom<=1030);
    for(int i=0;i<tiles.Count;i++)for(int j=i+1;j<tiles.Count;j++)Assert(tiles[i].Area.Right<=tiles[j].Area.Left||tiles[j].Area.Right<=tiles[i].Area.Left||tiles[i].Area.Bottom<=tiles[j].Area.Top||tiles[j].Area.Bottom<=tiles[i].Area.Top);
    try{SpeedCalibration.Tiles(new(0,0,240,240),24);throw new Exception("Small area accepted");}catch(InvalidOperationException){}
});
Test("Coverage audit distinguishes filled pixels gaps and uncertain colors",()=>
{
    var before=new PixelImage(40,30);var after=before.Clone();var mask=new bool[1200];
    for(int y=5;y<25;y++)for(int x=5;x<35;x++){int i=y*40+x;mask[i]=true;after.Set(i,new(255,20,20));}
    for(int x=10;x<20;x++)after.Set(15*40+x,before.Color(15*40+x));after.Set(16*40+10,new(20,255,20));
    var result=CoverageAudit.Read(before,after,mask,new(new(255,20,20),12));
    Assert(result.Expected==600&&result.Missing==10&&result.Unknown==1&&result.Covered==589&&!result.Passed);
});
Test("Coverage audit never certifies indistinguishable background or missing reference",()=>
{
    var before=new PixelImage(20,20);var mask=Enumerable.Repeat(true,400).ToArray();
    Assert(CoverageAudit.Read(before,before.Clone(),mask).Unknown==400);
    Assert(CoverageAudit.Read(before,before.Clone(),mask,new(new(4,4,4),12)).Unknown==400);
});
Test("Coverage reference is learned from stable consistent paint and rejects mixed colors",()=>
{
    var before=new PixelImage(20,20);var after=before.Clone();var mask=Enumerable.Repeat(true,400).ToArray();
    for(int i=0;i<400;i++)after.Set(i,new(220,40,30));
    Assert(CoverageAudit.Read(before,after,mask).Passed);
    for(int i=0;i<200;i++)after.Set(i,new(30,220,40));Assert(CoverageAudit.Learn(before,after,mask) is null);
});
Test("Scene changes outside the expected region make audit uncertain",()=>
{
    var before=new PixelImage(40,40);var after=before.Clone();var mask=new bool[1600];
    for(int i=0;i<200;i++){mask[i]=true;after.Set(i,new(255,0,0));}
    for(int i=300;i<500;i++)after.Set(i,new(0,255,0));
    var result=CoverageAudit.Read(before,after,mask,new(new(255,0,0),12));Assert(!result.Passed&&result.Unknown==200&&result.Missing==0);
    Assert(!CoverageAudit.Stable(before,after));
});
Test("Probe reference detects broken longitudinal lines",()=>
{
    var before=new PixelImage(64,64);var after=before.Clone();
    for(int x=10;x<=53;x++)after.Set(32*64+x,new(255,0,0));
    var mask=CoverageAudit.ProbeMask(before,after,new(10,32,53,32),4);Assert(mask.Count(x=>x)==44);
    after.Set(32*64+30,before.Color(32*64+30));
    try{CoverageAudit.ProbeMask(before,after,new(10,32,53,32),4);throw new Exception("Broken reference accepted");}catch(InvalidOperationException){}
});
Test("Repair strokes preserve the entire calibrated footprint inside the expected color",()=>
{
    var canvas=new ScreenRect(100,200,160,260);var expected=new bool[3600];var gaps=new bool[3600];
    for(int y=5;y<55;y++)for(int x=5;x<55;x++)expected[y*60+x]=true;
    for(int y=0;y<60;y++)for(int x=0;x<60;x++)gaps[y*60+x]=true;
    var repairs=CoverageAudit.Repair(gaps,expected,canvas,4);Assert(repairs.Count==42);
    foreach(var point in Centres(repairs))for(int dy=-4;dy<=4;dy++)for(int dx=-4;dx<=4;dx++)Assert(expected[(point.Y-200+dy)*60+point.X-100+dx]);
    Assert(CoverageAudit.Repair(new bool[3600],expected,canvas,4).Count==0);
});
Test("Expected audit mask scales cells exactly and preserves transparent holes",()=>
{
    var plan=SpeedPlan(new[]{0,-1,1,0},2,2);var mask=CoverageAudit.Expected(plan,new(0,0,20,20),0);
    Assert(mask.Count(x=>x)==200);Assert(mask[0]&&!mask[19]&&!mask[19*20]&&mask[399]);
});
Test("Audit and repair validation rejects unsupported settings",()=>
{
    var cfg=Settings.Defaults();cfg.Set("coverage_audit",true);
    cfg.Validate();Assert(CoverageAudit.SetupProblem(cfg) is not null,"Audit without calibration accepted");
    cfg.Set("coverage_audit",false);cfg.Set("audit_repair_passes",3);
    try{cfg.Validate();throw new Exception("Unbounded repair accepted");}catch(InvalidDataException){}
});

Test("Malformed or null probe samples are rejected without enabling input",()=>
{
    var cfg=ProbeConfig();cfg.Data["speed_probe_profile"]!["Samples"]!.AsArray().Add((System.Text.Json.Nodes.JsonNode?)null);
    Assert(!SpeedCalibration.Current(cfg));
    cfg.Set("speed_probe_profile","broken profile");Assert(!SpeedCalibration.Current(cfg));
    cfg=ProbeConfig();cfg.Set("brush_calibration_points",new double[][]{[1,3,4],[3,5,3],[10,21,13]});
    try{SpeedCalibration.Footprint(cfg,1);throw new Exception("Invalid footprint accepted");}catch(InvalidOperationException){}
});
Test("Calibrated input validates before sending and releases Shift during key settling failure",()=>
{
    foreach(var sample in new[]{new SpeedSample(1,(StrokeMethod)99,false,8,12,1,32,3,1),new(1,StrokeMethod.Paced,false,8,0,1,32,3,1),new(1,StrokeMethod.Paced,false,8,12,0,32,3,1)})
    {
        var input=new RecordingStrokeInput();
        try{CalibratedMotion.Draw(new(0,0,32,0),sample,input);throw new Exception("Invalid input accepted");}catch(ArgumentException){}
        Assert(input.Downs==0&&input.ShiftDowns==0&&input.Packets.Count==0);
    }
    var failure=new RecordingStrokeInput{FailShiftWait=true};
    try{CalibratedMotion.Draw(new(0,0,32,0),new(1,StrokeMethod.Shift,false,8,12,1,32,3,1),failure);throw new Exception("Failure swallowed");}catch(OperationCanceledException){}
    Assert(!failure.Held&&!failure.ShiftHeld&&failure.ShiftUps==1&&failure.Downs==0);
});
Test("Stale audit setup remains editable while execution readiness fails",()=>
{
    var cfg=ProbeConfig();cfg.Set("coverage_audit",true);Assert(CoverageAudit.SetupProblem(cfg) is null);cfg.Validate();
    cfg.Set("brush_shape_slot",4);cfg.Set("brush_shape","Square");cfg.Validate();
    Assert(CoverageAudit.SetupProblem(cfg) is not null);
    cfg.Set("coverage_audit",false);cfg.Validate();
});

Test("Experimental delay is configurable below Stable while numeric verification waits stay unchanged",()=>
{
    var cfg=Settings.Defaults();cfg.Set("fast_transfer",true);cfg.Set("input_frame_delay_ms",16);
    double numeric=StrokeTiming.SliderChangeEstimate(cfg),hex=StrokeTiming.HexChangeEstimate(cfg);
    var batch=new PaintBatch(0,new[]{new ScreenLine(0,0,512,0)},1);double stable=TransferSchedule.EstimateBatch(cfg,SpeedProfile.Get("Max Speed"),batch);
    cfg.Set("input_engine","Experimental 1 ms");Assert(StrokeTiming.Frame(cfg)==.012);
    double previous=double.PositiveInfinity;
    foreach(int ms in new[]{16,12,8})
    {
        cfg.Set("input_experimental_delay_ms",ms);cfg.Validate();Assert(StrokeTiming.Frame(cfg)==ms/1000.0);
        Assert(StrokeTiming.ControlFrame(cfg)==.016&&StrokeTiming.SliderChangeEstimate(cfg)==numeric&&StrokeTiming.HexChangeEstimate(cfg)==hex);
        var input=new RecordingStrokeInput();StrokeMotion.Draw(batch,cfg,SpeedProfile.Get("Max Speed"),input);
        double estimated=TransferSchedule.EstimateBatch(cfg,SpeedProfile.Get("Max Speed"),batch);
        Assert(Math.Abs(input.Seconds-estimated)<1e-9&&estimated<previous);previous=estimated;
        Assert(input.Painted.SetEquals(Centres(batch.Segments))&&!input.Held&&input.UpAt-input.DownAt>=.04-1e-9);
    }
    Assert(previous<stable);
    cfg.Set("input_engine","Stable");Assert(StrokeTiming.Frame(cfg)==.016);
});
Test("Experimental timing rejects invalid persisted values and changes checkpoint identity",()=>
{
    var cfg=Settings.Defaults();var image=Fixture();string identity=PlanIdentity.Compute(image,cfg,[]);
    cfg.Set("input_experimental_delay_ms",8);Assert(PlanIdentity.Compute(image,cfg,[])!=identity);
    foreach(string value in new[]{"0","7","17","NaN","Infinity"})
    {
        cfg.Set("input_experimental_delay_ms",value);
        try{cfg.Validate();throw new Exception("Invalid experimental delay accepted");}catch(InvalidDataException){}
    }
});
Test("Single-row edge gaps remain failures and are reported even when no repair fits",()=>
{
    var before=new PixelImage(80,80);var after=before.Clone();var mask=new bool[6400];
    for(int y=10;y<70;y++)for(int x=10;x<70;x++){int i=y*80+x;mask[i]=true;if(y>10)after.Set(i,new(220,40,30));}
    var result=CoverageAudit.Read(before,after,mask,new(new(220,40,30),12));
    Assert(result.Missing==60&&!result.Passed&&result.Unknown==0);
    var canvas=new ScreenRect(100,200,180,280);Assert(CoverageAudit.GapBounds(result.MissingMask,canvas)==new ScreenRect(110,210,170,211));
    Assert(CoverageAudit.Repair(result.MissingMask,mask,canvas,7).Count==0);
    Assert(CoverageAudit.GapBounds(new bool[6400],canvas) is null);
});

Console.WriteLine($"ALL {passed} TESTS PASSED");

sealed class RecordingStrokeInput : ICalibratedStrokeInput
{
    public double Seconds { get; private set; }
    public ScreenPoint Cursor { get; private set; }
    public bool Held { get; private set; }
    public bool ShiftHeld { get; private set; }
    public int ShiftDowns { get; private set; }
    public int ShiftUps { get; private set; }
    public void Shift(bool up){ShiftHeld=!up;if(up)ShiftUps++;else ShiftDowns++;}
    public int Downs { get; private set; }
    public int Ups { get; private set; }
    public double DownAt { get; private set; }
    public double UpAt { get; private set; }
    public bool FailHeldMove { get; init; }
    public bool FailHeldWait { get; init; }
    public bool FailShiftWait { get; init; }
    public HashSet<ScreenPoint> Painted { get; }=[];
    public List<ScreenPoint> HeldMoves { get; }=[];
    public List<ScreenPoint[]> Packets { get; }=[];
    public List<double> WaitDurations { get; }=[];
    public void Move(IReadOnlyList<ScreenPoint> points)
    {
        if(Held&&FailHeldMove)throw new OperationCanceledException("Synthetic input interruption");
        Packets.Add(points.ToArray());
        foreach(var point in points){Cursor=point;if(Held){Painted.Add(point);HeldMoves.Add(point);}}
    }
    public void Button(bool up)
    {
        if(up){Held=false;Ups++;UpAt=Seconds;}
        else{Held=true;Downs++;DownAt=Seconds;Painted.Add(Cursor);HeldMoves.Add(Cursor);}
    }
    public void Wait(double seconds)
    {
        if(Held&&FailHeldWait)throw new OperationCanceledException("Synthetic pause");
        if(ShiftHeld&&!Held&&FailShiftWait)throw new OperationCanceledException("Synthetic key pause");
        WaitDurations.Add(seconds);Seconds+=seconds;
    }
}
