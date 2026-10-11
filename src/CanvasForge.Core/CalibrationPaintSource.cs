namespace CanvasForge.Core;

public static class CalibrationPaintSource
{
    // The captured active swatch may be Rust's tiny brush-preview dot. Read its
    // unchanged colour at a readable Size, then restore the requested Size before
    // drawing. This never substitutes palette-cell proof or relaxes RGB tolerance.
    public static double ReadableSize(double requested)
    {
        if(!double.IsFinite(requested)||requested is <1 or >100)throw new ArgumentOutOfRangeException(nameof(requested));
        return Math.Max(3,requested);
    }

    public static T Read<T>(double requested,bool palette,Action<double> prepare,Func<T> read)
    {
        double proofSize=palette?ReadableSize(requested):requested;
        if(proofSize!=requested)prepare(proofSize);
        T result=read();
        if(proofSize!=requested)prepare(requested);
        return result;
    }

    public static bool Verify(double requested,bool palette,Action<double> prepare,Func<bool> read)
    {
        double proofSize=palette?ReadableSize(requested):requested;
        if(proofSize!=requested)prepare(proofSize);
        // On failure leave the readable Size selected, so retrying can obtain
        // fresh colour proof before making another small calibration stamp.
        bool passed=read();
        if(passed&&proofSize!=requested)prepare(requested);
        return passed;
    }
}
