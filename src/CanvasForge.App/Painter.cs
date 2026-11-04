using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using CanvasForge.Core;

namespace CanvasForge.App;
internal sealed record PaintProgress(int Done, int Total, double Elapsed, double Eta, string Status,
    EtaEstimate? Estimate=null,PaintPhase Phase=PaintPhase.Drawing,
    PhysicalCoverageResult? PhysicalCoverage=null,string? CoverageDirectory=null,RunTimeSummary? Times=null,bool TimesOnly=false);
internal sealed partial class Painter : IDisposable
{
    private readonly Settings settings;
    private readonly PixelImage? diagnosticSource;
    private string? runSnapshotDirectory,runSnapshotProblem,runIdentity;
    private string? runTargetIdentity;
    private bool runSnapshotComplete;
    private long runSnapshotSequence;
    private DateTimeOffset? runStartedUtc,runStateChangedUtc;
    private string? runSnapshotOutcome;
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
    private InputDelay? inputDelay;
    private string? pauseReason;
    private bool probeMode;
    private long safetyCalls,moveCalls;
    private double safetySeconds,sendInputSeconds;
    private double pausedSeconds;
    private volatile bool paused;
    private readonly Dictionary<string,double> verifiedControls = new();
    private readonly ICalibratedStrokeInput motionInput;
    private readonly InputCommandGuard coordinateGuard;
    private CheckpointJournal? checkpoint;
    private ScreenPoint? lastPaintPoint;
    private ClipboardLease? clipboardLease;
    private bool textRecovery;
    private StrokeExecutionPlan? executionPlan;
    private StrokeExecutionPlan Execution=>executionPlan??=new(settings);
    private ScreenPoint? lastFieldRequested,lastFieldObserved;
    public bool Paused { get => paused; set => paused = value; }
    private InputDelay DelayTimer => inputDelay??=new();
    public void Dispose()=>inputDelay?.Dispose();

    public Painter(Settings s, IntPtr target, string checkpoint, string log, Action<PaintProgress> callback, CancellationToken cancel,bool preparation=false,PixelImage? diagnosticSource=null,
        string? snapshotDirectory=null,DateTimeOffset? requestedUtc=null)
    {
        this.diagnosticSource=diagnosticSource;
        runSnapshotDirectory=snapshotDirectory;runStartedUtc=requestedUtc;
        runSnapshotSequence=snapshotDirectory is null?0:1;
        probeMode=preparation;
        if(TexelExperimentSettings.Enabled(s)&&TexelProductSession.Requested(s))TexelProduct.ValidateSettings(s);
        settings = DrawingWorkflow.Effective(s);
        LoadTransportTiming(settings,Path.GetDirectoryName(log)!);
        window = target;
        token = cancel;
        report = callback;
        checkpointPath = checkpoint;
        logPath = log;
        strokeCursor=new(ReadGuardCursor,Native.Release);
        inputEnvironment=new(ReadGuardCursor,Native.WindowAt,Native.GetForegroundWindow,Native.Release,Native.Mouse);
        if (!Native.IsRust(target) || !Native.GetWindowRect(target, out var r))
            throw new InvalidOperationException("Не знайдено вікно Rust. Переконайся, що гра відкрита, і повтори захоплення.");
        windowRect = r.ToScreen();
        windowProcessId = Native.ProcessIdOf(target);
        InputIntegrity.Verify(windowProcessId);
        windowDpi = Native.DpiOf(target);
        rebase = SessionRebase();
        settings=BrushFootprints.Snapshot(settings);
        coordinateGuard=InputCommandGuard.From(settings,windowRect);
        motionInput = new GuardedStrokeInput(this);
    }

    // Rebases the captured absolute coordinates from the Rust client origin that
    // was recorded at calibration time onto the window's current position/DPI.
    // Strokes and controls read from settings.Calibration, so shifting those
    // keys fixes them; the palette click points baked into the plan are mapped
    // separately via PaletteSelection.ClickPoint. When the window has not moved (or no baseline was
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

    private void Log(string action, object? details = null)
    {
        try
        {
            SessionLog.Write(logPath,action,details,settings?.Bool("diagnostic_debug")??false);
        }
        catch
        {
        }
    }

    private void Check(bool forceGeometry=false)
    {
        long started=Stopwatch.GetTimestamp();safetyCalls++;
        try
        {
        token.ThrowIfCancellationRequested();
        try{paletteHover?.CheckDeadline();}catch{Native.Release();throw;}
        if (Native.Down(0x1B))
        {
            Log("input_interruption",new{reason="escape"});
            throw new OperationCanceledException("ESC");
        }
        HandlePauseHotkey(Native.Down(0x75));
        var foreground=Native.GetForegroundWindow();
        if (foreground != window&&!Paused)
        {
            InputFocus.Pause(window,foreground,()=>{Paused=true;pauseReason="focus_lost";},Native.Release,
                evidence=>Log("input_pause",new{paused=true,reason=pauseReason,evidence.Active,evidence.Expected}));
        }
        // Preserve explicit pause/focus reasons and their existing recovery.
        // A cursor move into another app must not turn focus_lost into a new
        // cursor pause that disables focus recovery.
        if(!Paused)CheckStrokeCursor("safety_check");
        CheckEditorHeartbeat();
        if(!Paused)CheckStrokeCursor("after_editor_check");
        if (!forceGeometry&&Environment.TickCount64 < nextSafety)
            return;
        nextSafety = Environment.TickCount64 + 75;
        if (Native.ProcessIdOf(window) != windowProcessId || !Native.IsRust(window))
            throw new InvalidOperationException("Не знайдено вікно Rust. Переконайся, що гра відкрита, і повтори захоплення.");
        if (Native.DpiOf(window) != windowDpi)
            throw new InvalidOperationException(CalibrationSession.DpiChangedMessage);
        if (!Native.GetWindowRect(window, out var rect) || rect.ToScreen() != windowRect)
            throw new InvalidOperationException("Вікно Rust змінило розмір або положення. Повтори захоплення.");
        }
        finally{safetySeconds+=Stopwatch.GetElapsedTime(started).TotalSeconds;}
    }

    internal void HandlePauseHotkey(bool pressed)
    {
        if(pressed&&!f6Held)
        {
            Paused=!Paused;pauseReason=Paused?"f6":null;
            Log("input_pause",new{paused=Paused,reason="f6"});
        }
        f6Held=pressed;
    }

    // Called after native buttons are released, before waiting for recovery.
    internal void BeginRunPauseObservation()
    {
        runTimes?.Pause();
        SaveRunSnapshotMetadata("paused");
    }

    // All input waits are interruptible; never retain mouse-down while paused.
    private bool WaitReady()
        => RetryInputWait(WaitReadyAttempt,Native.Release);

    // A fresh editor proof can itself pause. It must return to the wait loop,
    // including when called from an interruption handler, rather than escape
    // that handler and terminate the run with a modal error.
    internal static bool RetryInputWait(Func<bool> attempt,Action release)
    {
        for(;;)
            try{return attempt();}
            catch(InputInterrupted){release();}
    }

    private bool WaitReadyAttempt()
    {
        Check();
        if(probeMode&&Paused)CheckProbe();
        if (!Paused)
            return false;
        Native.Release();
        verifiedControls.Clear();
        FlushCheckpoint("pause");
        var start = clock.Elapsed.TotalSeconds;
        string pauseMessage=pauseReason switch
        {
            "palette_swatch_unavailable"=>PaletteSwatchPauseMessage,
            "palette_hover_timeout"=>"Palette selection timed out. Return to Rust and repeat the color check.",
            "coordinate_out_of_bounds" or "foreign_window_on_route"=>"Координата поза полотном або перекрита іншим вікном. Зупини малювання та повтори захоплення областей Rust.",
            "editor_reference_missing" or "editor_reference_invalid" or "editor_landmarks_unavailable" or "editor_geometry_changed"
                =>RecoveryMessages.Editor(lastEditorFailure),
            var reason when reason?.StartsWith("editor_structure_",StringComparison.Ordinal)==true=>RecoveryMessages.Editor(lastEditorFailure),
            var reason when reason?.StartsWith("tool_",StringComparison.Ordinal)==true=>RecoveryMessages.Editor(lastEditorFailure),
            "editor_canvas_recapture_required"=>RecoveryMessages.Editor(lastEditorFailure),
            "cursor_moved"=>RecoveryMessages.CursorMoved,
            "cursor_position_mismatch"=>RecoveryMessages.CursorPosition,
            _=>"Пауза — повернись у Rust і натисни F6."
        };
        report(new(0, 0, 0, 0,pauseMessage));
        BeginRunPauseObservation();
        double nextSnapshot=clock.Elapsed.TotalSeconds+1;
        try
        {
        while (Paused)
        {
            token.ThrowIfCancellationRequested();
            if (Native.Down(0x1B))
                throw new OperationCanceledException();
            var f = Native.Down(0x75);
            if (f && !f6Held && Native.GetForegroundWindow() == window)
                Paused = false;
            f6Held = f;
            if(Paused&&clock.Elapsed.TotalSeconds>=nextSnapshot)
            {SaveRunSnapshotMetadata("paused");nextSnapshot=clock.Elapsed.TotalSeconds+1;}
            Thread.Sleep(15);
        }

        }
        finally{pausedSeconds += clock.Elapsed.TotalSeconds - start;runTimes?.Resume();}
        pauseReason=null;
        nextSafety = 0;
        Check();
        if (Paused)
            throw new InputInterrupted();
        RequireEditorProof();
        needsReprime = true;
        SaveRunSnapshotMetadata("running");
        return true;
    }

    private void Delay(double seconds)
    {
        DelayTimer.Wait(seconds,()=>
        {
            Check();
            if (Paused)
            {
                Native.Release();
                verifiedControls.Clear();
                if(probeMode)CheckProbe();
                throw new InputInterrupted();
            }
        });
    }

    internal sealed class InputInterrupted : PreparationInterruptedException
    {
        internal const string MessageText="Ввід призупинено: Rust втратив фокус або натиснуто F6. Повернись у Rust і повтори перевірку; під час малювання натисни F6 для продовження.";
        public InputInterrupted():base(MessageText){}
    }

    private void CheckBoundary()
    {
        Check(true);
        if(Paused){if(probeMode)CheckProbe();throw new InputInterrupted();}
        if(!editorValidationEnabled&&!provingEditor)RequireEditorProof();
    }

    private void Click(ScreenPoint p, bool twice = false,bool waitForReady=true)
    {
        for (var i = 0; i < (twice ? 2 : 1); i++)
        {
            if(waitForReady)WaitReady();
            CheckBoundary();
            MoveIntentCursor(p,InputIntent.UserInterface);
            Delay(TextDelay(StrokeTiming.ClickSettle(settings)));
            CheckBoundary();
            GuardedNativeInput.Button(coordinateGuard,false,InputIntent.UserInterface,CheckBoundary,CoordinateDenied);
            try { Delay(TextDelay(StrokeTiming.ClickHold(settings))); }
            finally { Native.Mouse(true); }
            Delay(TextDelay(StrokeTiming.ClickRelease(settings,twice)));
        }
    }

    private double TextDelay(double seconds)=>textRecovery?Math.Max(.08,seconds):seconds;
    private void SelectTextField(ScreenPoint point,int attempt)
    {
        textRecovery=attempt>0;
        Click(point,twice:textRecovery);
        if(textRecovery)Delay(.12);
        lastFieldRequested=point;lastFieldObserved=Native.Cursor();
        Log("text_field_selection",new{requested=point,observed=lastFieldObserved,attempt,recovery=textRecovery,
            transport="guarded SendInput",dpi=windowDpi});
    }

    private void PressKey(int key)
    {
        CheckBoundary();
        Native.Key(key);
        try { Delay(TextDelay(StrokeTiming.KeyHold(settings))); }
        finally { Native.Key(key, true); }
        Delay(TextDelay(StrokeTiming.KeyRelease(settings)));
    }

    private void ChordKey(int modifier, int key)
    {
        CheckBoundary();
        if(modifier==0x11&&key==0x43)clipboardLease?.ExpectCopy();
        Native.Key(modifier);
        try { Delay(TextDelay(StrokeTiming.ModifierSettle(settings))); PressKey(key); }
        finally { Native.Key(modifier, true); }
        Delay(TextDelay(StrokeTiming.ModifierRelease(settings)));
    }



    private void WriteClipboard(string text)
    {
        try
        {
            ClipboardRetry.Run(() => { (clipboardLease??throw new InvalidOperationException("Clipboard backup is required.")).Write(text); return true; }, () => Delay(.05));
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Log("clipboard", new { status = "write_failed" });
            throw; // Never paste if the new payload was not written.
        }
    }

    private string? ReadClipboard()
    {
        try { return ClipboardRetry.Run(()=> (clipboardLease??throw new InvalidOperationException("Clipboard backup is required.")).Read(), () => Delay(.05)); }
        catch (System.ComponentModel.Win32Exception)
        {
            Log("clipboard", new { status = "read_failed" });
            throw;
        }
    }

    private string? ReadHex(ScreenPoint p, Rgb expected)
    {
        var readback = new HexReadback(expected);
        bool previousRecovery=textRecovery;
        try
        {
            var result=readback.Read(new NumberReadbackInput(this,new(p.X,p.Y,p.X+1,p.Y+1),textRecovery),StrokeTiming.CopyDelay(settings),
                observation=>Log("hex_readback",new{strategy="select_all",attempt=observation.Attempt,poll=observation.Poll,
                    status=observation.Status,raw=ReadbackDiagnostics.SafeText(observation.Raw),value=observation.Value}),CalibrationReliability.Readback(settings));
            return result.Value;
        }
        finally{textRecovery=previousRecovery;}
    }

    public bool ApplyHex(Rgb rgb, bool forceVerify = false)
    {
        var point = settings.Calibration.HexPoint ?? throw new InvalidOperationException("Захопи поле HEX.");
        var target = rgb.Hex;
        var previous = BeginClipboard();
        bool previousRecovery=textRecovery;
        try
        {
            var maxAttempts = HexReadback.AttemptCount(settings.Int("hex_verify_retries", 2));
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                WaitReady();
                SelectTextField(point,attempt);
                Delay(StrokeTiming.Fast(settings)?StrokeTiming.ControlFrame(settings):.05);
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
            textRecovery=previousRecovery;
            RestoreClipboard(previous);
        }
    }

    private SliderObservation? ReadSlider(string kind)
        =>ReadSlider(kind,settings.PaintCalibration());
    private SliderObservation? ReadSlider(string kind,Calibration calibration)
    {
        var hint = calibration.Rect(kind + "_track");
        if (!hint.Valid) return null;
        ParkForControlCapture(calibration);
        var bounds = ControlCapture.Bounds(hint,windowRect);
        if (!bounds.Valid) return null;
        var local = new ScreenRect(hint.Left - bounds.Left, hint.Top - bounds.Top,
            hint.Right - bounds.Left, hint.Bottom - bounds.Top);
        var screenshot=Native.Screenshot(bounds);
        var read = RustSlider.Read(screenshot, local);
        if (read is not { } observation)
        {
            var diagnosis=RustSlider.Diagnose(screenshot,local);
            string? folder=null;
            try{folder=SliderDiagnostics.Save(Path.GetDirectoryName(Path.GetFullPath(logPath))!,kind,screenshot,bounds,local,diagnosis);}
            catch(Exception error){Log("slider_diagnostics_error",new{kind,error=error.GetType().Name});}
            Log("slider_read_failed",new{kind,bounds,hint,reason=diagnosis.Reason,diagnosis,folder,dpi=windowDpi});
            return null;
        }
        var r = observation.Track;
        var field = observation.ValueField;
        return observation with
        {
            Track = new(r.Left + bounds.Left, r.Top + bounds.Top, r.Right + bounds.Left, r.Bottom + bounds.Top),
            ValueField = new(field.Left + bounds.Left, field.Top + bounds.Top, field.Right + bounds.Left, field.Bottom + bounds.Top)
        };
    }

    private sealed class NumberReadbackInput(Painter owner,ScreenRect field,bool recovery=false):IControlReadbackInput
    {
        public void SelectField()=>owner.Click(field.Center);
        public void SelectField(int attempt)=>owner.SelectTextField(field.Center,recovery?Math.Max(1,attempt):attempt);
        public void SelectAll()=>owner.ChordKey(0x11,0x41);
        public void WriteMarker(string marker)=>owner.WriteClipboard(marker);
        public void Copy()=>owner.ChordKey(0x11,0x43);
        public void Wait(double seconds)=>owner.Delay(seconds);
        public string? Read()=>owner.ReadClipboard();
        public void Commit()=>owner.PressKey(0x0D);
    }
    private double? ReadControlNumber(string kind,ScreenRect field,double expected)
    {
        bool previousRecovery=textRecovery;
        try
        {
            var result=ControlReadback.Read(kind,expected,new NumberReadbackInput(this,field,textRecovery),StrokeTiming.CopyDelay(settings),
                observation=>Log("control_readback_poll",new{kind,expected,observation=observation with{Raw=ReadbackDiagnostics.SafeText(observation.Raw,kind)}}),CalibrationReliability.Readback(settings));
            Log("control_readback",new{kind,raw=ReadbackDiagnostics.SafeText(result.Raw,kind),number=result.Number,fresh=result.Raw is not null&&result.Raw!=ControlNumber.Marker,
                verified=result.Verified,attempts=result.Attempts,reads=result.Reads});
            if(!result.Verified&&result.Number is null)
            {
                Log("control_copy_unavailable",new{kind,expected,field,attempts=result.Attempts,reads=result.Reads,
                    markerPending=result.Raw==ControlNumber.Marker,raw=ReadbackDiagnostics.SafeText(result.Raw,kind),
                    requested=lastFieldRequested,observed=lastFieldObserved,clickMatched=lastFieldRequested is not null&&lastFieldRequested==lastFieldObserved,
                    budget=CalibrationReliability.Readback(settings),dpi=windowDpi,foreground=Native.GetForegroundWindow()==window});
                // The reader already reselected/copied three times. Do not
                // start a new paste while the final response is still pending.
                throw new InvalidOperationException(ControlCopyProblem(kind));
            }
            return result.Verified?result.Number:null;
        }
        finally{textRecovery=previousRecovery;}
    }

    private bool VerifyControlNumber(string kind, double value, SliderObservation geometry)
    {
        bool visual=CalibrationReliability.Visual(settings);
        var number = VisualControlVerification.ReadNumber(settings,()=>ReadControlNumber(kind,geometry.ValueField,value));
        VisualControlEvidence? evidence=visual?VisualControlVerification.Read(kind,value,()=>ReadSlider(kind),Delay,CalibrationReliability.StableInterval(settings)):null;
        var after = visual?evidence!.Second:ReadSlider(kind);
        var ok = visual?evidence!.Verified:ControlNumber.Matches(number, value) && after is { } result
            && result.Matches(ControlCurve.Fraction(kind, value));
        Log("slider_check", new { kind, value, number, actual = after?.Fraction, ok,
            strategy = visual?"visual_slider":"numeric_field",numericReadback=!visual,evidence,physicalFootprintRequired=visual });
        if(ok)verifiedControls[kind]=value;else verifiedControls.Remove(kind);
        return ok;
    }

    private bool SliderMatches(string kind, double value)
    {
        if(CalibrationReliability.Visual(settings))
            return ReadSlider(kind) is {} observed&&VerifyControlNumber(kind,value,observed);
        var previous = BeginClipboard();
        try { return ReadSlider(kind) is { } geometry && VerifyControlNumber(kind, value, geometry); }
        finally { RestoreClipboard(previous); }
    }

    private void Slider(string kind, double value)
    {
        if (kind != "size") throw new ArgumentException("Only Size is transported.", nameof(kind));
        WaitReady();
        var payload = ControlNumber.Format(kind, value);
        if(StrokeTiming.Fast(settings)&&verifiedControls.TryGetValue(kind,out var known)&&known==value
            &&ReadSlider(kind) is { } current&&current.Matches(ControlCurve.Fraction(kind,value)))
        {
            Log("control_unchanged",new{kind,value,verifiedEarlier=true});return;
        }
        verifiedControls.Remove(kind);
        if(CalibrationReliability.Visual(settings))
        {
            bool previousVisualRecovery=textRecovery;
            try
            {
                var result=VisualSizeEntry.Ensure(settings,value,()=>ReadSlider(kind),(geometry,text,attempt)=>
                {
                    Log("control_target",new{kind,value,point=geometry.ValueField.Center,field=geometry.ValueField,strategy="unicode_keys",attempt,clipboardUsed=false});
                    SelectTextField(geometry.ValueField.Center,attempt);ChordKey(0x11,0x41);
                    VisualSizeEntry.Type(text,()=>CheckBoundary(),Native.UnicodeKey,Delay,TextDelay(StrokeTiming.KeyHold(settings)),TextDelay(StrokeTiming.KeyRelease(settings)));
                    PressKey(0x0D);Delay(StrokeTiming.ControlCommit(settings)+attempt*.10);
                },Delay,(e,writes)=>Log("slider_check",new{kind,value,number=(double?)null,actual=e.Second?.Fraction,ok=e.Verified,
                    strategy="visual_slider",numericReadback=false,evidence=e,physicalFootprintRequired=true,clipboardUsed=false,writes}));
                verifiedControls[kind]=value;
                Log(result.Writes==0?"control_unchanged":"slider",new{kind,value,strategy="visual_slider",verified=true,clipboardUsed=false,writes=result.Writes});return;
            }
            finally{textRecovery=previousVisualRecovery;}
        }
        var previous = BeginClipboard();
        bool previousRecovery=textRecovery;
        try
        {
            var retries = Math.Clamp(settings.Int("control_verify_retries", 2), 2, 5);
            for (var attempt = 0; attempt <= retries; attempt++)
            {
                var geometry = ReadSlider(kind) ?? throw new InvalidOperationException(ControlLayoutProblem(kind));
                Log("control_target", new { kind, value, point = geometry.ValueField.Center, field = geometry.ValueField, strategy = "numeric_field", attempt });
                SelectTextField(geometry.ValueField.Center,attempt);
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
        finally { textRecovery=previousRecovery;RestoreClipboard(previous); }
    }

    private ClipboardLease BeginClipboard()
    {
        if(clipboardLease is not null)throw new InvalidOperationException("Nested clipboard transaction.");
        return clipboardLease=new(new WindowsClipboardStore(detail=>Log("clipboard_capture",detail),detail=>Log("clipboard_format_unavailable",detail)),windowProcessId,
            status=>Log("clipboard_restore",new{status}),detail=>Log("clipboard_sequence",detail));
    }

    internal static string ControlCopyProblem(string kind)=>kind switch
    {
        "size"=>"Rust не повернув число Розмір (Size). Захопи цю смугу разом із числом справа й повтори перевірку. Залиш Rust на передньому плані; для віддаленого підключення вимкни синхронізацію буфера обміну.",
        "interval"=>"Rust не повернув число Інтервал (Interval). Захопи цю смугу разом із числом справа й повтори перевірку. Залиш Rust на передньому плані; для віддаленого підключення вимкни синхронізацію буфера обміну.",
        "opacity"=>"Rust не повернув число Прозорість (Opacity). Захопи цю смугу разом із числом справа й повтори перевірку. Залиш Rust на передньому плані; для віддаленого підключення вимкни синхронізацію буфера обміну.",
        _=>throw new ArgumentException("Invalid control kind.",nameof(kind))
    };
    private void RestoreClipboard(ClipboardLease previous)
    {
        clipboardLease=null;previous.Dispose();
    }

    public Rgb SelectCalibrationColor(Rgb background)
        =>CalibrationPaintSource.Read(verifiedControls.GetValueOrDefault("size",DesiredControls()),
            settings.Mode==ColorMode.RustPalette,PrepareBrushCalibration,()=>SelectCalibrationColorAtReadableSize(background));

    private Rgb SelectCalibrationColorAtReadableSize(Rgb background)
    {
        var cal = settings.PaintCalibration();
        if(settings.Mode==ColorMode.RustPalette)
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
        ApplyPalette(entry,alreadyAligned:true,requireAppliedProof:true);
        return entry.Color;
    }

    // Read independently without reapplying paint. A field/swatch disagreement
    // aborts calibration rather than turning a different paint into a footprint.
    public bool VerifyCalibrationPaint(Rgb expected)
        =>CalibrationPaintSource.Verify(verifiedControls.GetValueOrDefault("size",DesiredControls()),
            settings.Mode==ColorMode.RustPalette,PrepareBrushCalibration,()=>ReadCalibrationPaint(expected));

    private bool ReadCalibrationPaint(Rgb expected)
    {
        CheckBoundary();bool passed;
        if(settings.Mode==ColorMode.HexDirect)
        {
            var point=settings.Calibration.HexPoint??throw new InvalidOperationException("Захопи поле HEX.");
            var lease=BeginClipboard();
            try{passed=ReadHex(point,expected)==expected.Hex;}
            finally{RestoreClipboard(lease);}
        }
        else
        {
            var swatch=settings.Calibration.Rect("palette_swatch");
            passed=swatch.Valid&&PaletteSelection.Applied(expected,swatch,
                area=>{CheckBoundary();return Native.Median(area);},()=>Delay(.08)).Passed;
        }
        Log("brush_paint_source",new{revision=BrushLocalColor.SourceRevision,requested=expected.Hex,mode=settings.Mode.ToString(),passed,
            proofSize=verifiedControls.GetValueOrDefault("size",DesiredControls()),
            proofSource=settings.Mode==ColorMode.HexDirect?"fresh HEX readback":"two active swatch frames",paintedRgbVerified=false});
        if(!passed)throw new InvalidOperationException("Активний колір змінився під час вимірювання пензля. Повтори перевірку кольорів і калібрування.");
        return true;
    }

    private readonly HashSet<string> unverifiedPaletteColors=[];
    internal IReadOnlyCollection<string> UnverifiedPaletteColors=>unverifiedPaletteColors;
    private void ApplyPalette(PaletteEntry entry,bool alreadyAligned=false,bool requireAppliedProof=false)
    {
        var point = PaletteSelection.ClickPoint(entry,rebase,alreadyAligned);
        var swatch = settings.Calibration.Rect("palette_swatch");
        PaletteAppliedEvidence? applied=null;
        var proof=PaletteColorTransaction.Select(()=>VerifyPaletteTarget(entry,point),attempt=>
        {SelectPaletteTarget(point,attempt);},()=>
        {
            if(!CanReusePaletteSwatch(swatch))return null;
            applied=PaletteSelection.Applied(entry.Color,swatch,area=>{CheckBoundary();return Native.Median(area);},()=>Delay(.08));
            Log("palette_cell_proof",new{point,evidence=applied,tolerance=12,activeColorVerified=applied.Passed,paintedRgbVerified=false});
            return applied.Passed;
        },()=>TryRecoverPaletteSwatch(entry,point),requireAppliedProof||!ReleaseProfile.Standard(settings));
        if(proof==PaletteColorProof.ActiveVerified)return;
        if(proof==PaletteColorProof.CellOnly)
        {
            unverifiedPaletteColors.Add(entry.Color.Hex);
            Log("palette_active_unverified",new{target=entry.Color.Hex,point,targetVerified=true,activeColorVerified=false});
            return;
        }
        // The target is verified; the active output is hidden or stale. Stop
        // sending paint, retain the run and let the user expose it then resume.
        Native.Release();Paused=true;pauseReason="palette_swatch_unavailable";
        Log("input_pause",new{paused=true,reason=pauseReason,evidence=applied,checkpointRetained=checkpoint?.OwnsRun!=true});
        if(probeMode)throw new PreparationInterruptedException(PaletteSwatchSetupMessage);
        throw new InputInterrupted();
    }

    private void VerifyPaletteTarget(PaletteEntry entry, ScreenPoint point)
    {
        CheckBoundary();
        try
        {
            // Move outside the palette before reading so hover/cursor pixels
            // cannot masquerade as a changed color. The guard checks focus too.
            var original=Native.Cursor();
            var palette=settings.Calibration.Rect(entry.Source=="quick"?"quick":"palette");
            var area=PaletteSelection.SampleArea(settings.Calibration,point,entry.Source);
            if(area.Left<windowRect.Left||area.Top<windowRect.Top||area.Right>windowRect.Right||area.Bottom>windowRect.Bottom)
                throw new PaletteLayoutException();
            var park=CaptureCursor.ParkingPoint(palette,windowRect,original,48)
                ??throw new InvalidOperationException("Немає місця для знімка без курсора. Повтори захоплення полотна.");
            if(park!=original){Native.ReleaseChecked();MoveCursor(park);Delay(.08);}
            var evidence = PaletteSelection.Verify(settings.Calibration, entry, point, area =>
            { CheckBoundary(); return Native.Median(area); }, () => Delay(.08));
            Log("palette_target", new { color=entry.Color.Hex, point, evidence, activeColorVerified=false });
        }
        catch(InvalidOperationException e)
        {
            Log("palette_target_failed", new { color=entry.Color.Hex, point, reason=e.Message,
                evidence=(e as PaletteTargetException)?.Evidence });
            throw;
        }
    }

    public void VerifyPaletteTargets()
    {
        foreach(var entry in settings.Palette())
            VerifyPaletteTarget(entry, PaletteSelection.ClickPoint(entry,rebase,alreadyAligned:true));
    }

    private double? activeAdaptiveSize;
    private int activeShape;

    private void ApplyBrushShape(int? shapeOverride=null)
    {
        var cal = settings.PaintCalibration();
        var shapes = cal.GridCenters("brush_shapes", cal.Get("brush_shape_cols", 7), 1);
        int selected=shapeOverride??(activeShape>0?activeShape:settings.Int("brush_shape_slot",3));
        var slot = Math.Clamp(selected - 1, 0, 6);
        if (shapes.Count > slot)
            Click(shapes[slot]);
        else if (selected is 3 or 4&&cal.Point(selected==4 ? "square_brush" : "hard_brush") is ScreenPoint shape)
            Click(shape);
        else throw new InvalidOperationException("Capture all seven brush shapes before using this shape.");
        activeShape=selected;
        activeAdaptiveSize=null; // Reverify Size after changing the brush style.
    }

    private double DesiredControls(double? sizeOverride = null)
    {
        var speed = SpeedProfile.Get(settings.Text("speed_profile", "Rapid"));
        var precision = settings.Text("coverage_mode", "Precision") == "Precision" && settings.Bool("force_precision_controls", true);
        var size = precision ? PaintTimingPlan.DefaultSize(settings) : settings.Number("brush_size_value", 3);
        if (!precision && settings.Bool("auto_brush_size", true))
            size = BrushValue(Math.Max(1, settings.Int("cell_px", 3)));
        if (sizeOverride.HasValue)
            size = sizeOverride.Value;
        return Math.Max(DrawingWorkflow.MinimumSize,size);
    }

    public void ApplyControls() => ApplyControls(null);

    private void ApplyControls(double? sizeOverride)
    {
        VerifyControlLayout();
        ApplyBrushShape();
        var desired = DesiredControls(sizeOverride);
        Slider("size", desired);
        activeAdaptiveSize = desired;
        needsReprime = false;
    }

    private void ReprimeControls(double? sizeOverride,int? shapeOverride=null)
    {
        // Returning from pause should not blindly re-click all three controls.
        // Verify their current positions and only repair a control that drifted.
        ApplyBrushShape(shapeOverride);
        var desired = DesiredControls(sizeOverride);
        var repaired = new List<string>();
        if (!SliderMatches("size", desired)) { Slider("size", desired); repaired.Add("size"); }
        activeAdaptiveSize = desired;
        Log("reprime", new { size = desired, repaired, verified = true });
    }

    private double BrushValue(double pixels)
        => AutomaticBrush.Value(settings, pixels);

    private double ColorDelay() => StrokeTiming.ColorDelay(settings);

    public void Run(PaintPlan plan, ResumeCheckpoint? resume = null)
    {
        using var inputTiming=nativeSendMetrics?.Observe();
        string outcome="completed";
        try{RunCore(plan,resume);}
        catch(OperationCanceledException){outcome="cancelled";throw;}
        catch{outcome="failed";throw;}
        finally
        {
            if(runTimes is not null)
            {
                SaveTransportTiming();
                var times=runTimes.Read();Log("run_time_breakdown",new{revision="exclusive-phase-v2-finish-separated",outcome,times,inputDiagnostics=InputSnapshot()});
                try{report(new(timingDone,timingTotal,ActiveSeconds,0,"",Times:times,TimesOnly:true));}
                catch(Exception e){Log("timing_publication_unavailable",new{reason=e.Message});}
            }
            // A cancellation can arrive before RunCore starts its clock. The
            // accepted START still owns diagnostic status, never a checkpoint.
            SaveRunSnapshotMetadata(outcome);
        }
    }
    private void SaveRunSnapshotMetadata(string outcome)
    {
        if(runSnapshotDirectory is null)return;
        var now=DateTimeOffset.UtcNow;
        runStartedUtc??=now;
        if(runSnapshotOutcome!=outcome){runSnapshotOutcome=outcome;runStateChangedUtc=now;}
        try{PaintRunSnapshotStore.Write(runSnapshotDirectory,new(BuildInfo.Full,runIdentity,runTargetIdentity,outcome,runSnapshotComplete,runSnapshotProblem,
            runTimes?.Read(),diagnosticSource is not null,timingDone,timingTotal,checkpoint?.Latest.Group,checkpoint?.Latest.Line,
            outcome=="paused"?pauseReason??"manual":null,checkpoint?.OwnsRun==true,++runSnapshotSequence,now)
            {startedUtc=runStartedUtc,stateChangedUtc=runStateChangedUtc,phase=runTimes is null?"Planning":timingPhase.ToString(),
                inputDiagnostics=InputSnapshot(),remainingEstimate=timing?.Estimate(ActiveSeconds)});}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){Log("run_snapshot_unavailable",new{reason=e.Message});}
    }
    private void RunCore(PaintPlan plan,ResumeCheckpoint? resume)
    {
        if(PaintingReadiness.BrushProblem(settings) is {} calibrationProblem)throw new InvalidOperationException(calibrationProblem);
        token.ThrowIfCancellationRequested();
        clock.Start();runTimes=new(()=>clock.Elapsed.TotalSeconds);runStartedUtc??=DateTimeOffset.UtcNow;timingPhase=PaintPhase.Preparing;
        try
        {
            runIdentity=plan.Identity;
            runTargetIdentity=plan.TargetIdentity;
            string evidence=runSnapshotDirectory??=Path.Combine(Path.GetDirectoryName(logPath)!,"last-run",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(evidence);
            DiagnosticRetention.Prune(Path.GetDirectoryName(evidence)!,evidence);
            File.WriteAllText(Path.Combine(evidence,"effective-settings.json"),settings.Data.ToJsonString());
            Images.Save(plan.Preview,Path.Combine(evidence,"target.png"));
            if(diagnosticSource is not null)Images.Save(diagnosticSource,Path.Combine(evidence,"source.png"));runSnapshotComplete=true;SaveRunSnapshotMetadata("preparing");
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){runSnapshotProblem=e.Message;SaveRunSnapshotMetadata("preparing");Log("run_snapshot_unavailable",new{reason=e.Message});}
        Log("start_preparing",new{version=BuildInfo.Version,workingSize=PaintTimingPlan.DefaultSize(settings)});
        report(new(0,0,0,0,"Готую штрихи та час. Можна зупинити підготовку.",Phase:PaintPhase.Preparing));
        settings.Validate();
        if(settings.Bool("coverage_audit")&&CoverageAudit.SetupProblem(settings) is { } auditProblem)
            throw new InvalidOperationException(auditProblem);
        if(settings.Bool("coverage_audit")&&resume is not null)
            throw new InvalidOperationException("Аудит потребує початкового кадру Canvas. Виконай новий START; RESUME доступний без аудиту.");
        PrepareTexelProduct(plan);
        var measuredPlan=plan.ExperimentalTexel is null?MeasuredColorPlan.TryBuild(plan,settings,token):null;
        var groups = measuredPlan is null?TransferSchedule.Build(plan,settings,token):TransferSchedule.Build(plan,settings,measuredPlan.Groups,token);
        if(measuredPlan is not null)
            Log("measured_color_plan",new{revision=MeasuredColorPlan.Revision,measuredPlan.TargetPixels,measuredPlan.CoveredPixels,measuredPlan.UnplannedPixels,
                sizes=measuredPlan.Groups.Values.SelectMany(x=>x).Select(x=>x.Size).Distinct().Order().ToArray(),selection=measuredPlan.Diagnostics?.Choice,
                strokeSavings=measuredPlan.Diagnostics?.StrokeSavings,physicalResultVerified=false});
        if(groups.Values.All(x=>x.Count==0))throw new InvalidOperationException("Жоден підтверджений пензель не поміщається у кольорові ділянки. Виміряй менший Size або зменш кількість кольорів.");
        var order = TransferSchedule.Order(plan, groups);
        SaveExecutionEvidence(plan,groups,order);
        if(plan.Mode==ColorMode.RustPalette)
            Log("palette_plan",new{available=plan.Palette.Length,expectedColors=plan.Counts.Count,scheduledColors=order.Count,
                skippedColors=plan.Counts.Keys.Except(order).Select(i=>plan.Palette[i].Color.Hex).ToArray(),
                paletteOnly=order.All(i=>plan.Palette[i].Source!="hex"&&plan.Palette[i].ClickPoint is not null),
                unplannedPixels=measuredPlan?.UnplannedPixels,physicalResultVerified=false});

        var total = groups.Values.Sum(x => x.Count);
        var done = resume?.Done ?? 0;
        if (resume is not null && !resume.Matches(plan.Identity, order.Select(color => groups[color].Count).ToArray()))
            throw new InvalidDataException("Прогрес RESUME не відповідає плану. Виконай новий START.");
        var speed = SpeedProfile.Get(settings.Text("speed_profile", "Rapid"));
        double nextReport=0;
        timing=new(PaintTimingPlan.Build(settings,groups,order,resume?.Group??0,resume?.Line??0,token));
        timingDone=done;timingTotal=total;timingPhase=PaintPhase.Countdown;
        checkpoint = new(checkpointPath, resume ?? new(plan.Identity, 0, 0, 0){RunKey=Guid.NewGuid().ToString("N")},deferUntilPaint:resume is null);
        if(resume is not null)FlushCheckpoint("resume");
        SaveTexelCheckpointLink(plan,resume,done);
        SaveRunSnapshotMetadata("countdown");
        bool completed = false;
        var highResolutionTimer = Native.BeginHighResolutionTimer();
        try
        {
            countdownStarted=clock.Elapsed.TotalSeconds;runTimes.Switch("countdown");
            try
            {
            for (var sec = settings.Int("start_delay", 5); sec > 0; sec--)
            {
                token.ThrowIfCancellationRequested();
                ReportTiming($"Старт через {sec} с…",sec);
                for (var j = 0; j < 20; j++)
                {
                    token.ThrowIfCancellationRequested();
                    if (Native.Down(0x1B))
                        throw new OperationCanceledException();
                    Thread.Sleep(50);
                }
            }

            }
            finally
            {
                countdownSeconds+=clock.Elapsed.TotalSeconds-countdownStarted.Value;countdownStarted=null;runTimes.Switch("service");
                Log("eta_checkpoint",new{point="R0-after-countdown",countdownSeconds,activeSeconds=ActiveSeconds});
            }
            if (!Native.IsRust(window))
                throw new InvalidOperationException("Canvas повинен належати вікну Rust.");
            Native.SetForegroundWindow(window);
            WaitReady();
            Log("start", new { version = BuildInfo.Version, commit = BuildInfo.GitCommit, buildDate = BuildInfo.BuildDate, inputEngine = settings.Text("input_engine", "Stable"), delayTransport=DelayTimer.Transport, fastTransfer = settings.Bool("fast_transfer"), fastMotion = TransferSchedule.Fast(settings), calibratedMotion=SpeedCalibration.Use(settings), coverageAudit=settings.Bool("coverage_audit"), motionTransport = SpeedCalibration.Use(settings)?"SendInput probe verified":TransferSchedule.Fast(settings)?"SendInput dense path":"standard", motionRevision=StrokeMotion.Revision, standardEmitter=StandardStrokeMotion.Emitter(settings), standardCoverageRevision=ReleaseProfile.Standard(settings)?Coverage.StandardRevision:null, movementPreset=settings.Text("speed_profile","Rapid"), timingRevision=StrokeTiming.Revision, highResolutionTimer, probeRevision=SpeedCalibration.Revision, pathPacketPoints=StrokeMotion.PacketSize(settings), resumeGroup=resume?.Group??0,resumeLine=resume?.Line??0,resumeDone=resume?.Done??0, frameMs = StrokeTiming.Frame(settings) * 1000, controlFrameMs=StrokeTiming.ControlFrame(settings)*1000, releaseMs = StrokeTiming.Release(settings) * 1000, mode = plan.Mode.ToString(), groups = order.Count, batches = total, sourceStrokes = groups.Values.SelectMany(x=>x).Sum(x=>x.SourceStrokes), canvas = settings.Calibration.Rect("canvas") });
            BeginTiming(PaintTimingPlan.Setup,PaintPhase.Preparing,"Налаштовую пензель…");
            SaveRunSnapshotMetadata("running");
            double setupStart=ActiveSeconds;
            for (;;)
                try { ApplyControls(); break; }
                catch (InputInterrupted) { WaitReady(); }
            timing.Complete(PaintTimingPlan.Setup,ActiveSeconds-setupStart);
            Log("eta_checkpoint",new{point="R1-after-setup",activeSeconds=ActiveSeconds,estimate=timing.Estimate(ActiveSeconds)});
            string beforeWork=ReleaseProfile.Standard(settings)?PaintTimingPlan.NativeBefore:PaintTimingPlan.FinalBefore;
            BeginTiming(beforeWork,ReleaseProfile.Standard(settings)?PaintPhase.Preparing:PaintPhase.Auditing,"Знімаю початкове полотно…");
            double baselineStart=ActiveSeconds;
            BeginFinalAudit(plan,resume is not null);
            timing.Complete(beforeWork,ActiveSeconds-baselineStart);
            Log("adaptive_plan", new { enabled = settings.Bool("adaptive_brush"), wide = groups.Values.SelectMany(x => x).Count(x => x.Size > PaintTimingPlan.DefaultSize(settings)),
                baseSize=PaintTimingPlan.DefaultSize(settings),bySize=groups.Values.SelectMany(x=>x).GroupBy(x=>x.Size)
                    .Select(g=>new{size=g.Key,operations=g.Count()}).ToArray(),total,
                footprintRevision=BrushFootprints.Revision,shapeSlots=groups.Values.SelectMany(x=>x).Select(x=>x.ShapeSlot>0?x.ShapeSlot:settings.Int("brush_shape_slot",3)).Distinct().Order().ToArray() });
            string? lastHex = null;
            for (var group = resume?.Group ?? 0; group < order.Count; group++)
            {
                var color = order[group];
                var entry = plan.Palette[color];
                WaitReady();
                BeginTiming(PaintTimingPlan.Color(group),PaintPhase.Preparing,$"#{entry.Color.Hex}");
                double colorStart=ActiveSeconds;
                bool skippedColor=false;
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
                            ApplyPalette(entry,requireAppliedProof:TexelExperimentSettings.Enabled(settings)||!ReleaseProfile.Standard(settings));
                        if (plan.Mode == ColorMode.HexDirect)
                            Delay(ColorDelay());
                        break;
                    }
                    catch (InputInterrupted)
                    {
                        WaitReady();
                        lastHex = null;
                    }
                    catch(PaletteAppliedException error)
                    {
                        if(ReleaseProfile.Standard(settings))throw;
                        skippedColor=true;
                        Log("palette_group_skipped",new{group,color=entry.Color.Hex,error.Evidence,reason="active color not confirmed",paintedRgbVerified=false});
                        break;
                    }

                timing.Complete(PaintTimingPlan.Color(group),ActiveSeconds-colorStart);runTimes?.Switch("service");
                if(skippedColor)
                {
                    int firstLine=group==(resume?.Group??-1)?resume!.Line:0;
                    for(int skippedLine=firstLine;skippedLine<groups[color].Count;skippedLine++)
                    {
                        timing.Skip(PaintTimingPlan.Shape(group,skippedLine));timing.Skip(PaintTimingPlan.Size(group,skippedLine));
                        timing.Skip(PaintTimingPlan.Motion(group,skippedLine));done++;skippedPaletteOperations++;
                    }
                    timing.Skip(PaintTimingPlan.BeforeAudit(group));timing.Skip(PaintTimingPlan.Audit(group));
                    timingDone=done;checkpoint.Update(new(plan.Identity,group+1,0,done));FlushCheckpoint("unconfirmed_color_skipped");
                    ReportTiming("Колір не підтверджено. Групу пропущено; перевір зразок активного кольору.");
                    continue;
                }
                Log("color_group", new { group, mode = plan.Mode.ToString(), color = entry.Color.Hex, point = entry.ClickPoint, strokes = groups[color].Count });
                PixelImage? auditBefore=null;
                if(settings.Bool("coverage_audit"))
                {
                    BeginTiming(PaintTimingPlan.BeforeAudit(group),PaintPhase.Auditing,"Знімаю полотно для аудиту…");
                    double captureStart=ActiveSeconds;
                    for(;;)try{auditBefore=StableShot(settings.Calibration.Rect("canvas"));break;}
                        catch(InputInterrupted){WaitReady();}
                    timing.Complete(PaintTimingPlan.BeforeAudit(group),ActiveSeconds-captureStart);
                }
                var lines = groups[color];
                double motionSeconds=0,plannedMotionSeconds=0,operationOverheadSeconds=0;
                double resolutionSeconds=0,estimateSeconds=0,etaModelSeconds=0,checkpointSeconds=0,reportSeconds=0;int motionBatches=0,shiftParts=0;
                var routes=new Dictionary<string,int>();var lengthHistogram=new int[6];
                InputCosts costs=default;
                bool groupComplete=false;
                try
                {
                for (var line = group == (resume?.Group ?? -1) ? resume!.Line : 0; line < lines.Count; line++)
                {
                    double cycleStart=0,excludedSeconds=0,actualMotion=0,recoverySeconds=0;
                    bool retriedCycle=false;
                    double plannedCycle=timing.Planned(PaintTimingPlan.Motion(group,line),PaintTimingPlan.Shape(group,line),PaintTimingPlan.Size(group,line));
                    while (true)
                        try
                        {
                            cycleStart=ActiveSeconds;excludedSeconds=0;recoverySeconds=0;
                            WaitReady();
                            var op = lines[line];
                            var targetSize = settings.Bool("adaptive_brush") ? (op.Size > 0 ? op.Size : PaintTimingPlan.DefaultSize(settings)) : (double?)null;
                            int targetShape=op.ShapeSlot>0?op.ShapeSlot:settings.Int("brush_shape_slot",3);

                            if (needsReprime)
                            {
                                texelViewRequired=true;
                                runTimes?.Switch("service");
                                double reprimeStart=ActiveSeconds;
                                ReprimeControls(targetSize,targetShape);
                                if (plan.Mode == ColorMode.HexDirect)
                                {
                                    if (!ApplyHex(entry.Color))
                                        throw new InvalidOperationException("HEX verification failed after pause.");
                                    lastHex = entry.Color.Hex;
                                }
                                else
                                    ApplyPalette(entry,requireAppliedProof:TexelExperimentSettings.Enabled(settings)||!ReleaseProfile.Standard(settings));
                                needsReprime = false;
                                recoverySeconds=ActiveSeconds-reprimeStart;excludedSeconds+=recoverySeconds;
                            }

                            if(activeShape!=targetShape)
                            {
                                BeginTiming(PaintTimingPlan.Shape(group,line),PaintPhase.Preparing,"Змінюю форму пензля…");
                                double shapeStart=ActiveSeconds;ApplyBrushShape(targetShape);
                                double shapeSeconds=ActiveSeconds-shapeStart;excludedSeconds+=shapeSeconds;
                                timing.Complete(PaintTimingPlan.Shape(group,line),shapeSeconds);
                                Log("adaptive_shape",new{group,line,shape=targetShape,op.ProfileId});
                            }
                            timing.Skip(PaintTimingPlan.Shape(group,line));
                            if (settings.Bool("adaptive_brush"))
                            {
                                var size = targetSize!.Value;
                                if (activeAdaptiveSize != size)
                                {
                                    BeginTiming(PaintTimingPlan.Size(group,line),PaintPhase.Preparing,"Змінюю розмір пензля…");
                                    double sizeStart=ActiveSeconds;
                                    Slider("size", size);
                                    double sizeSeconds=ActiveSeconds-sizeStart;excludedSeconds+=sizeSeconds;
                                    timing.Complete(PaintTimingPlan.Size(group,line),sizeSeconds);
                                    activeAdaptiveSize = size;
                                    Log("adaptive_size", new { group, line, size, baseSize=PaintTimingPlan.DefaultSize(settings),wide = size > PaintTimingPlan.DefaultSize(settings) });
                                }
                            }
                            // Reprime after a pause may have already set the pending Size.
                            timing.Skip(PaintTimingPlan.Size(group,line));
                            BeginTiming(PaintTimingPlan.Motion(group,line),PaintPhase.Drawing,$"#{entry.Color.Hex}");
                            long motionStart=Stopwatch.GetTimestamp();
                            var inputStart=InputSnapshot();long pointsBefore=emittedMovementPoints;
                            long resolutionStart=Stopwatch.GetTimestamp();
                            bool fastBatch=Execution.FastBatch(op);
                            var sample=op.Segments.Count==1?Execution.Resolve(activeAdaptiveSize??DesiredControls(),op.Segments[0],activeShape):null;
                            resolutionSeconds+=Stopwatch.GetElapsedTime(resolutionStart).TotalSeconds;
                            if(plan.ExperimentalTexel is {} texel)DrawTexelProduct(texel.Runs[line]);
                            else if(op.StrokeProfileId is {} motionId)
                            {
                                if(!StrokeProfiles.Matches(settings,motionId,op.Segments[0],out var motion)||motion!.Size!=targetSize||motion.Shape!=targetShape)
                                    throw new InvalidOperationException("Moving stroke profile changed. Recalibrate before painting.");
                                // The measured emitter runs forward only, at its
                                // exact route; alternating reversal is unproved.
                                Draw(op.Segments[0],SpeedProfile.Get("Rapid"),false,null);
                            }
                            else if(fastBatch)DrawBatch(op,speed);
                            else Draw(op.Segments[0], speed, line % 2 == 1,sample);
                            actualMotion=Stopwatch.GetElapsedTime(motionStart).TotalSeconds;runTimes?.Switch("service");
                            string route=plan.ExperimentalTexel is not null?StandardContourMotion.Revision:fastBatch?"dense_path":sample is not null?$"probe_{sample.Method}":
                                settings.Bool("line_mode")&&settings.Text("coverage_mode")=="Fast"&&TransferSchedule.Length(op.Segments[0])>=settings.Int("min_line_width",4)*settings.Int("cell_px",3)?"legacy_shift":"drag";
                            var first=op.Segments[0];route+=op.Segments.Count>1?":mixed":first.X1==first.X2&&first.Y1!=first.Y2?":V":":H";
                            routes[route]=routes.GetValueOrDefault(route)+1;
                            foreach(var segment in op.Segments){int n=TransferSchedule.Length(segment);lengthHistogram[n<8?0:n<16?1:n<32?2:n<64?3:n<128?4:5]++;}
                            if(sample?.Method==StrokeMethod.Shift)shiftParts+=Math.Max(1,(TransferSchedule.Length(first)+sample.MaxLength-1)/sample.MaxLength);
                            var observedCosts=InputSnapshot()-inputStart;costs+=observedCosts;
                            if(!fastBatch&&sample is null)ObserveTransport(observedCosts,emittedMovementPoints-pointsBefore,actualMotion);
                            long etaStart=Stopwatch.GetTimestamp();
                            timing.Complete(PaintTimingPlan.Motion(group,line),actualMotion);
                            etaModelSeconds+=Stopwatch.GetElapsedTime(etaStart).TotalSeconds;
                            motionSeconds+=actualMotion;
                            long estimateStart=Stopwatch.GetTimestamp();
                            plannedMotionSeconds+=Execution.EstimateForTiming(op);motionBatches++;
                            estimateSeconds+=Stopwatch.GetElapsedTime(estimateStart).TotalSeconds;
                            break;
                        }
                        catch (InputInterrupted)
                        {
                            retriedCycle=true;
                            Native.Release();
                            WaitReady();
                        }
                        catch(PaletteAppliedException error)
                        {
                            if(ReleaseProfile.Standard(settings))throw;
                            // A resumed color must satisfy the same proof as
                            // its initial selection. Never emit its next stroke.
                            skippedColor=true;
                            Log("palette_group_skipped",new{group,line,color=entry.Color.Hex,error.Evidence,reason="active color not confirmed after pause",paintedRgbVerified=false});
                            for(int skippedLine=line;skippedLine<lines.Count;skippedLine++)
                            {
                                timing.Skip(PaintTimingPlan.Shape(group,skippedLine));timing.Skip(PaintTimingPlan.Size(group,skippedLine));
                                timing.Skip(PaintTimingPlan.Motion(group,skippedLine));done++;skippedPaletteOperations++;
                            }
                            timing.Skip(PaintTimingPlan.Audit(group));timingDone=done;
                            checkpoint.Update(new(plan.Identity,group+1,0,done));FlushCheckpoint("unconfirmed_color_skipped_after_pause");
                            ReportTiming("Колір не підтверджено. Групу пропущено; перевір зразок активного кольору.");
                            break;
                        }

                    if(skippedColor)break;

                    done++;
                    timingDone=done;
                    var state = new ResumeCheckpoint(plan.Identity, group, line + 1, done);
                    if (line + 1 == lines.Count&&!settings.Bool("coverage_audit"))
                        state = state with
                        {
                            Group = group + 1,
                            Line = 0
                        };
                    long checkpointStart=Stopwatch.GetTimestamp();checkpoint.Update(state);
                    if (done % 50 == 0 || line + 1 == lines.Count)
                    {
                        FlushCheckpoint("periodic");
                        SaveRunSnapshotMetadata("running");
                    }
                    checkpointSeconds+=Stopwatch.GetElapsedTime(checkpointStart).TotalSeconds;
                    if (clock.Elapsed.TotalSeconds>=nextReport || done == total)
                    {
                        long reportStart=Stopwatch.GetTimestamp();
                        nextReport=clock.Elapsed.TotalSeconds+.25;
                        ReportTiming($"#{entry.Color.Hex}");
                        reportSeconds+=Stopwatch.GetElapsedTime(reportStart).TotalSeconds;
                    }
                    double overhead=Math.Max(0,ActiveSeconds-cycleStart-excludedSeconds-actualMotion);
                    timing.RecordOperationOverhead(PaintTimingPlan.Motion(group,line),overhead);
                    if(!retriedCycle&&recoverySeconds==0)
                        timing.RecordFullCycle(PaintTimingPlan.Motion(group,line),plannedCycle,Math.Max(0,ActiveSeconds-cycleStart));
                    operationOverheadSeconds+=overhead;
                }
                groupComplete=!skippedColor;
                }
                finally
                {
                // A cancelled color used to discard all its timing diagnostics.
                // Draw's finally releases input; release again before log IO.
                Native.Release();
                Log("motion_group",new{group,groupComplete,scope="completed operations only",motionBatches,motionSeconds,plannedMotionSeconds,operationOverheadSeconds,
                    resolutionSeconds,estimateSeconds,etaModelSeconds,checkpointSeconds,reportSeconds,executionRevision=StrokeExecutionPlan.Revision,
                    routes,shiftParts,lengthHistogram, lengthHistogramRanges=new[]{"0-7","8-15","16-31","32-63","64-127","128+"},
                    delayTransport=DelayTimer.Transport,inputCosts=costs,meanStrokeMs=motionBatches>0?motionSeconds*1000/motionBatches:0,plannedMeanStrokeMs=motionBatches>0?plannedMotionSeconds*1000/motionBatches:0});
                }
                if(auditBefore is not null&&!skippedColor)
                {
                    BeginTiming(PaintTimingPlan.Audit(group),PaintPhase.Auditing,"Перевіряю покриття…");
                    double auditStart=ActiveSeconds;
                    for(;;)try{AuditGroup(plan,color,group,auditBefore);break;}
                        catch(InputInterrupted){WaitReady();ReprimeControls(activeAdaptiveSize);}
                        catch(PaletteAppliedException error)
                        {
                            Log("palette_repair_skipped",new{group,color=entry.Color.Hex,error.Evidence,reason="active color not confirmed during repair",paintedRgbVerified=false});
                            ReportTiming("Колір не підтверджено. Групу пропущено; перевір зразок активного кольору.");
                            break;
                        }
                    timing.Complete(PaintTimingPlan.Audit(group),ActiveSeconds-auditStart);
                    checkpoint.Update(new(plan.Identity,group+1,0,done));
                    FlushCheckpoint("audited_group");
                }
            }

            string afterWork=ReleaseProfile.Standard(settings)?PaintTimingPlan.NativeAfter:PaintTimingPlan.FinalAudit;
            BeginTiming(afterWork,ReleaseProfile.Standard(settings)?PaintPhase.Finishing:PaintPhase.Auditing,ReleaseProfile.Standard(settings)?"Зберігаю знімок результату…":"Перевіряю фінальне покриття…");
            double finalAuditStart=ActiveSeconds;CompleteFinalAudit(plan);
            timing.Complete(afterWork,ActiveSeconds-finalAuditStart);
            BeginTiming(PaintTimingPlan.Finish,PaintPhase.Finishing,"Завершую перенесення…");
            double finishStart=ActiveSeconds;
            // Interval / Opacity are manual prerequisites, never transported by Pixora.
            if (checkpoint.OwnsRun && File.Exists(checkpointPath))
                File.Delete(checkpointPath);
            RestoreLastStrokeCursor();
            timing.Complete(PaintTimingPlan.Finish,ActiveSeconds-finishStart);
            completed = true;
            timingPhase=PaintPhase.Completed;
            ReportTiming(ReleaseProfile.Standard(settings)?"Команди надіслано. Перевір результат у Rust.":FinalCoverage?.Passed==true?"Покриття підтверджено аудитом. Перевір вигляд у Rust.":"Команди оброблено. Фінальний аудит потребує уваги.");
            Log("complete", new {unverifiedPaletteColors=unverifiedPaletteColors.ToArray(), profile=settings.Text("release_profile"),done, total, skippedPaletteOperations, elapsedSeconds=clock.Elapsed.TotalSeconds-pausedSeconds, fastTransfer=settings.Bool("fast_transfer"), calibratedMotion=SpeedCalibration.Use(settings),coverageVerified=FinalCoverage?.Passed==true,resultVerified = false, paletteSwatchAvailable = settings.Calibration.Rect("palette_swatch").Valid });
        }
        finally
        {
            Native.Release();
            if (highResolutionTimer)
                Native.EndHighResolutionTimer();
            if (!completed) FlushCheckpoint(token.IsCancellationRequested ? "stop" : "interruption");
        }
    }

    // Only restore controls while Rust is still foreground.
    internal static void FinishControls(Settings settings, Func<bool> foreground, Action<double> applyOpacity)
    {
        // Retained temporarily for binary/test compatibility; no control transactions.
    }

    private void FlushCheckpoint(string reason)
    {
        try
        {
            if (checkpoint?.Flush() == true)
                Log("checkpoint", new { reason, checkpoint.Latest.Group, checkpoint.Latest.Line, checkpoint.Latest.Done });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log("checkpoint_save_failed", new { reason, checkpoint?.Latest, message = e.Message });
            throw new IOException("Не вдалося зберегти прогрес. Продовження може повторити дії після попередньої контрольної точки.", e);
        }
    }

    private void Draw(ScreenLine line, SpeedProfile speed, bool reverse,SpeedSample? sample)
    {
        StrokeTraceInput? trace=sample is null&&ReleaseProfile.Standard(settings)&&settings.Bool("diagnostic_debug")&&strokeTraces<128
            ?new(motionInput,ReadCursor):null;
        try
        {
        if(sample is not null)
        {
            // Probe validates left-to-right / top-to-bottom. Preserve that direction.
            var tested=line.X2<line.X1||line.Y2<line.Y1?TransferSchedule.Reverse(line):line;
            CalibratedMotion.Draw(tested,sample,motionInput);
            lastPaintPoint=Native.Cursor();
            return;
        }
        if(StandardLineMotion.Applies(settings,line))
            GuardedNativeInput.Line(coordinateGuard,line,CheckBoundary,CoordinateDenied,deniedWithReason:CoordinateDenied);
        StandardStrokeMotion.Draw(line,settings,speed,reverse,(ICalibratedStrokeInput?)trace??motionInput);
        lastPaintPoint=Native.Cursor();
        }
        finally
        {
            try{PersistAfterRelease(Native.Release,PersistPaintOwnership);}
            finally{if(trace is not null)SaveStrokeTrace(trace,line,speed,reverse);}
        }
    }

    private void DrawBatch(PaintBatch batch,SpeedProfile speed)
    {
        try{StrokeMotion.Draw(batch,settings,speed,motionInput);lastPaintPoint=Native.Cursor();}
        finally{PersistAfterRelease(Native.Release,PersistPaintOwnership);}
    }

    private void CoordinateDenied()=>CoordinateDenied("coordinate_out_of_bounds");
    private void CoordinateDenied(string reason)
    {
        Paused=true;pauseReason=reason;
        Log("input_pause",new{reason=pauseReason,canvas=settings.Calibration.Rect("canvas")});
        throw new InputInterrupted();
    }
    private void MoveCursor(ScreenPoint point)=>MoveIntentCursor(point,InputIntent.Parking);
    private void MoveIntentCursor(ScreenPoint point,InputIntent intent)
    {
        // SetCursorPos can leave Rust's rendered brush indicator at the last
        // stroke. Use the guarded input event path for parking and returning too,
        // so the game receives the move before the settled capture is taken.
        if(motionInput is not IIntentStrokeInput transport)throw new InvalidOperationException("Input intent is required.");
        transport.Move([point],intent);
    }
    private bool CanReturnCursor()=>!token.IsCancellationRequested&&!Paused&&!Native.Down(0x1B)&&!Native.Down(0x75)
        &&Native.GetForegroundWindow()==window&&Native.ProcessIdOf(window)==windowProcessId
        &&Native.DpiOf(window)==windowDpi&&Native.GetWindowRect(window,out var rect)&&rect.ToScreen()==windowRect;
    public void RestoreLastStrokeCursor()
    {
        if(lastPaintPoint is not { } point)return;
        bool restored=CaptureCursor.Return(point,Native.ReleaseChecked,MoveCursor,CanReturnCursor,
            error=>Log("cursor_restore_failed",new{phase="completion",message=error.Message}));
        Log("cursor_finish",new{point,restored});
    }

    private sealed class GuardedStrokeInput(Painter owner) : IIntentStrokeInput,ITrackedStrokeInput
    {
        public double Seconds => owner.clock.Elapsed.TotalSeconds;
        public void Move(IReadOnlyList<ScreenPoint> points)=>Move(points,InputIntent.Paint);
        public void Move(IReadOnlyList<ScreenPoint> points,InputIntent intent)
        {
            owner.Check();
            if(owner.Paused){if(owner.probeMode)owner.CheckProbe();throw new InputInterrupted();}
            GuardedNativeInput.Move(owner.coordinateGuard,points,intent,()=>{owner.Check();if(owner.Paused)throw new InputInterrupted();},owner.CoordinateDenied,
                packet=>
                {
                    // Route/window checks may take time. Check again immediately
                    // before emission so a displaced cursor is not dragged back.
                    owner.CheckStrokeCursor("before_move",requireEndpoint:true);
                    owner.MovePathMeasured(packet);
                    owner.strokeCursor.Emitted(packet);
                });
        }
        public void Button(bool up)
        {
            if(!up&&owner.paletteHover?.Active==true){Native.Release();throw new InputInterrupted();}
            try{GuardedNativeInput.Button(owner.coordinateGuard,up,InputIntent.Paint,owner.CheckBoundary,owner.CoordinateDenied,environment:owner.inputEnvironment,beforePaintDown:owner.CheckPaintTool,cursorChanged:owner.CursorChangedBeforeDown);}
            catch(CursorReadException error)
            {owner.Log("input_cursor_unavailable",new{reason="cursor_read_failed",error.Win32Error,sampleStatus="invalid",paintDown=false});throw;}
            if(!up)owner.CommitPaintOwnership();
        }
        public void Shift(bool up){if(!up)owner.CheckBoundary();Native.Key(0x10,up);}
        public void ConfirmPosition(ScreenPoint expected)
        {
            try{GuardedNativeInput.ConfirmPosition(expected,owner.CheckBoundary,(a,b)=>owner.CursorCommandMismatch(a,b),owner.inputEnvironment);}
            catch(CursorReadException error)
            {owner.Log("input_cursor_unavailable",new{reason="cursor_read_failed",error.Win32Error,expected,sampleStatus="invalid"});throw;}
        }
        public void Wait(double seconds) => owner.Delay(seconds);
        public void BeginCursorTracking(ScreenPoint expected)=>owner.strokeCursor.Begin(expected);
        public void EndCursorTracking()=>owner.strokeCursor.End();
    }
}
