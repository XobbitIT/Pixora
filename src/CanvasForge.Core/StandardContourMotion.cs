namespace CanvasForge.Core;

// Explicit experiment entry only; ordinary Standard never dispatches here.
public static class StandardContourMotion
{
    public const string Revision="standard-dense-contour-experiment-v1";
    public static void Draw(IReadOnlyList<ScreenPoint> points,ScreenRect canvas,Settings settings,SpeedProfile speed,ITrackedStrokeInput input)
    {
        // Freeze caller-owned input before any input or timing is sent.
        var path=points.ToArray();StandardBandContour.Validate(path,canvas);
        if(!ReleaseProfile.Standard(settings)||StandardLineMotion.Enabled(settings)||speed.Pitch is <1 or >3||speed.BatchSize is <1 or >16)
            throw new InvalidDataException("Contour experiment requires explicit Standard dense context.");
        try
        {
            input.Move([path[0]]);input.Wait(StrokeTiming.Settle(settings,speed));input.ConfirmPosition(path[0]);
            input.BeginCursorTracking(path[0]);input.Button(false);double heldFrom=input.Seconds;
            input.Wait(StrokeTiming.Frame(settings));int span=speed.Pitch*speed.BatchSize;
            for(int first=1;first<path.Length;)
            {
                int count=Math.Min(span,path.Length-first);input.Move(path.AsSpan(first,count).ToArray());
                input.Wait(speed.PointDelay*((count+speed.Pitch-1)/speed.Pitch));first+=count;
            }
            input.Wait(StrokeTiming.EndHold(settings,speed));double held=input.Seconds-heldFrom;if(held<.04)input.Wait(.04-held);
        }
        finally{try{input.Button(true);}finally{input.EndCursorTracking();}}
        input.ConfirmPosition(path[^1]);input.Wait(StrokeTiming.Release(settings));
    }
}
