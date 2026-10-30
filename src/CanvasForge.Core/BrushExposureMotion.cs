namespace CanvasForge.Core;

// Diagnostic exposure only; its pixels never certify a stroke profile.
public static class BrushExposureMotion
{
    public static void Draw(ScreenPoint point,int milliseconds,ICalibratedStrokeInput input)
    {
        if(milliseconds is not (0 or 64 or 128))throw new ArgumentOutOfRangeException(nameof(milliseconds));
        input.Move([point]);input.Wait(.064);
        try{input.Button(false);if(milliseconds>0)input.Wait(milliseconds/1000d);}
        finally{input.Button(true);}
        input.Wait(.064);
    }
}
