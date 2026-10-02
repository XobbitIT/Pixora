using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using CanvasForge.Core;

namespace CanvasForge.App;
internal sealed record PaintProgress(int Done, int Total, double Elapsed, double Eta, string Status);
internal sealed record ResumeCheckpoint(string Identity, int Group, int Line, int Done);
internal sealed class Painter
{
    private readonly Settings settings;
    private readonly IntPtr window;
    private readonly ScreenRect windowRect;
    private readonly uint windowProcessId;
    private readonly int windowDpi;
    private readonly CoordinateRebase rebase;
    private readonly CancellationToken token;
    private readonly Action<PaintProgress> report;
    private readonly string checkpointPath;
    private readonly string logPath;
    private readonly Stopwatch clock = new();
    private readonly bool experimental;
    private readonly HashSet<long> verifiedSizeValues = [];
    private int hexChangesSinceReadback;
    private bool needsReprime;
    private bool f6Held;
    private long nextSafety;
    private double pausedSeconds;
    private volatile bool paused;
    public bool Paused { get => paused; set => paused = value; }

    public Painter(Settings s, IntPtr target, string checkpoint, string log, Action<PaintProgress> callback, CancellationToken cancel)
    {
        settings = s.Clone();
        experimental = StrokeTiming.Experimental(settings);
        window = target;
        token = cancel;
        report = callback;
        checkpointPath = checkpoint;
        logPath = log;
        if (!Native.IsRust(target) || !Native.GetWindowRect(target, out var r))
            throw new InvalidOperationException("Не знайдено вікно Rust. Переконайся, що гра відкрита, і повтори захоплення.");
        windowRect = r.ToScreen();
        windowProcessId = Native.ProcessIdOf(target);
        windowDpi = Native.DpiOf(target);
        rebase = SessionRebase();
    }

    // Rebases the captured absolute coordinates from the Rust client origin that
    // was recorded at calibration time onto the window's current position/DPI.
    // Strokes and controls read from settings.Calibration, so shifting those
    // keys fixes them; the palette click points baked into the plan are mapped
    // separately via MapPoint. When the window has not moved (or no baseline was
    // captured) this is the identity transform and nothing changes.
    private CoordinateRebase SessionRebase()
    {
        var cal = settings.Calibration;
        if (cal.SessionClient is not { } baseline)
            return default;
        var now = Native.ClientOrigin(window);
        var result = CalibrationSession.Align(settings, now, windowDpi, Native.ClientSize(window));
        if (!result.IsIdentity)
            Log("session_rebase", new { baseline, baseDpi = cal.SessionDpi, now, nowDpi = result.NowDpi, scale = result.Scale });
        return result;
    }

    private ScreenPoint MapPoint(ScreenPoint p)
    {
        if (rebase.IsIdentity) return p;
        var (x, y) = rebase.Map(p.X, p.Y);
        return new(x, y);
    }

    private void Log(string action, object? details = null)
    {
        try
        {
            File.AppendAllText(logPath, JsonSerializer.Serialize(new { time = DateTimeOffset.UtcNow, action, details }) + Environment.NewLine);
        }
        catch
        {
        }
    }

    private void Check()
    {
        token.ThrowIfCancellationRequested();
        if (Native.Down(0x1B))
            throw new OperationCanceledException("ESC");
        var f6 = Native.Down(0x75);
        if (f6 && !f6Held)
            Paused = !Paused;
        f6Held = f6;
        if (Native.GetForegroundWindow() != window)
            Paused = true;
        if (Environment.TickCount64 < nextSafety)
            return;
        nextSafety = Environment.TickCount64 + 75;
        if (Native.ProcessIdOf(window) != windowProcessId || !Native.IsRust(window))
            throw new InvalidOperationException("Не знайдено вікно Rust. Переконайся, що гра відкрита, і повтори захоплення.");
        if (Native.DpiOf(window) != windowDpi)
            throw new InvalidOperationException(CalibrationSession.DpiChangedMessage);
        if (!Native.GetWindowRect(window, out var rect) || rect.ToScreen() != windowRect)
            throw new InvalidOperationException("Вікно Rust змінило розмір або положення. Повтори захоплення.");
    }

    // All input waits are interruptible; never retain mouse-down while paused.
    private bool WaitReady()
    {
        Check();
        if (!Paused)
            return false;
        Native.Release();
        verifiedSizeValues.Clear();
        var start = clock.Elapsed.TotalSeconds;
        report(new(0, 0, 0, 0, "Пауза — повернись у Rust і натисни F6."));
        while (Paused)
        {
            token.ThrowIfCancellationRequested();
            if (Native.Down(0x1B))
                throw new OperationCanceledException();
            var f = Native.Down(0x75);
            if (f && !f6Held && Native.GetForegroundWindow() == window)
                Paused = false;
            f6Held = f;
            Thread.Sleep(15);
        }

        pausedSeconds += clock.Elapsed.TotalSeconds - start;
        nextSafety = 0;
        Check();
        if (Paused)
            WaitReady();
        needsReprime = true;
        return true;
    }

    private void Delay(double seconds)
    {
        var until = Stopwatch.GetTimestamp() + seconds * Stopwatch.Frequency;
        while (Stopwatch.GetTimestamp() < until)
        {
            Check();
            if (Paused)
            {
                Native.Release();
                throw new InputInterrupted();
            }

            var remaining = (until - Stopwatch.GetTimestamp()) / (double)Stopwatch.Frequency;
            if (remaining > .002)
                Thread.Sleep(1);
            else
                Thread.SpinWait(40);
        }
    }

    private sealed class InputInterrupted : Exception
    {
    }

    private void Click(ScreenPoint p, bool twice = false)
    {
        for (var i = 0; i < (twice ? 2 : 1); i++)
        {
            WaitReady();
            if (experimental)
            {
                Delay(.002);
                Native.MoveAndDown(p.X, p.Y);
                try
                {
                    // One short game-frame-sized hold is much more reliable than a literal 1 ms click.
                    Delay(.012);
                }
                finally
                {
                    Native.Mouse(true);
                }
                Delay(twice ? .012 : .004);
            }
            else
            {
                Native.SetCursorPos(p.X, p.Y);
                Delay(Math.Max(.005, settings.Number("click_delay", .02)));
                Native.Mouse(false);
                try
                {
                    Delay(Math.Max(.04, settings.Number("mouse_up_delay_ms", 8) / 1000));
                }
                finally
                {
                    Native.Mouse(true);
                }
                Delay(twice ? Math.Max(.04, settings.Number("reclick_delay_ms", 35) / 1000) : .04);
            }
        }
    }

    private void PressKey(int key)
    {
        Native.Key(key);
        try { Delay(experimental ? .012 : .04); }
        finally { Native.Key(key, true); }
        Delay(experimental ? .008 : .04);
    }

    private void ChordKey(int modifier, int key)
    {
        Native.Key(modifier);
        try { Delay(experimental ? .008 : .04); PressKey(key); }
        finally { Native.Key(modifier, true); }
        Delay(experimental ? .012 : .06);
    }

    private void TypeHex(string text)
    {
        foreach (var ch in text)
        {
            Native.UnicodeKey(ch, false);
            try { Delay(experimental ? .012 : .04); }
            finally { Native.UnicodeKey(ch, true); }
            Delay(experimental ? .006 : .05);
        }
    }

    private void SelectHex()
    {
        // Unity's HEX field normalizes on edit. Replacing one selection avoids
        // intermediate empty/partial values moving the caret on each deletion.
        PressKey(0x23);
        ChordKey(0x10, 0x24);
        Delay(experimental ? .02 : .08);
    }

    private void SelectHexMouse(ScreenRect box)
    {
        var y = box.Center.Y;
        Native.SetCursorPos(box.Right - 3, y);
        Delay(experimental ? .012 : .05);
        Native.Mouse(false);
        try
        {
            Delay(experimental ? .012 : .05);
            for (var i = 1; i <= 12; i++)
            {
                Native.SetCursorPos(box.Right - 3 + (box.Left + 3 - (box.Right - 3)) * i / 12, y);
                Delay(experimental ? .006 : .025);
            }
        }
        finally { Native.Mouse(true); }
        Delay(experimental ? .02 : .08);
    }

    private void WriteClipboard(string text)
    {
        try
        {
            ClipboardRetry.Run(() => { Native.ClipboardWrite(text); return true; }, () => Delay(.05));
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Log("clipboard", new { status = "write_failed" });
            throw; // Never paste if the new payload was not written.
        }
    }

    private string? ReadClipboard()
    {
        try { return ClipboardRetry.Run(Native.ClipboardRead, () => Delay(.05)); }
        catch (System.ComponentModel.Win32Exception)
        {
            Log("clipboard", new { status = "read_failed" });
            throw;
        }
    }

    private string? ReadHex(ScreenPoint p, Rgb expected)
    {
        var box = settings.Calibration.Rect("hex");
        var readback = new HexReadback(expected);
        for (var strategy = 0; strategy < 3; strategy++)
        {
            Click(p);
            if (strategy == 0) ChordKey(0x11, 0x41);
            else if (strategy == 1) SelectHex();
            else if (box.Valid) SelectHexMouse(box);
            else continue;
            WriteClipboard(HexReadback.Marker);
            ChordKey(0x11, 0x43);
            Delay(experimental ? .05 : .15);
            var raw = ReadClipboard();
            var value = HexReadback.Normalize(raw);
            Log("hex_readback", new { strategy, status = readback.Status(raw), raw, value });
            if (readback.Observe(raw)) return value;
        }
        return readback.LastValid;
    }

    public bool ApplyHex(Rgb rgb, bool forceVerify = false)
    {
        var point = settings.Calibration.HexPoint ?? throw new InvalidOperationException("Захопи поле HEX.");
        var target = rgb.Hex;
        var previous = ReadClipboard();
        try
        {
            var maxAttempts = HexReadback.AttemptCount(settings.Int("hex_verify_retries", 2));
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                WaitReady();
                Click(point);
                Delay(experimental ? .008 : .02);
                var box = settings.Calibration.Rect("hex");
                if (attempt % 3 == 0) ChordKey(0x11, 0x41);
                else if (attempt % 3 == 1) SelectHex();
                else if (box.Valid) SelectHexMouse(box);
                else ChordKey(0x11, 0x41);
                var payload = settings.Bool("hex_include_hash", false) ? "#" + target : target;
                WriteClipboard(payload);
                ChordKey(0x11, 0x56);
                Delay(experimental ? .02 : .18);
                PressKey(0x0D);
                var configuredDelay = settings.Number("hex_apply_delay_ms", 180) / 1000;
                Delay(experimental ? Math.Clamp(configuredDelay / 3, .06, .08) : Math.Max(.18, configuredDelay));

                var verify = forceVerify || settings.Bool("hex_verify", true);
                var swatch = settings.Calibration.Rect("swatch");
                if (!swatch.Valid && settings.Calibration.Point("color_swatch") is ScreenPoint sp)
                    swatch = new(sp.X - 3, sp.Y - 3, sp.X + 4, sp.Y + 4);

                var colorOk = true;
                if (verify && swatch.Valid)
                {
                    var c = Native.Median(swatch);
                    var tolerance = settings.Int("hex_verify_tolerance", 22);
                    colorOk = Math.Max(Math.Abs(c.R - rgb.R), Math.Max(Math.Abs(c.G - rgb.G), Math.Abs(c.B - rgb.B))) <= tolerance;
                }

                // A swatch screenshot is much cheaper than selecting/copying the HEX field.
                // Keep a periodic full readback as a guard, and always read back when swatch
                // verification is unavailable or failed.
                var readbackEvery = Math.Clamp(settings.Int("hex_readback_every", 8), 1, 64);
                var fullReadback = verify && (forceVerify || !swatch.Valid || !colorOk || hexChangesSinceReadback >= readbackEvery - 1);
                var read = fullReadback ? ReadHex(point, rgb) : target;

                Log("hex", new
                {
                    version = BuildInfo.Version,
                    commit = BuildInfo.GitCommit,
                    target,
                    payload,
                    read,
                    attempt,
                    colorOk,
                    swatchVerified = verify && swatch.Valid,
                    fullReadback
                });
                if (read == target && colorOk)
                {
                    hexChangesSinceReadback = fullReadback ? 0 : hexChangesSinceReadback + 1;
                    return true;
                }
            }

            return false;
        }
        finally
        {
            if (previous is not null)
                try
                {
                    Native.ClipboardWrite(previous);
                }
                catch
                {
                }
        }
    }

    private readonly record struct SliderTarget(ScreenPoint Point, ScreenRect Track, double Fraction, bool Anchored);

    // Sizes for which the user may capture an exact thumb position. An anchor is
    // ground truth for that value, so it takes priority over the universal
    // ControlCurve interpolation (whose hardcoded fraction mapping may not match
    // the current Rust slider exactly).
    private static string? SizeAnchorKey(double value)
    {
        foreach (var s in new[] { 1, 3, 10, 20 })
            if (Math.Abs(value - s) < .01)
                return "size_anchor_" + s;
        return null;
    }

    private SliderTarget? ResolveSliderTarget(string kind, double value)
    {
        var cal = settings.PaintCalibration();
        var box = cal.Rect(kind + "_track");
        var fraction = Math.Clamp(ControlCurve.Fraction(kind, value), 0, 1);

        ScreenPoint? point = null;
        var anchored = false;
        if (kind == "size" && SizeAnchorKey(value) is { } anchorKey)
        {
            point = cal.Point(anchorKey);
            anchored = point is not null;
        }

        point ??= ControlCurve.Point(cal, kind, value);
        if (point is null)
            return null;

        // IMPORTANT: the captured min/max anchors (or a per-size manual anchor)
        // are the source of truth. Older builds re-detected a "green run" inside
        // the screenshot and replaced the click geometry with it. On the current
        // Rust UI the green fill/label is not the real interactive slider range,
        // so Size 1 could jump to ~2.27 and every subsequent stroke became too
        // wide. Keep the captured anchors for clicking; the rectangle is
        // diagnostics / verification only.
        Log("control_target", new
        {
            kind,
            value,
            fraction,
            point,
            anchored,
            min = cal.Point(kind + "_min"),
            max = cal.Point(kind + "_max"),
            box
        });
        return new(point.Value, box, fraction, anchored);
    }

    private bool SliderMatches(string kind, double value)
    {
        var target = ResolveSliderTarget(kind, value);
        if (target is null || !target.Value.Track.Valid) return false;
        var actual = DetectSlider(Native.Screenshot(target.Value.Track));
        if (actual is null)
        {
            Log("slider_check_unverified", new { kind, value, desired = target.Value.Fraction, reason = "green band not found" });
            return false; // Unknown after a pause must trigger reapplication.
        }
        var ok = Math.Abs(actual.Value - target.Value.Fraction) <= settings.Number("control_verify_tolerance", .12);
        Log("slider_check", new { kind, value, desired = target.Value.Fraction, actual, ok });
        return ok;
    }

    private void Slider(string kind, double value)
    {
        var target = ResolveSliderTarget(kind, value);
        if (target is null) return;
        var point = target.Value.Point;
        var box = target.Value.Track;
        WaitReady();
        if (experimental)
        {
            Native.MoveAndDown(point.X, point.Y);
            try { Delay(.02); }
            finally { Native.Mouse(true); }
            Delay(.03);
        }
        else
        {
            Native.SetCursorPos(point.X, point.Y);
            Delay(.08);
            Native.Mouse(false);
            try { Delay(.08); }
            finally { Native.Mouse(true); }
            Delay(.15);
        }
        Delay(settings.Number("sequence_delay_ms", 3) / 1000);

        var explicitVerify = settings.Bool("verify_controls");
        var sizeKey = (long)Math.Round(value * 10000);
        var anchored = target.Value.Anchored;
        var verifySizeOnce = kind == "size" && (value > 1.01 || anchored) && !verifiedSizeValues.Contains(sizeKey);
        if ((explicitVerify || verifySizeOnce) && box.Valid)
        {
            var desired = target.Value.Fraction;
            var retries = Math.Clamp(settings.Int("control_verify_retries", 2), 0, 5);
            double? actual = null;
            for (var attempt = 0; attempt <= retries; attempt++)
            {
                Delay(experimental ? .016 : .05);
                actual = DetectSlider(Native.Screenshot(box));
                Log("slider", new { kind, value, desired, actual, attempt, verifySizeOnce });
                if (actual is null)
                    continue; // band not visible yet; re-check without disturbing the slider
                if (Math.Abs(actual.Value - desired) <= settings.Number("control_verify_tolerance", .12))
                {
                    if (verifySizeOnce) verifiedSizeValues.Add(sizeKey);
                    return;
                }
                if (attempt < retries)
                    Click(point, settings.Bool("double_click_controls", true));
            }

            // The green band could not be located at all. The click already used
            // the calibrated min/max anchors (the source of truth), so warn and
            // continue instead of failing the whole transfer.
            if (actual is null)
            {
                Log("slider_unverified", new { kind, value, desired, reason = "green band not found" });
                if (verifySizeOnce) verifiedSizeValues.Add(sizeKey);
                return;
            }

            // The click used a user-captured anchor for this exact size, which is
            // ground truth; the green-band fraction is only a rough guide here, so
            // warn instead of failing the transfer on a mismatch.
            if (anchored)
            {
                Log("slider_anchor_mismatch", new { kind, value, desired, actual });
                verifiedSizeValues.Add(sizeKey);
                return;
            }

            // A confirmed mismatched Size is unsafe even when general control
            // verification is disabled: continuing would paint with the wrong
            // physical radius and make the picture appear shifted/out of place.
            if (explicitVerify || verifySizeOnce)
                throw new InvalidOperationException($"Не підтверджено {kind}. Перевір захоплення min/max повзунка.");
        }
    }

    // Advisory only: returns the filled fraction of the green track, or null
    // when no clear band is visible in the region. Callers must treat null as
    // "cannot verify visually" and fall back to the calibrated anchors rather
    // than as a mismatch.
    internal static double? DetectSlider(PixelImage image)
    {
        var values = new double[image.Width];
        for (var x = 0; x < image.Width; x++)
        {
            double sum = 0;
            var n = 0;
            for (var y = image.Height / 3; y < Math.Max(image.Height / 3 + 1, image.Height * 2 / 3); y++)
            {
                var c = image.Color(y * image.Width + x);
                sum += c.G * 1.2 + c.R * .25 + c.B * .15;
                n++;
            }

            values[x] = sum / Math.Max(1, n);
        }

        var sorted = values.OrderBy(x => x).ToArray();
        var lo = sorted[(int)(sorted.Length * .2)];
        var hi = sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * .8))];
        if (hi - lo < 5)
            return null;
        var threshold = (lo + hi) / 2;
        var last = 0;
        for (var x = 0; x < values.Length; x++)
        {
            var filled = 0;
            for (var k = Math.Max(0, x - 2); k <= Math.Min(values.Length - 1, x + 2); k++)
                if (values[k] >= threshold)
                    filled++;
            if (filled >= 3)
                last = x;
        }

        return last / (double)Math.Max(1, image.Width - 1);
    }

    private double? activeAdaptiveSize;

    private void ApplyBrushShape()
    {
        var cal = settings.PaintCalibration();
        var shapes = cal.GridCenters("brush_shapes", cal.Get("brush_shape_cols", 7), 1);
        var slot = Math.Clamp(settings.Int("brush_shape_slot", 3) - 1, 0, 6);
        if (shapes.Count > slot)
            Click(shapes[slot]);
        else if (cal.Point(settings.Text("brush_shape", "Round") == "Square" ? "square_brush" : "hard_brush") is ScreenPoint shape)
            Click(shape);
    }

    private (double Size, double Interval, double Opacity) DesiredControls(double? sizeOverride = null)
    {
        var speed = SpeedProfile.Get(settings.Text("speed_profile", "Rapid"));
        var precision = settings.Text("coverage_mode", "Precision") == "Precision" && settings.Bool("force_precision_controls", true);
        var size = precision ? speed.BrushSize : settings.Number("brush_size_value", 3);
        if (!precision && settings.Bool("auto_brush_size", true))
            size = BrushValue(Math.Max(1, settings.Int("cell_px", 3)));
        if (sizeOverride.HasValue)
            size = sizeOverride.Value;
        return (size, precision ? .01 : settings.Number("interval_value", .25), settings.Bool("use_fixed_opacity", true) ? settings.Number("paint_opacity_value", 1) : 1);
    }

    public void ApplyControls() => ApplyControls(null);

    private void ApplyControls(double? sizeOverride)
    {
        ApplyBrushShape();
        var desired = DesiredControls(sizeOverride);
        Slider("size", desired.Size);
        Slider("interval", desired.Interval);
        Slider("opacity", desired.Opacity);
        activeAdaptiveSize = desired.Size;
        needsReprime = false;
    }

    private void ReprimeControls(double? sizeOverride)
    {
        // Returning from pause should not blindly re-click all three controls.
        // Verify their current positions and only repair a control that drifted.
        ApplyBrushShape();
        var desired = DesiredControls(sizeOverride);
        var repaired = new List<string>();
        if (!SliderMatches("size", desired.Size)) { Slider("size", desired.Size); repaired.Add("size"); }
        if (!SliderMatches("interval", desired.Interval)) { Slider("interval", desired.Interval); repaired.Add("interval"); }
        if (!SliderMatches("opacity", desired.Opacity)) { Slider("opacity", desired.Opacity); repaired.Add("opacity"); }
        activeAdaptiveSize = desired.Size;
        Log("reprime", new { size = desired.Size, desired.Interval, desired.Opacity, repaired });
    }

    private double BrushValue(double pixels)
    {
        var pts = settings.Data["brush_calibration_points"] as System.Text.Json.Nodes.JsonArray;
        if (pts is null || pts.Count < 2)
            return pixels;
        var list = pts.OfType<System.Text.Json.Nodes.JsonArray>().Where(x => x.Count == 2).Select(x => (Value: x[0]!.GetValue<double>(), Pixels: x[1]!.GetValue<double>())).OrderBy(x => x.Pixels).ToArray();
        if (list.Length < 2)
            return pixels;
        if (pixels <= list[0].Pixels)
            return list[0].Value;
        for (var i = 1; i < list.Length; i++)
            if (pixels <= list[i].Pixels)
                return list[i - 1].Value + (list[i].Value - list[i - 1].Value) * (pixels - list[i - 1].Pixels) / (list[i].Pixels - list[i - 1].Pixels);
        return list[^1].Value;
    }

    private double ColorDelay() => StrokeTiming.ColorDelay(settings);

    public void Run(PaintPlan plan, ResumeCheckpoint? resume = null)
    {
        var groups = AdaptiveBrush.Build(plan, settings);
        var order = groups.Keys.OrderByDescending(i => plan.Counts.GetValueOrDefault(i)).ToList();
        if (plan.BackgroundColor is int bg)
        {
            order.Remove(bg);
            order.Insert(0, bg);
        }

        var total = groups.Values.Sum(x => x.Count);
        var done = resume?.Done ?? 0;
        if (resume is not null)
        {
            if (resume.Group < 0 || resume.Group > order.Count || resume.Line < 0 || done < 0 || done > total
                || resume.Group == order.Count && resume.Line != 0
                || resume.Group < order.Count && resume.Line > groups[order[resume.Group]].Count)
                throw new InvalidDataException("Пошкоджений RESUME. Виконай новий START.");
            var expected = order.Take(resume.Group).Sum(color => groups[color].Count) + resume.Line;
            if (expected != done)
                throw new InvalidDataException("Прогрес RESUME не відповідає плану. Виконай новий START.");
        }
        var speed = SpeedProfile.Get(settings.Text("speed_profile", "Rapid"));
        clock.Start();
        if (resume is null)
            Save(new(plan.Identity, 0, 0, 0));
        var highResolutionTimer = Native.BeginHighResolutionTimer();
        try
        {
            for (var sec = settings.Int("start_delay", 5); sec > 0; sec--)
            {
                token.ThrowIfCancellationRequested();
                report(new(done, total, 0, 0, $"Старт через {sec} с…"));
                for (var j = 0; j < 20; j++)
                {
                    token.ThrowIfCancellationRequested();
                    if (Native.Down(0x1B))
                        throw new OperationCanceledException();
                    Thread.Sleep(50);
                }
            }

            if (!Native.IsRust(window))
                throw new InvalidOperationException("Canvas повинен належати вікну Rust.");
            Native.SetForegroundWindow(window);
            if (Native.GetForegroundWindow() != window)
                throw new InvalidOperationException("Поверни фокус у Rust і повтори START.");
            WaitReady();
            Log("start", new { version = BuildInfo.Version, commit = BuildInfo.GitCommit, buildDate = BuildInfo.BuildDate, inputEngine = settings.Text("input_engine", "Stable"), frameMs = StrokeTiming.Frame(settings) * 1000, releaseMs = StrokeTiming.Release(settings) * 1000, mode = plan.Mode.ToString(), groups = order.Count, strokes = total, canvas = settings.Calibration.Rect("canvas") });
            ApplyControls();
            Log("adaptive_plan", new { enabled = settings.Bool("adaptive_brush"), wide = groups.Values.SelectMany(x => x).Count(x => x.Size > 0), total });
            string? lastHex = null;
            for (var group = resume?.Group ?? 0; group < order.Count; group++)
            {
                var color = order[group];
                var entry = plan.Palette[color];
                WaitReady();
                for (;;)
                    try
                    {
                        if (plan.Mode == ColorMode.HexDirect)
                        {
                            if (lastHex != entry.Color.Hex)
                            {
                                if (!ApplyHex(entry.Color))
                                    throw new InvalidOperationException($"Rust не підтвердив HEX {entry.Color.Hex}. Малювання зупинено.");
                                lastHex = entry.Color.Hex;
                            }
                        }
                        else
                            Click(MapPoint(entry.ClickPoint ?? throw new InvalidOperationException("Колір не має координат палітри.")));
                        Delay(ColorDelay());
                        break;
                    }
                    catch (InputInterrupted)
                    {
                        WaitReady();
                        lastHex = null;
                    }

                Log("color_group", new { group, mode = plan.Mode.ToString(), color = entry.Color.Hex, point = entry.ClickPoint, strokes = groups[color].Count });
                var lines = groups[color];
                for (var line = group == (resume?.Group ?? -1) ? resume!.Line : 0; line < lines.Count; line++)
                {
                    while (true)
                        try
                        {
                            WaitReady();
                            var op = lines[line];
                            var targetSize = settings.Bool("adaptive_brush") ? (op.Size > 0 ? op.Size : speed.BrushSize) : (double?)null;

                            if (needsReprime)
                            {
                                ReprimeControls(targetSize);
                                if (plan.Mode == ColorMode.HexDirect)
                                {
                                    if (!ApplyHex(entry.Color))
                                        throw new InvalidOperationException("HEX verification failed after pause.");
                                    lastHex = entry.Color.Hex;
                                }
                                else
                                    Click(MapPoint(entry.ClickPoint!.Value));
                                needsReprime = false;
                            }

                            if (settings.Bool("adaptive_brush"))
                            {
                                var size = targetSize!.Value;
                                if (activeAdaptiveSize != size)
                                {
                                    Slider("size", size);
                                    activeAdaptiveSize = size;
                                    Log("adaptive_size", new { group, line, size, wide = op.Size > 0 });
                                }
                            }
                            Draw(op.Line, speed, line % 2 == 1);
                            break;
                        }
                        catch (InputInterrupted)
                        {
                            Native.Release();
                            WaitReady();
                        }

                    done++;
                    var state = new ResumeCheckpoint(plan.Identity, group, line + 1, done);
                    if (line + 1 == lines.Count)
                        state = state with
                        {
                            Group = group + 1,
                            Line = 0
                        };
                    if (done % 50 == 0 || line + 1 == lines.Count)
                        Save(state);
                    if (done % 12 == 0 || done == total)
                    {
                        var elapsed = clock.Elapsed.TotalSeconds - pausedSeconds;
                        report(new(done, total, elapsed, done > 0 ? elapsed * (total - done) / done : 0, $"#{entry.Color.Hex}"));
                    }
                }
            }

            if (Native.GetForegroundWindow() == window)
                Slider("opacity", 1);
            if (File.Exists(checkpointPath))
                File.Delete(checkpointPath);
            report(new(total, total, clock.Elapsed.TotalSeconds - pausedSeconds, 0, "Готово"));
            Log("complete", new { done, total });
        }
        finally
        {
            Native.Release();
            if (highResolutionTimer)
                Native.EndHighResolutionTimer();
        }
    }

    private void Save(ResumeCheckpoint state)
    {
        var temp = checkpointPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state));
        File.Move(temp, checkpointPath, true);
    }

    private void Draw(ScreenLine line, SpeedProfile speed, bool reverse)
    {
        var a = new ScreenPoint(reverse ? line.X2 : line.X1, reverse ? line.Y2 : line.Y1);
        var b = new ScreenPoint(reverse ? line.X1 : line.X2, reverse ? line.Y1 : line.Y2);
        var length = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
        var shift = settings.Bool("line_mode") && settings.Text("coverage_mode") == "Fast" && length >= settings.Int("min_line_width", 4) * settings.Int("cell_px", 3);
        Native.SetCursorPos(a.X, a.Y);
        var frame = StrokeTiming.Frame(settings);
        Delay(StrokeTiming.Settle(settings, speed));
        if (shift)
            Native.Key(0x10);
        Native.Mouse(false);
        try
        {
            Delay(frame);
            var batch = new List<ScreenPoint>();
            if (shift)
            {
                Native.SetCursorPos(b.X, b.Y);
                Delay(settings.Number("stroke_speed", .028) * Math.Max(1, length) / 100);
            }
            else
                for (var step = speed.Pitch; step < length + speed.Pitch; step += speed.Pitch)
                {
                    Check();
                    if (Paused)
                        throw new InputInterrupted();
                    var f = Math.Min(step, length) / (double)Math.Max(1, length);
                    batch.Add(new((int)Math.Round(a.X + (b.X - a.X) * f), (int)Math.Round(a.Y + (b.Y - a.Y) * f)));
                    if (batch.Count >= speed.BatchSize || step >= length)
                    {
                        Native.MovePath(batch);
                        Delay(speed.PointDelay * batch.Count);
                        batch.Clear();
                    }
                }

            Delay(StrokeTiming.EndHold(settings, speed));
        }
        finally
        {
            Native.Mouse(true);
            if (shift)
                Native.Key(0x10, true);
        }

        Delay(StrokeTiming.Release(settings));
    }
}
