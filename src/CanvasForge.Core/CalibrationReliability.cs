namespace CanvasForge.Core;

public sealed record ReadbackOptions(int Polls=24,int Attempts=3,double RetryPause=.05)
{
    public void Validate()
    {
        if(Polls is <24 or >120||Attempts is <1 or >5||!double.IsFinite(RetryPause)||RetryPause is <0 or >1)
            throw new InvalidDataException("Invalid readback budget.");
    }
}

public static class CalibrationReliability
{
    private const string PendingKey="visual_brush_check_pending";
    public static bool Visual(Settings s)=>s.Text("control_confirmation","Clipboard")=="Visual";
    public static ReadbackOptions Readback(Settings s)=>new(s.Int("readback_polls",24),s.Int("readback_attempts",3),s.Number("readback_retry_pause_ms",50)/1000);
    public static int StableAttempts(Settings s)=>s.Int("capture_stable_attempts",5);
    public static double StableInterval(Settings s)=>s.Number("capture_stable_interval_ms",80)/1000;
    public static void Validate(Settings s)
    {
        if(s.Text("control_confirmation","Clipboard") is not ("Clipboard" or "Visual"))throw new InvalidDataException("Invalid control confirmation method.");
        foreach(var (key,fallback) in new[]{("readback_polls",24),("readback_attempts",3),("capture_stable_attempts",5)})
            if(s.Number(key,fallback)!=s.Int(key,fallback))throw new InvalidDataException("Invalid calibration integer: "+key);
        Readback(s).Validate();
        if(StableAttempts(s) is <5 or >20||!double.IsFinite(StableInterval(s))||StableInterval(s) is <.04 or >1)
            throw new InvalidDataException("Invalid stable capture budget.");
    }
    public static void ApplyPreset(Settings s,bool slow)
    {
        s.Set("readback_polls",slow?54:24);s.Set("readback_attempts",3);s.Set("readback_retry_pause_ms",slow?200:50);
        s.Set("capture_stable_attempts",slow?10:5);s.Set("capture_stable_interval_ms",slow?160:80);
        s.Set("control_confirmation",slow?"Visual":"Clipboard");s.Set("fast_transfer",false);s.Set("input_engine","Stable");
        if(slow)
        {s.Set("coverage_mode","Precision");s.Set("force_precision_controls",true);s.Set("use_fixed_opacity",true);s.Set("paint_opacity_value",1);}
    }
    public static void BeginBrushCheck(Settings s)
    {if(Visual(s))s.Set(PendingKey,true);}
    public static void ConfirmBrushCheck(Settings s,IReadOnlyList<BrushFootprint> measured)
    {
        double size=PaintTimingPlan.DefaultSize(s);int shape=s.Int("brush_shape_slot",3);
        if(measured.Any(p=>p.Size==size&&p.ShapeSlot==shape&&p.SolidCore.Valid&&BrushFootprints.Valid(p)&&p.Context==BrushFootprints.Context(s,shape)))
            s.Set(PendingKey,false);
    }
    public static string? PaintingProblem(Settings s)
    {
        if(PaintTimingPlan.DefaultSize(s)==1&&!SpeedCalibration.BrushReady(s,1))
            return "Size 1 не підтверджений. Вибери Size 3 або виміряй Size 1 зі стабільним ядром.";
        if(!Visual(s))return null;
        if(s.Text("coverage_mode","Precision")!="Precision"||!s.Bool("force_precision_controls",true)||!s.Bool("use_fixed_opacity",true)||s.Number("paint_opacity_value",1)!=1)
            return "Візуальне підтвердження потребує Precision та Opacity 1. Застосуй профіль надійності або поверни зчитування чисел.";
        double size=PaintTimingPlan.DefaultSize(s);
        bool pending=s.Data.ContainsKey(PendingKey)&&!(s.Data[PendingKey] is System.Text.Json.Nodes.JsonValue v&&v.GetValueKind()==System.Text.Json.JsonValueKind.False);
        if(pending||!BrushFootprints.Read(s,s.Bool("adaptive_brush")&&s.Bool("adaptive_auto_shape")).Any(p=>p.Size==size&&p.SolidCore.Valid))
            return "Спочатку виміряй робочий пензель: візуальна перевірка смуг без підтвердженого відбитка не дозволяє малювання.";
        return null;
    }
}

public sealed record VisualControlEvidence(string Kind,double Expected,double Fraction,SliderObservation? First,SliderObservation? Second,bool Verified);
public static class VisualControlVerification
{
    // Visual mode must not issue numeric copies, even if the clipboard reader
    // would time out or throw. HEX color verification uses its own transport.
    public static double? ReadNumber(Settings s,Func<double?> copyNumber)
        =>CalibrationReliability.Visual(s)?null:copyNumber();
    public static VisualControlEvidence Read(string kind,double expected,Func<SliderObservation?> capture,Action<double> wait,double interval)
    {
        if(!ControlNumber.InRange(kind,expected)||!double.IsFinite(interval)||interval is <.04 or >1)throw new ArgumentException("Invalid visual control check.");
        double fraction=ControlCurve.Fraction(kind,expected);var first=capture();wait(interval);var second=capture();
        bool verified=first is {} a&&second is {} b&&a.Track==b.Track&&a.ValueField==b.ValueField&&a.Matches(fraction)&&b.Matches(fraction);
        return new(kind,expected,fraction,first,second,verified);
    }
}

public static class StableCapture
{
    public static PixelImage Read(Func<PixelImage> capture,Action<double> wait,int attempts,double interval,
        Action<PixelImage,PixelImage,int>? unstable=null,Action<PixelImage,PixelImage>? settled=null)
    {
        if(attempts is <5 or >20||!double.IsFinite(interval)||interval is <.04 or >1)throw new ArgumentException("Invalid stable capture budget.");
        var previous=capture();
        for(int i=0;i<attempts;i++)
        {
            wait(interval);var next=capture();
            if(CoverageAudit.Stable(previous,next)){settled?.Invoke(previous,next);return next;}
            unstable?.Invoke(previous,next,i+1);previous=next;
        }
        throw new InvalidOperationException("Canvas змінюється між кадрами. Зупини рух камери й повтори тест.");
    }
}

public static class ReadbackDiagnostics
{
    public static string? SafeText(string? raw,string? kind=null)
        =>raw is null?null:raw is ControlNumber.Marker or HexReadback.Marker?raw:
            kind is null?HexReadback.Normalize(raw) is {} hex?hex:"[redacted non-HEX text]":
            raw.Length<=32&&ControlNumber.Parse(kind,raw) is not null?raw:"[redacted non-numeric text]";
}
