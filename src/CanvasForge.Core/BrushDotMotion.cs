namespace CanvasForge.Core;

public sealed record BrushDotStep(string Phase,double Seconds,ScreenPoint Cursor);
public sealed record BrushDotTrace(string Revision,ScreenPoint Command,BrushDotStep[] Steps);

// One stationary input application must never include a swept path.
// Refresh the same movement event while held; do not nudge or recenter the stamp.
public static class BrushDotMotion
{
    public const string Revision="stationary-checked-v1";
    public const double Interval=.064;
    public const string PositionError="Calibration cursor did not settle at the requested point.";
    public const string DriftError="Calibration cursor moved while drawing the dot.";
    public static void CheckSamePoint(BrushDotTrace first,BrushDotTrace repeated)
    {
        if(first.Command!=repeated.Command||first.Steps.Length!=4||repeated.Steps.Length!=4
            ||first.Steps.Any(s=>s.Cursor!=first.Steps[0].Cursor)||repeated.Steps.Any(s=>s.Cursor!=first.Steps[0].Cursor))
            throw new InvalidOperationException("Saturation check did not repeat the original cursor position.");
    }
    public static BrushDotTrace Draw(ScreenPoint command,IStrokeInput input,Func<ScreenPoint> cursor,Action<BrushDotStep>? observe=null)
    {
        var steps=new List<BrushDotStep>();double started=input.Seconds;
        ScreenPoint Record(string phase)
        {
            var actual=cursor();var step=new BrushDotStep(phase,input.Seconds-started,actual);
            steps.Add(step);observe?.Invoke(step);return actual;
        }
        input.Move([command]);input.Wait(Interval);
        var settled=Record("settled");
        // Windows' 16-bit absolute-coordinate normalization can land one pixel
        // from the integer command. Keep that observation; never shift the mask.
        if(Math.Abs((long)settled.X-command.X)>1||Math.Abs((long)settled.Y-command.Y)>1)
            throw new InvalidOperationException(PositionError);
        try
        {
            input.Button(false);input.Wait(Interval);
            if(Record("held")!=settled)throw new InvalidOperationException(DriftError);
            input.Move([command]);input.Wait(Interval);
            if(Record("refreshed")!=settled)throw new InvalidOperationException(DriftError);
        }
        finally{input.Button(true);}
        input.Wait(Interval);
        if(Record("released")!=settled)throw new InvalidOperationException(DriftError);
        return new(Revision,command,steps.ToArray());
    }
}

public static class BrushControlReuse
{
    public static string[] Required(bool sameShape,IReadOnlyDictionary<string,double> verified,
        double size,double interval,double opacity,Func<string,double,bool> visualMatch)
        =>new[]{("size",size),("interval",interval),("opacity",opacity)}
            .Where(p=>!sameShape||!verified.TryGetValue(p.Item1,out double value)||value!=p.Item2||!visualMatch(p.Item1,p.Item2))
            .Select(p=>p.Item1).ToArray();
}
