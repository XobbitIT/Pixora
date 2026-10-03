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
    private int hexChangesSinceReadback;
    private bool needsReprime;
    private bool f6Held;
    private long nextSafety;
    private double pausedSeconds;
    private volatile bool paused;
    private readonly Dictionary<string,double> verifiedControls = new();
    public bool Paused { get => paused; set => paused = value; }

    public Painter(Settings s, IntPtr target, string checkpoint, string log, Action<PaintProgress> callback, CancellationToken cancel)
    {
        settings = s.Clone();
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
        verifiedControls.Clear();
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
                verifiedControls.Clear();
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
            Native.SetCursorPos(p.X, p.Y);
            Delay(StrokeTiming.ClickSettle(settings));
            Native.Mouse(false);
            try { Delay(StrokeTiming.ClickHold(settings)); }
            finally { Native.Mouse(true); }
            Delay(StrokeTiming.ClickRelease(settings,twice));
        }
    }

    private void PressKey(int key)
    {
        Native.Key(key);
        try { Delay(StrokeTiming.KeyHold(settings)); }
        finally { Native.Key(key, true); }
        Delay(StrokeTiming.KeyRelease(settings));
    }

    private void ChordKey(int modifier, int key)
    {
        Native.Key(modifier);
        try { Delay(StrokeTiming.ModifierSettle(settings)); PressKey(key); }
        finally { Native.Key(modifier, true); }
        Delay(StrokeTiming.ModifierRelease(settings));
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
        var readback = new HexReadback(expected);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            Click(p);
            ChordKey(0x11, 0x41);
            WriteClipboard(HexReadback.Marker);
            ChordKey(0x11, 0x43);
            Delay(StrokeTiming.CopyDelay(settings) + attempt * .05);
            var raw = ReadClipboard();
            var value = HexReadback.Normalize(raw);
            Log("hex_readback", new { strategy = "select_all", attempt, status = readback.Status(raw), raw, value });
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
                Delay(StrokeTiming.Fast(settings)?StrokeTiming.Frame(settings):.05);
                ChordKey(0x11, 0x41);
                var payload = settings.Bool("hex_include_hash", false) ? "#" + target : target;
                WriteClipboard(payload);
                ChordKey(0x11, 0x56);
                Delay(StrokeTiming.HexPaste(settings));
                PressKey(0x0D);
                Delay(StrokeTiming.HexCommit(settings) + attempt * .10);

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
                    colorOk = verify && swatch.Valid ? (bool?)colorOk : null,
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

    private SliderObservation? ReadSlider(string kind)
    {
        var hint = settings.PaintCalibration().Rect(kind + "_track");
        if (!hint.Valid) return null;
        var bounds = new ScreenRect(
            Math.Max(windowRect.Left, hint.Left - Math.Max(48, hint.Width / 4)),
            Math.Max(windowRect.Top, hint.Top - 12),
            Math.Min(windowRect.Right, hint.Right + Math.Max(120, hint.Width / 2)),
            Math.Min(windowRect.Bottom, hint.Bottom + 12));
        if (!bounds.Valid) return null;
        var local = new ScreenRect(hint.Left - bounds.Left, hint.Top - bounds.Top,
            hint.Right - bounds.Left, hint.Bottom - bounds.Top);
        var read = RustSlider.Read(Native.Screenshot(bounds), local);
        if (read is not { } observation) return null;
        var r = observation.Track;
        var field = observation.ValueField;
        return observation with
        {
            Track = new(r.Left + bounds.Left, r.Top + bounds.Top, r.Right + bounds.Left, r.Bottom + bounds.Top),
            ValueField = new(field.Left + bounds.Left, field.Top + bounds.Top, field.Right + bounds.Left, field.Bottom + bounds.Top)
        };
    }

    private double? ReadControlNumber(string kind, ScreenRect field)
    {
        Click(field.Center);
        ChordKey(0x11, 0x41);
        // Overwrite the pasted payload before Ctrl+C. A missed copy must fail.
        WriteClipboard(ControlNumber.Marker);
        ChordKey(0x11, 0x43);
        Delay(StrokeTiming.CopyDelay(settings));
        var raw = ReadClipboard();
        var number = ControlNumber.Parse(kind, raw);
        PressKey(0x0D);
        Log("control_readback", new { kind, raw, number, fresh = raw != ControlNumber.Marker });
        return number;
    }

    private bool VerifyControlNumber(string kind, double value, SliderObservation geometry)
    {
        var number = ReadControlNumber(kind, geometry.ValueField);
        Native.SetCursorPos(geometry.Track.Left - 12, geometry.Track.Center.Y);
        Delay(StrokeTiming.CursorPark(settings));
        var after = ReadSlider(kind);
        var ok = ControlNumber.Matches(number, value) && after is { } result
            && result.Matches(ControlCurve.Fraction(kind, value));
        Log("slider_check", new { kind, value, number, actual = after?.Fraction, ok, strategy = "numeric_field" });
        if(ok)verifiedControls[kind]=value;else verifiedControls.Remove(kind);
        return ok;
    }

    private bool SliderMatches(string kind, double value)
    {
        var previous = ReadClipboard();
        try { return ReadSlider(kind) is { } geometry && VerifyControlNumber(kind, value, geometry); }
        finally { RestoreClipboard(previous); }
    }

    private void Slider(string kind, double value)
    {
        WaitReady();
        var payload = ControlNumber.Format(kind, value);
        if(StrokeTiming.Fast(settings)&&verifiedControls.TryGetValue(kind,out var known)&&known==value
            &&ReadSlider(kind) is { } current&&current.Matches(ControlCurve.Fraction(kind,value)))
        {
            Log("control_unchanged",new{kind,value,verifiedEarlier=true});return;
        }
        verifiedControls.Remove(kind);
        var previous = ReadClipboard();
        try
        {
            var retries = Math.Clamp(settings.Int("control_verify_retries", 2), 2, 5);
            for (var attempt = 0; attempt <= retries; attempt++)
            {
                var geometry = ReadSlider(kind) ?? throw new InvalidOperationException(
                    $"Не вдалося прочитати {kind}. Захопи повзунок із числом справа.");
                Log("control_target", new { kind, value, point = geometry.ValueField.Center, field = geometry.ValueField, strategy = "numeric_field", attempt });
                Click(geometry.ValueField.Center);
                ChordKey(0x11, 0x41);
                WriteClipboard(payload);
                ChordKey(0x11, 0x56);
                PressKey(0x0D);
                Delay(StrokeTiming.ControlCommit(settings) + attempt * .10);
                var ok = VerifyControlNumber(kind, value, geometry);
                Log("slider", new { kind, value, attempt, strategy = "numeric_field", verified = ok });
                if (ok) return;
            }
            throw new InvalidOperationException($"Не підтверджено число {kind}. Перевір числове поле справа й повтори тест controls.");
        }
        finally { RestoreClipboard(previous); }
    }

    private static void RestoreClipboard(string? previous)
    {
        if (previous is not null) try { Native.ClipboardWrite(previous); } catch { }
    }

    public Rgb SelectCalibrationColor(Rgb background)
    {
        var cal = settings.PaintCalibration();
        Click(cal.Point("brush_tool") ?? throw new InvalidOperationException("Захопи інструмент пензля через налаштування Rust."));
        ApplyBrushShape();
        if (settings.Mode == ColorMode.HexDirect)
        {
            var color = RustSlider.Delta(background, new(0, 0, 0)) > RustSlider.Delta(background, new(255, 255, 255))
                ? new Rgb(0, 0, 0) : new Rgb(255, 255, 255);
            if (!ApplyHex(color, true)) throw new InvalidOperationException("Не підтверджено контрастний HEX-колір калібрування.");
            return color;
        }
        var entry = settings.Palette().Where(x => x.ClickPoint.HasValue)
            .OrderByDescending(x => RustSlider.Delta(x.Color, background)).FirstOrDefault()
            ?? throw new InvalidOperationException("Захопи палітру Rust.");
        if (RustSlider.Delta(entry.Color, background) < 40)
            throw new InvalidOperationException("У палітрі немає контрастного кольору для цього Canvas.");
        ApplyPalette(entry);
        return entry.Color;
    }

    private void ApplyPalette(PaletteEntry entry)
    {
        var point = MapPoint(entry.ClickPoint ?? throw new InvalidOperationException("Колір не має координат палітри."));
        var swatch = settings.Calibration.Rect("palette_swatch");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            Click(point);
            Delay(ColorDelay() + attempt * .10);
            if (!swatch.Valid)
            {
                Log("palette", new { target = entry.Color.Hex, point, attempt, verified = false, reason = "no active color swatch captured" });
                return;
            }
            var actual = Native.Median(swatch);
            var ok = RustSlider.Delta(actual, entry.Color) <= Math.Clamp(settings.Int("hex_verify_tolerance", 22), 0, 40);
            Log("palette", new { target = entry.Color.Hex, actual = actual.Hex, point, attempt, verified = ok });
            if (ok) return;
        }
        throw new InvalidOperationException($"Rust не підтвердив колір палітри {entry.Color.Hex}. Перевір зразок активного кольору.");
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
        Log("reprime", new { size = desired.Size, desired.Interval, desired.Opacity, repaired, verified = true });
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
        var groups = TransferSchedule.Build(plan, settings);
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
        double nextReport=0;
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
            Log("start", new { version = BuildInfo.Version, commit = BuildInfo.GitCommit, buildDate = BuildInfo.BuildDate, inputEngine = settings.Text("input_engine", "Stable"), fastTransfer = settings.Bool("fast_transfer"), fastMotion = TransferSchedule.Fast(settings), moveSpan = TransferSchedule.MoveSpan(settings), frameMs = StrokeTiming.Frame(settings) * 1000, releaseMs = StrokeTiming.Release(settings) * 1000, mode = plan.Mode.ToString(), groups = order.Count, batches = total, sourceStrokes = groups.Values.SelectMany(x=>x).Sum(x=>x.SourceStrokes), canvas = settings.Calibration.Rect("canvas") });
            for (;;)
                try { ApplyControls(); break; }
                catch (InputInterrupted) { WaitReady(); }
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
                            ApplyPalette(entry);
                        if (plan.Mode == ColorMode.HexDirect)
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
                                    ApplyPalette(entry);
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
                            if(TransferSchedule.Fast(settings))DrawBatch(op,speed);
                            else Draw(op.Segments[0], speed, line % 2 == 1);
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
                    if (clock.Elapsed.TotalSeconds>=nextReport || done == total)
                    {
                        nextReport=clock.Elapsed.TotalSeconds+.25;
                        var elapsed = clock.Elapsed.TotalSeconds - pausedSeconds;
                        report(new(done, total, elapsed, done > 0 ? elapsed * (total - done) / done : 0, $"#{entry.Color.Hex}"));
                    }
                }
            }

            if (Native.GetForegroundWindow() == window)
                Slider("opacity", 1);
            if (File.Exists(checkpointPath))
                File.Delete(checkpointPath);
            report(new(total, total, clock.Elapsed.TotalSeconds - pausedSeconds, 0, "Команди виконано. Перевір результат у Rust."));
            Log("complete", new { done, total, elapsedSeconds=clock.Elapsed.TotalSeconds-pausedSeconds, fastTransfer=settings.Bool("fast_transfer"), resultVerified = false, paletteSwatchAvailable = settings.Calibration.Rect("palette_swatch").Valid });
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
        var heldFrom = Stopwatch.GetTimestamp();
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
            var held = (Stopwatch.GetTimestamp() - heldFrom) / (double)Stopwatch.Frequency;
            if (held < .04) Delay(.04 - held);
        }
        finally
        {
            Native.Mouse(true);
            if (shift)
                Native.Key(0x10, true);
        }

        Delay(StrokeTiming.Release(settings));
    }

    private void DrawBatch(PaintBatch batch,SpeedProfile speed)
    {
        var first=batch.Segments[0];
        Native.SetCursorPos(first.X1,first.Y1);
        Delay(StrokeTiming.Settle(settings,speed));
        Native.Mouse(false);
        var heldFrom=Stopwatch.GetTimestamp();
        try
        {
            Delay(StrokeTiming.Frame(settings));
            foreach(var line in batch.Segments)
            {
                int length=TransferSchedule.Length(line);
                int steps=Math.Max(1,(int)Math.Ceiling(length/(double)TransferSchedule.MoveSpan(settings)));
                for(int step=1;step<=steps;step++)
                {
                    double fraction=step/(double)steps;
                    Native.SetCursorPos((int)Math.Round(line.X1+(line.X2-line.X1)*fraction),
                        (int)Math.Round(line.Y1+(line.Y2-line.Y1)*fraction));
                    // Each end/bend remains visible for at least one game tick.
                    Delay(StrokeTiming.Frame(settings));
                }
            }
            var held=(Stopwatch.GetTimestamp()-heldFrom)/(double)Stopwatch.Frequency;
            if(held<.04)Delay(.04-held);
        }
        finally{Native.Mouse(true);}
        Delay(StrokeTiming.Release(settings));
    }
}
