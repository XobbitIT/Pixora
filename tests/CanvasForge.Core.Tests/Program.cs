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
Test("Ukrainian messages translate owned English failures without changing user data", () =>
{
    var examples = new Dictionary<string,string>
    {
        ["Capture Canvas."] = "Захопи полотно.",
        ["Canvas is outside Rust."] = "Полотно розташоване поза вікном Rust.",
        ["Cannot open clipboard."] = "Не вдалося відкрити буфер обміну.",
        ["Unsupported image format."] = "Формат зображення не підтримується.",
        ["Input frame delay must be 16–100 ms."] = "Затримка стабільного вводу має бути від 16 до 100 мс.",
        ["Experimental input delay must be 8–16 ms."] = "Затримка експериментального вводу має бути від 8 до 16 мс.",
        ["Invalid setting: input_engine"] = "Некоректне налаштування: input_engine"
    };
    foreach(var (en,uk) in examples)
    {
        Assert(CanvasForge.App.Translations.ForLanguage(en,false)==uk,en);
        Assert(CanvasForge.App.Translations.ForLanguage(uk,true)==en,uk);
    }
    foreach(var text in new[]{"C:\\малюнки\\Photo $1.png","My image — малюнок.png","Example.exe"})
        foreach(bool english in new[]{false,true})Assert(CanvasForge.App.Translations.ForLanguage(text,english)==text,"User data was translated: "+text);
});
Test("Dynamic localization preserves captures and translates cached nested failures", () =>
{
    const string uk="Покриття: 99.1%; пропуски 336; невпевнено 823.";
    const string en="Coverage: 99.1%; missing 336; uncertain 823.";
    Assert(CanvasForge.App.Translations.ForLanguage(uk,true)==en);
    Assert(CanvasForge.App.Translations.ForLanguage(en,false)==uk);
    const string detail="C:\\mal\\$1\\image.png\nsecond line";
    Assert(CanvasForge.App.Translations.ForLanguage("Invalid setting: "+detail,false)=="Некоректне налаштування: "+detail,"Template changed backslashes, dollars, or newlines");
    const string failure="Could not measure Size 3: Cannot resize image.";
    const string localized="Не вдалося виміряти розмір 3: Не вдалося змінити розмір зображення.";
    Assert(CanvasForge.App.Translations.ForLanguage(failure,false)==localized);
    Assert(CanvasForge.App.Translations.ForLanguage(localized,true)==failure);
});
Test("Option labels localize without exposing legacy canonical UI names", () =>
{
    foreach(var (key,value) in new[]{("input_engine","Stable"),("input_engine","Experimental 1 ms"),("speed_profile","Max Speed"),("profile","Anime / Line Art"),("profile","Photo"),("profile","Pixel Art"),("color_mode","HEX Direct"),("fit_mode","smart"),("background_mode","auto"),("coverage_mode","Precision"),("max_colors","Auto"),("hex_max_colors","Auto")})
    {
        var uk=CanvasForge.App.Translations.Option(key,value,false);
        Assert(uk!=value&&System.Text.RegularExpressions.Regex.IsMatch(uk,"[А-Яа-яІіЇїЄєҐґ]"),"Missing Ukrainian option: "+key+" / "+value);
        Assert(!System.Text.RegularExpressions.Regex.IsMatch(CanvasForge.App.Translations.Option(key,value,true),"[А-Яа-яІіЇїЄєҐґ]"),"Missing English option");
    }
    Assert(CanvasForge.App.Translations.Option("probe_size","3",false)=="3");
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
    foreach (var key in new[] { "smooth_preview", "auto_insert_preview", "transfer_simulator", "minimize", "restore_window_after_paint" })
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
    var spatial=SpatialFixtureProfile(cfg,1);ProbeSpatialCalibration.Save(cfg,spatial);
    var samples=new List<SpeedSample>{new(1,StrokeMethod.Paced,false,12,17,1,32,3,1,spatial.Id),new(1,StrokeMethod.Shift,false,8,12,1,32,3,1,spatial.Id),new(1,StrokeMethod.Paced,true,20,27,1,32,3,1,spatial.Id)};
    cfg.Set("speed_probe_profile",new SpeedProbeProfile(SpeedCalibration.Context(cfg),DateTimeOffset.UtcNow,samples));return cfg;
}
Test("Probe accepts repeated full coverage and rejects unvalidated profiles",()=>
{
    var cfg=ProbeConfig();Assert(SpeedCalibration.Use(cfg));
    foreach(var invalid in new[]{new SpeedSample(1,StrokeMethod.Shift,false,8,8,1,32,3,1),new(1,StrokeMethod.Shift,false,8,12,1,32,2,1),new(1,StrokeMethod.Shift,false,8,12,1,32,3,.99),new(1,StrokeMethod.Paced,false,8,12,64,32,3,1)})
    {cfg.Set("speed_probe_profile",new SpeedProbeProfile(SpeedCalibration.Context(cfg),DateTimeOffset.UtcNow,[invalid with{SpatialId=ProbeSpatialCalibration.Read(cfg,1)!.Id}]));Assert(!SpeedCalibration.Current(cfg));}
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
    var tiles=SpeedCalibration.Tiles(new(20,30,1220,1230),24);Assert(tiles.Count==62);
    foreach(var t in tiles)Assert(t.Area.Left>=20&&t.Area.Top>=30&&t.Area.Right<=1220&&t.Area.Bottom<=1230);
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
Test("Single-row edge gaps project to safe interior centres without being declared covered",()=>
{
    var before=new PixelImage(80,80);var after=before.Clone();var mask=new bool[6400];
    for(int y=10;y<70;y++)for(int x=10;x<70;x++){int i=y*80+x;mask[i]=true;if(y>10)after.Set(i,new(220,40,30));}
    var result=CoverageAudit.Read(before,after,mask,new(new(220,40,30),12));
    Assert(result.Missing==60&&!result.Passed&&result.Unknown==0);
    var canvas=new ScreenRect(100,200,180,280);Assert(CoverageAudit.GapBounds(result.MissingMask,canvas)==new ScreenRect(110,210,170,211));
    var repair=CoverageAudit.PlanRepair(result.MissingMask,mask,canvas,7);
    Assert(repair.TargetPixels==60&&repair.UnreachablePixels==0&&repair.Strokes.Count>0);
    foreach(var point in Centres(repair.Strokes))
    {
        Assert(point.Y==217,"Thin top-edge gap was not projected inward");
        for(int dy=-7;dy<=7;dy++)for(int dx=-7;dx<=7;dx++)Assert(mask[(point.Y-200+dy)*80+point.X-100+dx]);
    }
    var unchanged=CoverageAudit.Read(before,after,mask,result.Reference);
    Assert(!unchanged.Passed&&unchanged.Missing==60&&unchanged.Unknown==0,"Planned repair fabricated a PASS");
    Assert(CoverageAudit.GapBounds(new bool[6400],canvas) is null);
});
Test("Repair cannot target an uncertain-only result or a color region smaller than the footprint",()=>
{
    var before=new PixelImage(20,20);var expected=Enumerable.Repeat(true,400).ToArray();
    var unknown=CoverageAudit.Read(before,before,expected);
    var empty=CoverageAudit.PlanRepair(unknown.MissingMask,expected,new(0,0,20,20),2);
    Assert(unknown.Unknown==400&&!unknown.Passed&&empty.Strokes.Count==0&&empty.TargetPixels==0);
    Array.Fill(expected,false);var gaps=new bool[400];
    for(int y=3;y<=5;y++)for(int x=3;x<=5;x++)expected[y*20+x]=gaps[y*20+x]=true;
    var small=CoverageAudit.PlanRepair(gaps,expected,new(0,0,20,20),2);
    Assert(small.TargetPixels==9&&small.UnreachablePixels==9&&small.Strokes.Count==0);
});
Test("Projected repair matches brute-force reach and keeps every moving footprint inside its color",()=>
{
    var random=new Random(2505);const int w=18,h=16;
    for(int trial=0;trial<120;trial++)
    {
        int radius=trial%4;var expected=new bool[w*h];var gaps=new bool[w*h];
        for(int i=0;i<expected.Length;i++){expected[i]=random.NextDouble()>.08;gaps[i]=random.NextDouble()<.25;}
        var safe=new List<ScreenPoint>();
        for(int y=radius;y<h-radius;y++)for(int x=radius;x<w-radius;x++)
        {
            bool inside=true;
            for(int dy=-radius;dy<=radius;dy++)for(int dx=-radius;dx<=radius;dx++)inside&=expected[(y+dy)*w+x+dx];
            if(inside)safe.Add(new(x,y));
        }
        var originalGaps=(bool[])gaps.Clone();var originalExpected=(bool[])expected.Clone();
        var plan=CoverageAudit.PlanRepair(gaps,expected,new(-100,200,-100+w,200+h),radius);
        int targets=0,unreachable=0;
        for(int i=0;i<gaps.Length;i++)if(gaps[i]&&expected[i])
        {
            targets++;if(!safe.Any(p=>Math.Max(Math.Abs(p.X-i%w),Math.Abs(p.Y-i/w))<=radius))unreachable++;
        }
        Assert(plan.TargetPixels==targets&&plan.UnreachablePixels==unreachable,"Distance transform differs from exhaustive search");
        var centres=Centres(plan.Strokes).Select(p=>new ScreenPoint(p.X+100,p.Y-200)).ToArray();
        Assert(centres.All(p=>safe.Contains(p)),"A regrouped repair moves through another color");
        for(int i=0;i<gaps.Length;i++)if(gaps[i]&&expected[i]&&safe.Any(p=>Math.Max(Math.Abs(p.X-i%w),Math.Abs(p.Y-i/w))<=radius))
            Assert(centres.Any(p=>Math.Max(Math.Abs(p.X-i%w),Math.Abs(p.Y-i/w))<=radius),"Reachable target lost during regrouping");
        Assert(gaps.SequenceEqual(originalGaps)&&expected.SequenceEqual(originalExpected),"Planning changed the evidence masks");
    }
});
Test("Repair validates geometry and enforces the bounded operation limit",()=>
{
    foreach(var operation in new Action[]{()=>CoverageAudit.PlanRepair(new bool[9],new bool[9],new(0,0,3,3),-1),
        ()=>CoverageAudit.PlanRepair(new bool[8],new bool[9],new(0,0,3,3),1),
        ()=>CoverageAudit.PlanRepair(new bool[9],new bool[9],new(0,0,3,3),1,0)})
    {try{operation();throw new Exception("Invalid repair accepted");}catch(ArgumentException){}}
    var gaps=new bool[400];gaps[42]=gaps[315]=true;
    try{CoverageAudit.PlanRepair(gaps,Enumerable.Repeat(true,400).ToArray(),new(0,0,20,20),0,1);throw new Exception("Repair limit ignored");}
    catch(InvalidOperationException){}
});
Test("Enlarged gap markers are visible on red paint and never alter audit pixels or masks",()=>
{
    var after=new PixelImage(12,10);for(int i=0;i<120;i++)after.Set(i,new(255,40,70));
    var missing=new bool[120];missing[0]=missing[5*12+6]=true;
    var overlay=CoverageAudit.GapOverlay(after,missing);
    Assert(overlay.Color(5*12+6)==new Rgb(0,255,255)&&overlay.Color(3*12+6)==new Rgb(0,0,0));
    Assert(overlay.Color(0)==new Rgb(0,255,255)&&overlay.Color(119)==new Rgb(255,40,70));
    Assert(after.Color(0)==new Rgb(255,40,70)&&missing.Count(x=>x)==2,"Diagnostics changed audit evidence");
});

Test("Auto brush reads legacy and current calibration using outer diameter", () =>
{
    foreach(var points in new[]{new double[][]{[1,3],[10,21],[20,43]},new double[][]{[1,3,1],[10,21,13],[20,43,23]}})
    {
        var s=Settings.Defaults();s.Set("brush_calibration_points",points);
        Assert(AutomaticBrush.Value(s,21)==10,"21 px must use calibrated Size 10, not Size 21");
        Assert(AutomaticBrush.Value(s,12)==5.5,"Outer diameter interpolation differs between formats");
        Assert(AutomaticBrush.Value(s,1)==1&&AutomaticBrush.Value(s,100)==20);
    }
});
Test("Auto brush rejects stale malformed and nonmonotonic measurements", () =>
{
    var s=Settings.Defaults();s.Data["brush_calibration_points"]=JsonNode.Parse("[[1,3,1], [10,21,13], [null,9,1], [20,9,99], [\"bad\",15], [], [1,3,1]]");
    Assert(AutomaticBrush.Value(s,21)==10,"Bad rows discarded valid calibration");
    s.Set("brush_calibration_context","v2:stale");Assert(AutomaticBrush.Value(s,21)==21,"Stale measurements were used");
    s.Set("brush_calibration_context","");s.Set("brush_calibration_points",new double[][]{[10,3,1],[1,21,13]});
    Assert(AutomaticBrush.Value(s,21)==21,"Nonmonotonic measurements were used");
    s.Set("brush_calibration_points",new double[][]{});Assert(AutomaticBrush.Value(s,1000)==100);
});
Test("Checkpoint commits every completed batch and flushes exact interrupted progress", () =>
{
    var folder=Path.Combine(Path.GetTempPath(),"pixora-checkpoint-"+Guid.NewGuid());Directory.CreateDirectory(folder);
    string path=Path.Combine(folder,"resume.json");var counts=new[]{138,138,138,138};
    try
    {
        var journal=new CheckpointJournal(path,new("plan",0,0,0));journal.Flush();
        foreach(int target in new[]{49,50,137,138,253,552})
        {
            for(int done=journal.Latest.Done+1;done<=target;done++)
                journal.Update(new("plan",done/138,done%138,done));
            Assert(journal.Latest.Done==target);journal.Flush();
            var saved=System.Text.Json.JsonSerializer.Deserialize<ResumeCheckpoint>(File.ReadAllText(path))!;
            Assert(saved.Done==target&&saved.Matches("plan",counts),"STOP/F6/error would resume from an earlier checkpoint");
            Assert(!journal.Flush(),"Unchanged checkpoint was written repeatedly");
        }
    }
    finally{foreach(var file in Directory.GetFiles(folder))File.Delete(file);Directory.Delete(folder);}
});
Test("Checkpoint write failure preserves previous JSON and allows a later retry", () =>
{
    if(!OperatingSystem.IsWindows())return;
    var folder=Path.Combine(Path.GetTempPath(),"pixora-checkpoint-"+Guid.NewGuid());Directory.CreateDirectory(folder);
    string path=Path.Combine(folder,"resume.json");
    try
    {
        var journal=new CheckpointJournal(path,new("plan",0,0,0));journal.Flush();
        journal.Update(new("plan",0,49,49));
        using(var locked=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            try{journal.Flush();throw new Exception("Locked checkpoint was overwritten");}catch(Exception e) when(e is IOException or UnauthorizedAccessException){}
            Assert(System.Text.Json.JsonSerializer.Deserialize<ResumeCheckpoint>(File.ReadAllText(path))!.Done==0);
        }
        Assert(journal.Latest.Done==49&&journal.Flush());
        Assert(System.Text.Json.JsonSerializer.Deserialize<ResumeCheckpoint>(File.ReadAllText(path))!.Done==49);
        Assert(!File.Exists(path+".tmp"));
    }
    finally{foreach(var file in Directory.GetFiles(folder))File.Delete(file);Directory.Delete(folder);}
});
Test("Resume validation rejects another plan inconsistent counters and invalid boundaries", () =>
{
    var counts=new[]{138,138,138,138};
    foreach(var state in new[]{new ResumeCheckpoint("plan",0,49,49),new("plan",1,0,138),new("plan",0,138,138),new("plan",4,0,552)})
        Assert(state.Matches("plan",counts));
    foreach(var state in new[]{new ResumeCheckpoint("other",0,49,49),new("plan",0,49,0),new("plan",0,139,139),new("plan",4,1,553),new("plan",-1,0,0),new("plan",5,0,552)})
        Assert(!state.Matches("plan",counts));
    Assert(!new ResumeCheckpoint("plan",0,0,0).Matches("plan",new[]{-1}));
});
Test("Retired controls preserve stored data without affecting plan identity", () =>
{
    var s=Config(ColorMode.RustPalette);var image=Fixture();var entries=new[]{new PaletteEntry(new(0,0,0),new(0,0),"main")};
    var identity=PlanIdentity.Compute(image,s,entries);
    s.Set("sequence_delay_ms",20);s.Set("double_click_controls",false);s.Set("control_verify_tolerance",.99);
    s.Validate();Assert(PlanIdentity.Compute(image,s,entries)==identity);
    Assert(s.Int("sequence_delay_ms")==20&&!s.Bool("double_click_controls")&&s.Number("control_verify_tolerance")==.99);
});
Test("Probe separates mixed brush edges from fixed solid core without loosening audit",()=>
{
    var (before,after,line)=ProbeFixture();var result=ProbeAnalysis.Control(before,after,line,7,2);
    Assert(result.Passed&&result.CoreCoverage.Expected==140&&result.CoreCoverage.Covered==140);
    Assert(result.FullMeasurement.Failure==ReferenceFailure.NonUniformColor);
    Assert(CoverageAudit.Learn(before,after,result.ChangedMask) is null,"Full audit was silently relaxed");
    Assert(result.CoreMeasurement.BackgroundRgb==new Rgb(200,200,200)&&result.CoreMeasurement.StrokeRgb==new Rgb(0,0,0));
    Assert(result.CoreMeasurement.Contrast==200&&result.CoreMeasurement.Uniformity==1&&result.CoreMeasurement.SampleCount==140);
});
Test("Reference measurement distinguishes sample count low contrast and mixed color",()=>
{
    var before=new PixelImage(10,10);for(int i=0;i<100;i++)before.Set(i,new(200,200,200));
    var after=before.Clone();for(int i=0;i<100;i++)after.Set(i,new(190,190,190));
    var mask=Enumerable.Repeat(true,100).ToArray();var low=CoverageAudit.MeasureReference(before,after,mask,true);
    Assert(low.Failure==ReferenceFailure.LowContrast&&low.Contrast==10&&low.StrokeRgb==new Rgb(190,190,190));
    Assert(low.Reference is null&&low.SampleCount==100&&low.ChangedSamples==0);
    Array.Fill(mask,false);for(int i=0;i<7;i++){mask[i]=true;after.Set(i,new(0,0,0));}
    Assert(CoverageAudit.MeasureReference(before,after,mask,true).Failure==ReferenceFailure.InsufficientSamples);
    Array.Fill(mask,true);for(int i=0;i<100;i++)after.Set(i,i<50?new(0,0,0):new(200,0,0));
    Assert(CoverageAudit.MeasureReference(before,after,mask,true).Failure==ReferenceFailure.NonUniformColor);
});
Test("Probe rejects missing longitudinal slices instead of fitting its mask to changed pixels",()=>
{
    var (before,after,line)=ProbeFixture();for(int y=43;y<=53;y++)after.Set(y*96+48,before.Color(y*96+48));
    var result=ProbeAnalysis.Control(before,after,line,7,2);
    Assert(!result.Passed&&result.Failure==ProbeFailure.LongitudinalGap&&result.LongitudinalGaps==1);
    Assert(result.CoreCoverage.Expected==140&&result.CoreCoverage.Missing==5);
});
Test("Thin probe control allows fixed raster offset while trials cannot recenter",()=>
{
    var (before,_,line)=ProbeFixture();var after=before.Clone();for(int x=30;x<=65;x++)after.Set(49*96+x,new(0,0,0));
    var control=ProbeAnalysis.Control(before,after,line,7,0);
    Assert(control.Passed&&control.PerpendicularOffset==1&&control.CoreCoverage.Expected==28);
    Assert(ProbeAnalysis.Trial(before,after,line,control).Passed);
    var shifted=before.Clone();for(int x=30;x<=65;x++)shifted.Set(50*96+x,new(0,0,0));
    var result=ProbeAnalysis.Trial(before,shifted,line,control);
    Assert(!result.Passed&&result.CoreCoverage.Missing==28&&result.PerpendicularOffset==1);
    Assert(result.CoreMask.SequenceEqual(control.CoreMask),"Trial changed the expected mask");
});
Test("Probe trials retain control color and reject holes and a different solid color",()=>
{
    var (before,after,line)=ProbeFixture();var control=ProbeAnalysis.Control(before,after,line,7,2);
    var wrong=after.Clone();for(int i=0;i<control.CoreMask.Length;i++)if(control.CoreMask[i])wrong.Set(i,new(0,255,0));
    var result=ProbeAnalysis.Trial(before,wrong,line,control);
    Assert(!result.Passed&&result.CoreCoverage.Unknown==140&&result.CoreCoverage.Reference==control.CoreCoverage.Reference);
    after.Set(48*96+48,before.Color(48*96+48));result=ProbeAnalysis.Trial(before,after,line,control);
    Assert(!result.Passed&&result.CoreCoverage.Missing==1&&result.CoreCoverage.Expected==140);
});
Test("Probe scene-change check uses the whole physical brush region",()=>
{
    var (before,after,line)=ProbeFixture();var control=ProbeAnalysis.Control(before,after,line,7,2);
    Assert(control.Passed&&control.OutsideChanged==0,"Normal brush edges were mistaken for camera movement");
    for(int y=0;y<2;y++)for(int x=0;x<96;x++)after.Set(y*96+x,new(0,0,0));
    var result=ProbeAnalysis.Trial(before,after,line,control);
    Assert(!result.Passed&&result.Failure==ProbeFailure.SceneChanged&&result.CoreCoverage.Unknown==140);
});
Test("Wide Size 20 probe retains geometric inner strip on a short control tile",()=>
{
    var (before,_,line)=ProbeFixture();var after=before.Clone();
    for(int y=35;y<=61;y++)for(int x=30;x<=65;x++)after.Set(y*96+x,new(0,0,0));
    var control=ProbeAnalysis.Control(before,after,line,24,10);
    Assert(control.Passed&&control.CoreCoverage.Expected==588&&control.CoreCoverage.Covered==588);
    after.Set(48*96+48,new(100,100,100));var result=ProbeAnalysis.Trial(before,after,line,control);
    Assert(!result.Passed&&result.CoreCoverage.Unknown==1,"Uncertain inner pixel accepted");
});
Test("Probe rejects reuse of a control with mismatched geometry even with equal pixel counts",()=>
{
    var (before,after,line)=ProbeFixture();var control=ProbeAnalysis.Control(before,after,line,7,2);
    foreach(var operation in new Action[]{()=>ProbeAnalysis.Trial(new(128,72),new(128,72),line,control),
        ()=>ProbeAnalysis.Trial(before,after,new(48,30,48,65),control)})
    {try{operation();throw new Exception("Geometry mismatch accepted");}catch(ArgumentException){}}
});
Test("Probe mask sampling preserves thin lines independently of full-image stride",()=>
{
    var before=new PixelImage(1000,1000);var after=before.Clone();var mask=new bool[1_000_000];
    for(int x=30;x<=65;x++){int i=49*1000+x;mask[i]=true;after.Set(i,new(255,0,0));}
    Assert(CoverageAudit.MeasureReference(before,after,mask,true).ChangedSamples==36);
    Assert(CoverageAudit.MeasureReference(before,after,mask,true).Reference is not null);
    Assert(CoverageAudit.Learn(before,after,mask) is null,"Existing audit sampling behavior changed");
});
Test("Snapshot parks at the nearest clear capture edge within the game window",()=>
{
    var region=new ScreenRect(100,100,400,400);var window=new ScreenRect(0,0,800,600);
    Assert(CaptureCursor.ParkingPoint(region,window,new(105,250),20)==new ScreenPoint(80,250));
    Assert(CaptureCursor.ParkingPoint(region,window,new(250,395),20)==new ScreenPoint(250,420));
    Assert(CaptureCursor.ParkingPoint(region,window,new(500,250),20)==new ScreenPoint(500,250));
    Assert(CaptureCursor.ParkingPoint(new(-500,100,-200,400),new(-800,0,0,600),new(-495,250),20)==new ScreenPoint(-520,250));
    Assert(CaptureCursor.ParkingPoint(window,window,new(200,200),20) is null);
});
Test("Capture clearance separates a small brush halo from the snapshot and scales with DPI",()=>
{
    Assert(CaptureCursor.Clearance(7)==96&&CaptureCursor.Clearance(7,192)==192);
    Assert(CaptureCursor.Clearance(258)==270&&CaptureCursor.Clearance(258,192)==282);
    var region=new ScreenRect(100,100,400,400);var window=new ScreenRect(0,0,800,600);
    var parked=CaptureCursor.ParkingPoint(region,window,new(105,250),CaptureCursor.Clearance(7));
    Assert(parked==new ScreenPoint(4,250)&&parked.Value.X+40<region.Left,"Recorded 40 px halo could intersect the tile");
    Assert(CaptureCursor.ParkingPoint(new(30,30,170,170),new(0,0,200,200),new(40,40),CaptureCursor.Clearance(7)) is null,
        "A tight window was allowed to capture a contaminated region");
});
Test("Recorded beta24 halo contamination remains SceneChanged without relaxing probe acceptance",()=>
{
    var (before,_)=SliderFixture("beta24-probe-halo-before");var (after,_)=SliderFixture("beta24-probe-halo-after");
    var result=ProbeAnalysis.SpatialControl(before,after,new(13,32,50,32),7,0);
    Assert(!result.Passed&&result.Failure==ProbeFailure.SceneChanged&&result.CoreCoverage.Unknown==30);
    Assert(result.OutsideChanged==286&&result.OutsidePixels==3316&&result.PerpendicularOffset==3,
        "Live scene contamination was silently reclassified");
});
Test("Snapshot releases input before temporary moves and restores the last action",()=>
{
    ScreenPoint original=new(200,200),parked=new(80,200),cursor=original;bool held=true;var moves=new List<ScreenPoint>();
    int releases=0;void Release(){held=false;releases++;}void Move(ScreenPoint point){Assert(!held,"Cursor moved while painting");cursor=point;moves.Add(point);}
    int result=CaptureCursor.Snapshot(original,parked,Release,Move,()=>{Assert(cursor==parked&&!held);return 42;},()=>true);
    Assert(result==42&&cursor==original&&!held&&releases==2&&moves.SequenceEqual(new[]{parked,original}));
});
Test("A cursor already clear of the snapshot is left in place without extra moves",()=>
{
    var current=new ScreenPoint(500,250);int moves=0,releases=0;
    Assert(CaptureCursor.Snapshot(current,current,()=>releases++,_=>moves++,()=>42,()=>true)==42);
    Assert(moves==0&&releases==0);
});
Test("Snapshot retains the original capture exception even when cursor return fails",()=>
{
    ScreenPoint original=new(200,200),parked=new(80,200);var captureError=new InvalidOperationException("unstable frames");
    Exception? restoreError=null;
    try
    {
        CaptureCursor.Snapshot<int>(original,parked,()=>{},point=>{if(point==original)throw new IOException("cursor move failed");},
            ()=>throw captureError,()=>true,error=>restoreError=error);
        throw new Exception("Capture failure hidden");
    }
    catch(InvalidOperationException error){Assert(ReferenceEquals(error,captureError));}
    Assert(restoreError is IOException);
});
Test("Snapshot does not reclaim cursor after focus cancellation or manual movement",()=>
{
    ScreenPoint original=new(200,200),parked=new(80,200),userPoint=new(700,500),cursor=original;
    var moves=new List<ScreenPoint>();void Move(ScreenPoint point){cursor=point;moves.Add(point);}
    CaptureCursor.Snapshot(original,parked,()=>{},Move,()=>{cursor=userPoint;return 42;},()=>cursor==parked);
    Assert(cursor==userPoint&&moves.SequenceEqual(new[]{parked}));
    moves.Clear();CaptureCursor.Snapshot(original,parked,()=>{},Move,()=>42,()=>false);
    Assert(cursor==parked&&moves.SequenceEqual(new[]{parked}));
});
Test("Completion cursor return uses no click and respects interruption predicate",()=>
{
    var last=new ScreenPoint(200,200);bool held=true;int releases=0,moves=0;
    bool Return(bool allowed)=>CaptureCursor.Return(last,()=>{held=false;releases++;},point=>{Assert(point==last&&!held);moves++;},()=>allowed);
    Assert(!Return(false)&&moves==0&&releases==0);
    Assert(Return(true)&&moves==1&&releases==1&&!held);
});
Test("Snapshot aborts before moving or capturing when input release fails",()=>
{
    int moves=0,captures=0;var failure=new IOException("mouse-up rejected");
    try
    {
        CaptureCursor.Snapshot(new(200,200),new(80,200),()=>throw failure,_=>moves++,()=>++captures,()=>true);
        throw new Exception("Input release failure hidden");
    }
    catch(IOException error){Assert(ReferenceEquals(error,failure));}
    Assert(moves==0&&captures==0,"Cursor moved while mouse-up was unconfirmed");
});
Test("Spatial slow control chooses the darkest verified center rather than the first AA edge",()=>
{
    var (before,_,line)=ProbeFixture();var after=before.Clone();
    for(int x=30;x<=65;x++){after.Set(49*96+x,new(80,80,80));after.Set(50*96+x,new(20,20,20));}
    var slow=ProbeAnalysis.SpatialControl(before,after,line,7,0);
    Assert(slow.Passed&&slow.PerpendicularOffset==2&&slow.CoreCoverage.Reference!.Color==new Rgb(20,20,20));
    Assert(ProbeAnalysis.Control(before,after,line,7,0).PerpendicularOffset==1,"Legacy control changed silently");
});
Test("Spatial layout spreads six non-overlapping controls over the real small Canvas",()=>
{
    foreach(int outer in new[]{8,10,29})
    {
        var canvas=new ScreenRect(676,428,1306,901);var tiles=ProbeSpatialCalibration.Tiles(canvas,outer);
        Assert(tiles.Count==6&&tiles.Take(3).Select(x=>x.Horizontal.Y1).Distinct().Count()==3&&tiles.Skip(3).Select(x=>x.Vertical.X1).Distinct().Count()==3);
        foreach(var t in tiles)Assert(t.Area.Left>=canvas.Left&&t.Area.Top>=canvas.Top&&t.Area.Right<=canvas.Right&&t.Area.Bottom<=canvas.Bottom);
        for(int i=0;i<6;i++)for(int j=i+1;j<6;j++)Assert(tiles[i].Area.Right<=tiles[j].Area.Left||tiles[j].Area.Right<=tiles[i].Area.Left||tiles[i].Area.Bottom<=tiles[j].Area.Top||tiles[j].Area.Bottom<=tiles[i].Area.Top);
    }
});
Test("Spatial model requires six complete controls at distinct positions",()=>
{
    var cfg=SessionFixture();var (before,_,line)=ProbeFixture();
    var controls=new List<(ScreenLine Requested,ProbeAnalysisResult Result)>();
    foreach(bool vertical in new[]{false,true})for(int n=0;n<3;n++)
    {
        var local=vertical?new ScreenLine(48,30,48,65):line;var after=SpatialStroke(before,local,n-1,0);
        var result=ProbeAnalysis.SpatialControl(before,after,local,7,0);
        controls.Add((vertical?new(130+n*50,220,130+n*50,255):new(120,220+n*40,155,220+n*40),result));
    }
    var model=ProbeSpatialCalibration.Build(cfg,1,controls);ProbeSpatialCalibration.Save(cfg,model);
    Assert(ProbeSpatialCalibration.Read(cfg,1) is not null&&model.Axes.All(x=>x.Offsets.SequenceEqual(new[]{-1,0,1})));
    foreach(var bad in new[]{controls.Take(5).ToList(),controls.Select(x=>(controls[0].Requested,x.Result)).ToList()})
    {try{ProbeSpatialCalibration.Build(cfg,1,bad);throw new Exception("Incomplete geometry accepted");}catch(InvalidOperationException){}}
});
Test("Spatial persistence rejects moved geometry DPI and corrupted models",()=>
{
    var cfg=ProbeConfig();Assert(ProbeSpatialCalibration.Read(cfg,1) is not null);
    var changed=cfg.Clone();var cal=changed.Calibration;cal.SetRect("canvas",new(101,200,301,350));changed.SetCalibration(cal);
    Assert(ProbeSpatialCalibration.Read(changed,1) is null&&!SpeedCalibration.Current(changed));
    changed=cfg.Clone();cal=changed.Calibration;cal.SetSession(new(1,0),96,new(2000,1200));changed.SetCalibration(cal);Assert(ProbeSpatialCalibration.Read(changed,1) is null);
    foreach(string json in new[]{"null","[null]","{}","[{}]"})
    {changed=cfg.Clone();changed.Data["probe_spatial_profiles"]=System.Text.Json.Nodes.JsonNode.Parse(json);Assert(ProbeSpatialCalibration.Read(changed,1) is null);}
});
Test("Speed routes cannot reuse a missing or replaced spatial model",()=>
{
    var cfg=ProbeConfig();var old=SpeedCalibration.Read(cfg)!;
    cfg.Data.Remove("probe_spatial_profiles");Assert(!SpeedCalibration.Current(cfg));
    ProbeSpatialCalibration.Save(cfg,SpatialFixtureProfile(cfg,1));Assert(!SpeedCalibration.Current(cfg));
    var id=ProbeSpatialCalibration.Read(cfg,1)!.Id;cfg.Set("speed_probe_profile",old with{Samples=old.Samples.Select(x=>x with{SpatialId=id}).ToList()});
    Assert(SpeedCalibration.Current(cfg));
    cfg.Set("speed_probe_profile",old with{Context="probe-solid-core-diagnostics-v2"});Assert(!SpeedCalibration.Current(cfg));
});
Test("Spatial trial freezes a bounded integer envelope and color before speed trials",()=>
{
    var (before,_,line)=ProbeFixture();var axis=SpatialTestAxis(false,0,1,3);var reference=new AuditReference(new(20,20,20),12);
    var a=ProbeSpatialCalibration.Trial(before,SpatialStroke(before,line,1,0),line,7,axis,reference);
    var b=ProbeSpatialCalibration.Trial(before,SpatialStroke(before,line,3,0),line,7,axis,reference);
    Assert(a.Passed&&b.Passed&&a.CoreCoverage.Expected==28&&a.CoreCoverage.Covered==28);
    Assert(a.CoreMask.SequenceEqual(b.CoreMask)&&a.CoreCoverage.Reference==reference&&b.CoreCoverage.Reference==reference);
    var intermediate=ProbeSpatialCalibration.Trial(before,SpatialStroke(before,line,2,0),line,7,axis,reference);
    Assert(intermediate.Passed&&intermediate.CoreMask.SequenceEqual(a.CoreMask)&&axis.Offsets.SequenceEqual(new[]{1,3})
        &&axis.AllowedOffsets.SequenceEqual(new[]{1,2,3}));
});
Test("Spatial fast trial cannot recenter to a solid line outside its frozen model",()=>
{
    var (before,_,line)=ProbeFixture();var axis=SpatialTestAxis(false,0,1,2);var reference=new AuditReference(new(20,20,20),12);
    var result=ProbeSpatialCalibration.Trial(before,SpatialStroke(before,line,3,0),line,7,axis,reference);
    Assert(!result.Passed&&result.CoreCoverage.Missing==28&&result.Spatial!.AllowedOffsets.SequenceEqual(new[]{1,2}));
});
Test("Spatial trial retains every longitudinal slice including a one-pixel gap",()=>
{
    var (before,_,line)=ProbeFixture();var axis=SpatialTestAxis(false,0,1,2);var after=SpatialStroke(before,line,2,0);after.Set(50*96+48,before.Color(50*96+48));
    var result=ProbeSpatialCalibration.Trial(before,after,line,7,axis,new(new(20,20,20),12));
    Assert(!result.Passed&&result.LongitudinalGaps==1&&result.CoreCoverage.Expected==28&&result.CoreCoverage.Missing==1&&result.Spatial!.PassedSlices==27);
});
Test("Spatial trial rejects another color without learning it from fast paint",()=>
{
    var (before,_,line)=ProbeFixture();var after=SpatialStroke(before,line,1,0);
    for(int x=30;x<=65;x++)after.Set(49*96+x,new(0,200,0));var reference=new AuditReference(new(20,20,20),12);
    var result=ProbeSpatialCalibration.Trial(before,after,line,7,SpatialTestAxis(false,0,1,2),reference);
    Assert(!result.Passed&&result.CoreCoverage.Unknown==28&&result.CoreCoverage.Reference==reference);
});
Test("Spatial wide cores need their full contiguous width, not a thin edge or scattered pixels",()=>
{
    var (before,_,line)=ProbeFixture();var axis=SpatialTestAxis(false,2,-1,1);var reference=new AuditReference(new(20,20,20),12);
    Assert(ProbeSpatialCalibration.Trial(before,SpatialStroke(before,line,1,2),line,7,axis,reference).Passed);
    var thin=ProbeSpatialCalibration.Trial(before,SpatialStroke(before,line,1,0),line,7,axis,reference);
    Assert(!thin.Passed&&thin.CoreCoverage.Expected==140&&thin.CoreCoverage.Covered==28&&thin.CoreCoverage.Missing==112);
    var split=SpatialStroke(before,line,0,3);for(int x=30;x<=65;x++)split.Set(48*96+x,before.Color(48*96+x));
    Assert(!ProbeSpatialCalibration.Trial(before,split,line,7,axis,reference).Passed,"Scattered pixels replaced a full core");
});
Test("Spatial trial rejects dirty background and scene changes",()=>
{
    var (before,_,line)=ProbeFixture();var axis=SpatialTestAxis(false,0,1,2);var reference=new AuditReference(new(20,20,20),12);var after=SpatialStroke(before,line,2,0);
    Assert(!ProbeSpatialCalibration.Trial(after,after,line,7,axis,reference).Passed,"Existing paint counted as a new stroke");
    for(int y=0;y<2;y++)for(int x=0;x<96;x++)after.Set(y*96+x,new(0,0,0));
    var result=ProbeSpatialCalibration.Trial(before,after,line,7,axis,reference);
    Assert(result.Failure==ProbeFailure.SceneChanged&&result.CoreCoverage.Covered==0&&result.CoreCoverage.Unknown==28);
});
Test("Spatial held-out slow validation refuses to expand its offset distribution",()=>
{
    var (before,_,line)=ProbeFixture();var axis=SpatialTestAxis(false,0,1,2);
    var slow=ProbeAnalysis.SpatialControl(before,SpatialStroke(before,line,3,0),line,7,0);
    Assert(slow.Passed);try{ProbeSpatialCalibration.Bind(axis,slow);throw new Exception("Out-of-model slow line accepted");}
    catch(InvalidOperationException e){Assert(e.Message.StartsWith(ProbeSpatialCalibration.OutsideMessage)&&e.Message.Contains("+3 px"));}
    Assert(axis.Offsets.SequenceEqual(new[]{1,2}));
});
Test("Live vertical minus-one held-out offset fits the predeclared minus-four to zero envelope",()=>
{
    var (before,_,_)=ProbeFixture();var line=new ScreenLine(48,30,48,65);
    var axis=SpatialTestAxis(true,0,-4,0);axis.Anchors[2]=axis.Anchors[2] with{Offset=-2};
    var slow=ProbeAnalysis.SpatialControl(before,SpatialStroke(before,line,-1,0),line,7,0);
    var reference=ProbeSpatialCalibration.Bind(axis,slow);
    var result=ProbeSpatialCalibration.Trial(before,SpatialStroke(before,line,-1,0),line,7,axis,reference);
    Assert(result.Passed&&result.Spatial!.PassedSlices==28&&axis.Offsets.SequenceEqual(new[]{-4,-2,0}));
    foreach(int outside in new[]{-5,1})
        Assert(!ProbeSpatialCalibration.Trial(before,SpatialStroke(before,line,outside,0),line,7,axis,reference).Passed);
    var gap=SpatialStroke(before,line,-1,0);gap.Set(48*96+47,before.Color(48*96+47));
    Assert(!ProbeSpatialCalibration.Trial(before,gap,line,7,axis,reference).Passed,"Intermediate offsets hid a missing slice");
});
Test("Malformed spatial bounds cannot allocate an unbounded envelope",()=>
{
    Assert(SpatialTestAxis(false,0,int.MinValue,int.MaxValue).AllowedOffsets.Length==0);
});
Test("Control layout diagnosis needs all three alternate controls and distinct captured rows",()=>
{
    bool[] ready=[true,true,true],partial=[true,false,true],missing=[false,false,false];
    Assert(ControlLayout.Inspect(ready,ready,3)==ControlLayoutState.Ready);
    Assert(ControlLayout.Inspect(missing,ready,3)==ControlLayoutState.DifferentMode);
    Assert(ControlLayout.Inspect(partial,partial,3)==ControlLayoutState.Missing);
    Assert(ControlLayout.Inspect(partial,ready,0)==ControlLayoutState.Missing);
});
Test("Spatial vertical reverse lines retain offsets and full-width occupancy",()=>
{
    var (before,_,_)=ProbeFixture();var line=new ScreenLine(48,65,48,30);var axis=SpatialTestAxis(true,1,-2,-1);
    var result=ProbeSpatialCalibration.Trial(before,SpatialStroke(before,line,-2,1),line,7,axis,new(new(20,20,20),12));
    Assert(result.Passed&&result.CoreCoverage.Expected==84&&result.Spatial!.PassedSlices==28);
});
Test("Offset trajectory records stable paint and a single transition without changing acceptance",()=>
{
    var (before,_,line)=ProbeFixture();var axis=SpatialTestAxis(false,0,-4,0);var reference=new AuditReference(new(20,20,20),12);
    var stable=ProbeSpatialCalibration.Trial(before,SpatialStroke(before,line,-1,0),line,7,axis,reference);
    var evidence=stable.Spatial!.Trajectory!;
    Assert(stable.Passed&&evidence.ResolvedSlices==28&&evidence.ComparablePairs==27&&evidence.Transitions==0
        &&evidence.MinimumOffset==-1&&evidence.MaximumOffset==-1&&evidence.DominantOffsets.SequenceEqual(new[]{-1}));
    Assert(evidence.LongestStableRun==28&&evidence.TransitionRate==0);
    var shifted=before.Clone();
    for(int k=0;k<=35;k++)shifted.Set((48+(k<18?-1:0))*96+30+k,new(20,20,20));
    var trial=ProbeSpatialCalibration.Trial(before,shifted,line,7,axis,reference);var path=trial.Spatial!.Trajectory!;
    Assert(trial.Passed&&trial.CoreCoverage.Covered==stable.CoreCoverage.Covered&&trial.CoreMask.SequenceEqual(stable.CoreMask));
    Assert(path.Transitions==1&&path.ComparablePairs==27&&path.MaximumJump==1&&path.MinimumOffset==-1&&path.MaximumOffset==0
        &&path.DominantOffsets.SequenceEqual(new[]{-1,0})&&path.DominantCount==14);
    Assert(path.LongestStableRun==14&&path.TransitionRate==1d/27);
});
Test("Offset diagnostics expose jitter while retaining existing PASS and frozen geometry",()=>
{
    var (before,_,line)=ProbeFixture();var axis=SpatialTestAxis(false,0,-4,0);var after=before.Clone();
    for(int k=0;k<=35;k++)after.Set((48+(k%2==0?-4:0))*96+30+k,new(20,20,20));
    var result=ProbeSpatialCalibration.Trial(before,after,line,7,axis,new(new(20,20,20),12));
    var path=result.Spatial!.Trajectory!;
    Assert(result.Passed&&result.CoreCoverage.Expected==28&&result.CoreCoverage.Covered==28&&result.CoreCoverage.Unknown==0
        &&result.Failure==ProbeFailure.None&&result.LongitudinalGaps==0);
    Assert(path.Transitions==27&&path.ComparablePairs==27&&path.MaximumJump==4&&path.MinimumOffset==-4&&path.MaximumOffset==0);
    Assert(path.LongestStableRun==1&&path.TransitionRate==1);
    Assert(axis.Offsets.SequenceEqual(new[]{-4,0})&&result.Spatial.AllowedOffsets.SequenceEqual(new[]{-4,-3,-2,-1,0}));
});
Test("Several supported core centres remain ambiguous rather than a fabricated stable trajectory",()=>
{
    var (before,_,line)=ProbeFixture();var axis=SpatialTestAxis(false,2,-1,1);
    var result=ProbeSpatialCalibration.Trial(before,SpatialStroke(before,line,0,3),line,7,axis,new(new(20,20,20),12));
    var path=result.Spatial!.Trajectory!;
    Assert(result.Passed&&result.CoreCoverage.Expected==140&&path.AmbiguousSlices==28&&path.ResolvedSlices==0
        &&path.UnresolvedSlices==0&&path.ComparablePairs==0&&path.DominantOffsets.Length==0
        &&path.MinimumOffset is null&&path.MaximumOffset is null&&path.OffsetsBySlice.All(x=>x is null));
    Assert(path.LongestStableRun==0&&path.TransitionRate is null);
});
Test("Offset transitions cannot bridge missing or ambiguous slices",()=>
{
    var path=ProbeOffsetTrajectory.Measure(new int[][]{[-1],[],[0],[0,-1],[-4],[0]});
    Assert(path.ResolvedSlices==4&&path.AmbiguousSlices==1&&path.UnresolvedSlices==1&&path.ComparablePairs==1
        &&path.Transitions==1&&path.MaximumJump==4&&path.DominantOffsets.SequenceEqual(new[]{0}));
    var (before,_,line)=ProbeFixture();var after=SpatialStroke(before,line,-1,0);after.Set(47*96+48,before.Color(47*96+48));
    var result=ProbeSpatialCalibration.Trial(before,after,line,7,SpatialTestAxis(false,0,-4,0),new(new(20,20,20),12));
    Assert(!result.Passed&&result.Spatial!.Trajectory!.UnresolvedSlices==1&&result.Spatial.Trajectory.ComparablePairs==25);
});
Test("Stable offset runs cannot join through ambiguous or missing slices",()=>
{
    var path=ProbeOffsetTrajectory.Measure(new int[][]{[1],[1],[1,2],[1],[1],[1],[],[1],[1],[2],[2]});
    Assert(path.LongestStableRun==3&&path.ComparablePairs==6&&path.Transitions==1&&path.TransitionRate==1d/6);
    var blocks=ProbeOffsetTrajectory.Measure(new int[][]{[1],[1],[1],[1],[1],[1],[2],[2],[2],[2]});
    var alternating=ProbeOffsetTrajectory.Measure(new int[][]{[1],[2],[1],[2],[1],[2],[1],[2],[1],[1]});
    Assert(blocks.MinimumOffset==alternating.MinimumOffset&&blocks.MaximumOffset==alternating.MaximumOffset
        &&blocks.DominantCount==alternating.DominantCount&&blocks.LongestStableRun==6&&alternating.LongestStableRun==2
        &&blocks.TransitionRate==1d/9&&alternating.TransitionRate==8d/9);
});
Test("No adjacent resolved offsets leaves transition rate unavailable",()=>
{
    foreach(var slices in new int[][][]{[],[[],[1,2]],[[1]],[[1],[],[1],[1,2],[2]]})
    {
        var path=ProbeOffsetTrajectory.Measure(slices);
        Assert(path.ComparablePairs==0&&path.TransitionRate is null);
        Assert(path.LongestStableRun==(path.ResolvedSlices==0?0:1));
    }
});
Test("Offset trajectory is withheld on a scene-change failure",()=>
{
    var (before,_,line)=ProbeFixture();var after=SpatialStroke(before,line,-1,0);
    for(int y=0;y<2;y++)for(int x=0;x<96;x++)after.Set(y*96+x,new(0,0,0));
    var result=ProbeSpatialCalibration.Trial(before,after,line,7,SpatialTestAxis(false,0,-4,0),new(new(20,20,20),12));
    Assert(result.Failure==ProbeFailure.SceneChanged&&result.Spatial!.Trajectory is null&&result.CoreCoverage.Covered==0);
});
Test("ETA warms up on twenty completed operations rather than initial setup or saved Done",()=>
{
    var timer=new RemainingTime(Enumerable.Range(0,100).Select(i=>new TimedWork($"m{i}","3:drag:H",.0642,true)));
    for(int i=0;i<19;i++)timer.Complete($"m{i}",.129);
    Assert(timer.Estimate(0).Basis==EtaBasis.Planned&&Math.Abs(timer.Estimate(0).Seconds-81*.0642)<1e-9);
    timer.Complete("m19",.129);var result=timer.Estimate(0);
    Assert(result.Basis==EtaBasis.Measured&&result.MotionSamples==20&&Math.Abs(result.Seconds-80*.129)<1e-9&&Math.Abs(result.MeanMotionMs-129)<1e-9);
});
Test("ETA follows the most recent fifty motions when the actual pace changes",()=>
{
    var timer=new RemainingTime(Enumerable.Range(0,150).Select(i=>new TimedWork($"m{i}","drag",.064,true)));
    for(int i=0;i<50;i++)timer.Complete($"m{i}",.128);
    Assert(Math.Abs(timer.Estimate(0).Seconds-100*.128)<1e-9);
    for(int i=50;i<100;i++)timer.Complete($"m{i}",.064);
    var result=timer.Estimate(0);Assert(result.WindowSamples==50&&Math.Abs(result.Seconds-50*.064)<1e-9&&Math.Abs(result.MotionRatio-1)<1e-9);
});
Test("ETA scales remaining motion duration instead of multiplying mixed lengths by a flat average",()=>
{
    var work=Enumerable.Range(0,20).Select(i=>new TimedWork($"m{i}","drag",i%2==0?.1:1,true))
        .Concat(new[]{new TimedWork("short","drag",.1,true),new("long","drag",1,true)});
    var timer=new RemainingTime(work);for(int i=0;i<20;i++)timer.Complete($"m{i}",i%2==0?.2:2);
    Assert(Math.Abs(timer.Estimate(0).Seconds-2.2)<1e-9&&Math.Abs(timer.Estimate(0).MotionRatio-2)<1e-9);
});
Test("ETA keeps unmeasured Sizes and routes planned rather than extrapolating another route",()=>
{
    var work=Enumerable.Range(0,20).Select(i=>new TimedWork($"m{i}","1:drag:H",.1,true))
        .Concat(new[]{new TimedWork("known","1:drag:H",.1,true),new("wide","20:probe_Shift:V",1,true)});
    var timer=new RemainingTime(work);for(int i=0;i<20;i++)timer.Complete($"m{i}",.2);
    var result=timer.Estimate(0);Assert(result.Basis==EtaBasis.Mixed&&result.UnmeasuredOperations==1&&Math.Abs(result.Seconds-1.2)<1e-9);
});
Test("ETA needs five local route samples after overall warmup",()=>
{
    var work=Enumerable.Range(0,25).Select(i=>new TimedWork($"m{i}",i<20?"H":"V",.1,true)).Append(new("remaining","V",.1,true));
    var timer=new RemainingTime(work);for(int i=0;i<24;i++)timer.Complete($"m{i}",.2);
    Assert(timer.Estimate(0).Basis==EtaBasis.Mixed);timer.Complete("m24",.2);
    Assert(timer.Estimate(0).Basis==EtaBasis.Measured&&Math.Abs(timer.Estimate(0).Seconds-.2)<1e-9);
});
Test("Rare Size ETA blends only its own samples and remains partly measured",()=>
{
    var work=Enumerable.Range(0,20).Select(i=>new TimedWork($"m{i}","3:H",.1,true))
        .Concat(Enumerable.Range(0,5).Select(i=>new TimedWork($"wide{i}","20:H",1,true)))
        .Append(new("unknown","20:V",2,true));
    var timer=new RemainingTime(work);for(int i=0;i<20;i++)timer.Complete($"m{i}",.8);
    for(int i=0;i<4;i++)
    {
        timer.Complete($"wide{i}",2);var result=timer.Estimate(0);
        Assert(result.Basis==EtaBasis.Mixed&&Math.Abs(result.Seconds-((4-i)*(1+(i+1)/5.0)+2))<1e-9);
    }
    Assert(timer.Estimate(0).UnmeasuredOperations==2);
});
Test("ETA isolates startup and rolling color overhead from successful motion measurements",()=>
{
    var work=new[]{new TimedWork("setup","setup",2,false)}
        .Concat(Enumerable.Range(0,10).Select(i=>new TimedWork($"c{i}","color",1,false)))
        .Concat(Enumerable.Range(0,21).Select(i=>new TimedWork($"m{i}","drag",.1,true)));
    var timer=new RemainingTime(work);timer.Complete("setup",30);
    for(int i=0;i<9;i++)timer.Complete($"c{i}",i==0?8:.5);
    for(int i=0;i<20;i++)timer.Complete($"m{i}",.2);
    var result=timer.Estimate(0);Assert(result.MotionSamples==20&&Math.Abs(result.Seconds-.7)<1e-9&&Math.Abs(result.MeanMotionMs-200)<1e-9);
});
Test("ETA uses active time and remains unchanged through a wall-clock pause",()=>
{
    var timer=new RemainingTime(new[]{new TimedWork("color","color",4,false),new("stroke","drag",1,true)});
    timer.Begin("color",10);Assert(Math.Abs(timer.Estimate(12).Seconds-3)<1e-9);
    double wall=112,paused=100;Assert(timer.Estimate(wall-paused)==timer.Estimate(12));
    timer.Complete("color",2);Assert(timer.Estimate(12).Seconds==1&&timer.Estimate(12).MotionSamples==0);
});
Test("ETA cannot double-count completion or learn from a skipped or unfinished operation",()=>
{
    var timer=new RemainingTime(new[]{new TimedWork("size","brush",2,false),new("motion","drag",1,true)});
    timer.Begin("motion",0);Assert(timer.Estimate(.5).MotionSamples==0);
    Assert(timer.Skip("size")&&!timer.Skip("size"));Assert(timer.Complete("motion",1)&&!timer.Complete("motion",10));
    Assert(timer.Estimate(1).MotionSamples==1&&timer.Estimate(1).Basis==EtaBasis.Complete&&timer.Estimate(1).Seconds==0);
});
Test("ETA retains pending audit and finishing work after all drawing operations",()=>
{
    var work=Enumerable.Range(0,20).Select(i=>new TimedWork($"m{i}","drag",.1,true))
        .Concat(new[]{new TimedWork("audit","audit",.2,false),new("finish","finish",1,false)});
    var timer=new RemainingTime(work);for(int i=0;i<20;i++)timer.Complete($"m{i}",.2);
    var result=timer.Estimate(0);Assert(result.RemainingOperations==0&&result.Basis!=EtaBasis.Complete&&Math.Abs(result.Seconds-1.2)<1e-9);
    timer.Complete("audit",2);Assert(timer.Estimate(0).Seconds==1);timer.Complete("finish",1);Assert(timer.Estimate(0).Basis==EtaBasis.Complete);
});
Test("ETA rejects invalid observations before consuming pending work",()=>
{
    var timer=new RemainingTime(new[]{new TimedWork("m","drag",1,true)});
    foreach(double invalid in new[]{double.NaN,double.PositiveInfinity,-1})
    {
        try{timer.Complete("m",invalid);throw new Exception("Invalid measurement accepted");}catch(ArgumentOutOfRangeException){}
        Assert(timer.Contains("m")&&timer.Estimate(0).MotionSamples==0);
    }
    foreach(var bad in new[]{new[]{new TimedWork("m","drag",0,true)},new[]{new TimedWork("m","drag",1,true),new("m","drag",1,true)},
        new[]{new TimedWork("a","shared",1,true),new("b","shared",1,false)}})
        try{_ = new RemainingTime(bad);throw new Exception("Invalid work accepted");}catch(ArgumentException){}
});
Test("Timing plan starts fresh at a resume position without counting past operations",()=>
{
    var cfg=Settings.Defaults();var groups=new Dictionary<int,List<PaintBatch>>{
        [0]=Enumerable.Range(0,40).Select(i=>new PaintBatch(0,new[]{new ScreenLine(0,i,20,i)},1)).ToList(),
        [1]=Enumerable.Range(0,40).Select(i=>new PaintBatch(0,new[]{new ScreenLine(0,i,20,i)},1)).ToList()};
    var resumed=PaintTimingPlan.Build(cfg,groups,new[]{0,1},1,30);var timer=new RemainingTime(resumed);
    Assert(resumed.Count(x=>x.Motion)==10&&timer.Estimate(0).MotionSamples==0&&timer.Estimate(0).Basis==EtaBasis.Planned);
    Assert(resumed.All(x=>!x.Id.StartsWith("motion:0:")&&!x.Id.StartsWith("color:0")));
    Assert(resumed.Any(x=>x.Id==PaintTimingPlan.Setup)&&resumed.Any(x=>x.Id==PaintTimingPlan.Color(1)));
});
Test("Timing plan counts batch operations and captures actual adaptive Size transitions",()=>
{
    var cfg=Settings.Defaults();cfg.Set("adaptive_brush",true);cfg.Set("coverage_audit",true);
    var groups=new Dictionary<int,List<PaintBatch>>{[0]=new(){new(20,new[]{new ScreenLine(0,0,20,0)},8),new(20,new[]{new ScreenLine(0,1,20,1)},8),new(0,new[]{new ScreenLine(0,2,20,2)},1)}};
    var work=PaintTimingPlan.Build(cfg,groups,new[]{0});Assert(work.Count(x=>x.Motion)==3&&work.Count(x=>x.RateKey=="brush")==2);
    Assert(work.Any(x=>x.Id==PaintTimingPlan.BeforeAudit(0))&&work.Any(x=>x.Id==PaintTimingPlan.Audit(0)));
    var resumed=PaintTimingPlan.Build(cfg,groups,new[]{0},0,2);Assert(resumed.All(x=>!x.Id.StartsWith("size:")),"Resume inherited old adaptive Size");
});
Test("Timing schedule and route keys preserve settings and reflect verified motion transport",()=>
{
    var cfg=ProbeConfig();cfg.Set("adaptive_brush",true);var before=cfg.Data.ToJsonString();
    var batch=new PaintBatch(1,new[]{new ScreenLine(0,0,32,0)},1);
    Assert(PaintTimingPlan.Route(cfg,batch,1)=="1:probe_Shift:H");
    PaintTimingPlan.Build(cfg,new(){[0]=new(){batch}},new[]{0});Assert(cfg.Data.ToJsonString()==before);
    cfg.Set("calibrated_strokes",false);Assert(PaintTimingPlan.Route(cfg,batch,1)=="1:drag:H");
});
Test("Timing default Size agrees with precision, manual and calibrated automatic controls",()=>
{
    var cfg=ProbeConfig();Assert(PaintTimingPlan.DefaultSize(cfg)==SpeedProfile.Get(cfg.Text("speed_profile","Rapid")).BrushSize);
    cfg.Set("force_precision_controls",false);cfg.Set("auto_brush_size",false);cfg.Set("brush_size_value",10);Assert(PaintTimingPlan.DefaultSize(cfg)==10);
    cfg.Set("auto_brush_size",true);Assert(PaintTimingPlan.DefaultSize(cfg)==AutomaticBrush.Value(cfg,Math.Max(1,cfg.Int("cell_px",3))));
});
Test("Palette comparison caps four plans while preserving source, settings and geometry", () =>
{
    var cfg = Config(ColorMode.HexDirect, 32, 16); cfg.Set("hex_max_colors", "Auto");
    cfg.Set("fast_transfer", true); cfg.Set("input_engine", "Experimental 1 ms");
    var image = new PixelImage(32, 16);
    for (int i = 0; i < 512; i++) image.Set(i, new((byte)(i % 256), (byte)(i * 7 % 256), (byte)(i * 13 % 256)), i % 17 == 0 ? (byte)0 : (byte)255);
    var bytes = (byte[])image.Rgba.Clone(); string json = cfg.Data.ToJsonString();
    var result = PaletteComparison.Build(image, cfg);
    Assert(result.Variants.Select(v => v.Limit).SequenceEqual(new[] { 64, 96, 128, 256 }));
    Assert(cfg.Data.ToJsonString() == json && image.Rgba.SequenceEqual(bytes), "Comparison mutated the active input");
    Assert(result.SettingsJson == json && result.SourceWidth == 32 && result.SourceHeight == 16 && result.SourceSha256.Length == 64);
    foreach (var variant in result.Variants)
    {
        Assert(variant.Plan.Width == 32 && variant.Plan.Height == 16 && variant.Plan.ColorCount <= variant.Limit);
        Assert(variant.Plan.Mode == ColorMode.HexDirect && variant.Plan.Indices.Where((_, i) => i % 17 == 0).All(x => x == -1));
        var expected = cfg.Clone(); expected.Set("hex_max_colors", variant.Limit.ToString());
        var plan = Planner.Build(image, expected);
        Assert(variant.Plan.Identity == plan.Identity && variant.Plan.Indices.SequenceEqual(plan.Indices), "Comparison changed more than the cap");
    }
});
Test("Palette comparison counts execution batches and timing including colors, start and audits", () =>
{
    var cfg = Config(ColorMode.HexDirect); cfg.Set("coverage_audit", true); cfg.Set("start_delay", 7);
    cfg.Set("fast_transfer", true); cfg.Set("input_engine", "Experimental 1 ms");
    foreach (var variant in PaletteComparison.Build(Fixture(), cfg).Variants)
    {
        var settings = cfg.Clone(); settings.Set("hex_max_colors", variant.Limit.ToString());
        var groups = TransferSchedule.Build(variant.Plan, settings); var batches = groups.Values.SelectMany(x => x).ToArray();
        var timing = PaintTimingPlan.Build(settings, groups, TransferSchedule.Order(variant.Plan, groups));
        Assert(variant.Operations == batches.Length && variant.SourceStrokes == batches.Sum(x => x.SourceStrokes));
        Assert(variant.ColorChanges == groups.Count && variant.WideOperations == batches.Count(x => x.Size > 0));
        Assert(Math.Abs(variant.PlannedSeconds - (7 + timing.Sum(x => x.PlannedSeconds))) < 1e-9);
        Assert(timing.Count(x => x.RateKey == "audit") == groups.Count && variant.SizeChanges == 0);
    }
});
Test("Palette comparison publishes no partial result after cancellation", () =>
{
    var cfg = Config(ColorMode.HexDirect); string before = cfg.Data.ToJsonString();
    using var canceled = new CancellationTokenSource(); canceled.Cancel();
    try { PaletteComparison.Build(Fixture(), cfg, token: canceled.Token); throw new Exception("Canceled comparison ran"); }
    catch (OperationCanceledException) { }
    using var active = new CancellationTokenSource(); int reports = 0;
    var progress = new InlineComparisonProgress(p => { reports++; if (p.Completed == 1) active.Cancel(); });
    try { PaletteComparison.Build(Fixture(), cfg, progress, active.Token); throw new Exception("Partial comparison returned"); }
    catch (OperationCanceledException) { }
    Assert(reports == 2 && before == cfg.Data.ToJsonString());
});
Test("Palette comparison rejects palette mode and stale adaptive calibration", () =>
{
    var palette = Config(ColorMode.RustPalette);
    try { PaletteComparison.Build(Fixture(), palette); throw new Exception("Palette mode accepted"); }
    catch (InvalidOperationException e) { Assert(e.Message.Contains("HEX Direct")); }
    var adaptive = Config(ColorMode.HexDirect); adaptive.Set("adaptive_brush", true);
    try { PaletteComparison.Build(Fixture(), adaptive); throw new Exception("Stale adaptive silently disabled"); }
    catch (InvalidOperationException) { }
    Assert(adaptive.Bool("adaptive_brush"));
});
Test("Transparent palette comparison has no painting or color operations", () =>
{
    var result = PaletteComparison.Build(new PixelImage(16, 12), Config(ColorMode.HexDirect));
    Assert(result.Variants.All(x => x.Operations == 0 && x.WideOperations == 0 && x.ColorChanges == 0 && x.Plan.ColorCount == 0));
});
Test("Palette comparison retains current HEX adaptive calibration and counts accepted wide batches", () =>
{
    var cfg = Config(ColorMode.HexDirect, 240, 240); cfg.Set("adaptive_brush", true); cfg.Set("fast_transfer", true);
    var hex = cfg.Calibration;
    foreach (var (key, y) in new[] { ("size_track", 260), ("interval_track", 300), ("opacity_track", 340), ("brush_shapes", 380) })
        hex.SetRect(key, new(250, y, 500, y + 40));
    cfg.SetPaintCalibration(hex);
    cfg.Set("brush_calibration_points", new double[][] { [1, 3, 1], [3, 5, 3], [10, 21, 13], [20, 35, 23] });
    cfg.Set("brush_calibration_context", AdaptiveBrush.Context(cfg));
    string before = cfg.Data.ToJsonString();
    var result = PaletteComparison.Build(Fixture(), cfg);
    Assert(result.Variants.All(v => v.WideOperations > 0 && v.SizeChanges > 0));
    foreach (var variant in result.Variants)
    {
        var settings = cfg.Clone(); settings.Set("hex_max_colors", variant.Limit.ToString());
        Assert(AdaptiveBrush.CalibrationCurrent(settings));
        var batches = TransferSchedule.Build(variant.Plan, settings).Values.SelectMany(x => x).ToArray();
        Assert(variant.WideOperations == batches.Count(x => x.Size > 0) && variant.Operations == batches.Length);
    }
    Assert(before == cfg.Data.ToJsonString(), "Comparison modified the shared adaptive calibration");
});
BrushFootprintChecks.Run(Test);
LocalProbeChecks.Run(Test);
ProbeSpeedSearchChecks.Run(Test);
BrushBatchChecks.Run(Test);
BrushSignalChecks.Run(Test);
BrushColorChecks.Run(Test);
ReviewRegressionChecks.Run(Test);
DrawingWorkflowChecks.Run(Test);
Console.WriteLine($"ALL {passed} TESTS PASSED");

static SpatialProbeProfile SpatialFixtureProfile(Settings cfg,double size)
{
    var footprint=SpeedCalibration.Footprint(cfg,size);var c=cfg.Calibration.Rect("canvas");var axes=new List<SpatialAxis>();
    foreach(bool vertical in new[]{false,true})axes.Add(new(vertical,footprint.Inner,Enumerable.Range(0,3).Select(i=>new SpatialAnchor(
        vertical?new(c.Left+20+i*30,c.Top+20,c.Left+20+i*30,c.Top+55):new(c.Left+20,c.Top+20+i*30,c.Left+55,c.Top+20+i*30),
        0,new(20,20,20),12)).ToList()));
    return new(Guid.NewGuid().ToString("N"),ProbeSpatialCalibration.Context(cfg),DateTimeOffset.UtcNow,size,footprint.Outer,axes);
}
static SpatialAxis SpatialTestAxis(bool vertical,int inner,int a,int b)=>new(vertical,inner,new[]{a,b,a}.Select((offset,i)=>new SpatialAnchor(
    vertical?new(100+i*30,100,100+i*30,135):new(100,100+i*30,135,100+i*30),offset,new(20,20,20),12)).ToList());
static PixelImage SpatialStroke(PixelImage before,ScreenLine line,int offset,int inner)
{
    var after=before.Clone();int n=TransferSchedule.Length(line),dx=Math.Sign(line.X2-line.X1),dy=Math.Sign(line.Y2-line.Y1);
    for(int k=0;k<=n;k++)for(int p=offset-inner;p<=offset+inner;p++)
        after.Set((line.Y1+k*dy+(dx!=0?p:0))*before.Width+line.X1+k*dx+(dy!=0?p:0),new(20,20,20));
    return after;
}

static (PixelImage Before,PixelImage After,ScreenLine Line) ProbeFixture()
{
    var before=new PixelImage(96,96);for(int i=0;i<96*96;i++)before.Set(i,new(200,200,200));
    var after=before.Clone();for(int x=30;x<=65;x++)for(int p=-5;p<=5;p++)
        after.Set((48+p)*96+x,Math.Abs(p)<=2?new(0,0,0):new((byte)(40+Math.Abs(p)*20),0,0));
    return(before,after,new(30,48,65,48));
}

sealed class InlineComparisonProgress(Action<PaletteComparisonProgress> report) : IProgress<PaletteComparisonProgress>
{
    public void Report(PaletteComparisonProgress value) => report(value);
}

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
