namespace CanvasForge.Core;

// Candidate for Rust's native Shift-drag line tool, documented by Facepunch:
// https://rust.facepunch.com/news/shipshape
// Keep opt-in until physical line quality is independently checked in Rust.
// Ordinary/Adaptive motion and its existing profile revision are unchanged.
public static class StandardLineMotion
{
    public const string Revision="standard-native-shift-line-v3";
    public static bool Enabled(Settings settings)=>ReleaseProfile.Standard(settings)
        &&settings.Bool("standard_native_line",false)&&settings.Text("input_engine","Stable")=="Stable"
        &&StrokeTiming.Frame(settings)>=.020;
    public static bool Applies(Settings settings,ScreenLine line)=>Enabled(settings)
        &&(line.X1==line.X2||line.Y1==line.Y2)&&TransferSchedule.Length(line)>0;
    public static void Draw(ScreenLine line,Settings settings,bool reverse,ICalibratedStrokeInput input)
    {
        if(!Enabled(settings))throw new InvalidOperationException("Native line prototype requires explicit Standard opt-in and Stable timing of at least20ms. Physical timing remains unqualified.");
        DrawTransaction(line,StrokeTiming.Frame(settings),StrokeTiming.Release(settings),reverse,false,input);
    }
    // The alternate order is confined to the diagnostic runner. It is never
    // selected by production settings or treated as a qualified paint route.
    public static void Diagnostic(ScreenLine line,int frameMs,bool reverse,bool shiftAfterDown,ICalibratedStrokeInput input)
    {
        if(frameMs is not (20 or 40 or 80))throw new ArgumentOutOfRangeException(nameof(frameMs));
        DrawTransaction(line,frameMs/1000d,frameMs/1000d,reverse,shiftAfterDown,input);
    }
    private static void DrawTransaction(ScreenLine line,double frame,double release,bool reverse,bool shiftAfterDown,ICalibratedStrokeInput input)
    {
        if(line.X1!=line.X2&&line.Y1!=line.Y2||TransferSchedule.Length(line)==0)
            throw new ArgumentException("Expected a nonzero cardinal line.");
        var a=new ScreenPoint(reverse?line.X2:line.X1,reverse?line.Y2:line.Y1);
        var b=new ScreenPoint(reverse?line.X1:line.X2,reverse?line.Y1:line.Y2);
        input.Move([a]);input.Wait(frame);
        Confirm(a);
        try
        {
            if(!shiftAfterDown){input.Shift(false);input.Wait(frame);}
            try
            {
                Confirm(a);
                input.Button(false);input.Wait(frame);
                if(shiftAfterDown){input.Shift(false);input.Wait(frame);}
                input.Move([b]);input.Wait(frame);
                Confirm(b);
            }
            finally{input.Button(true);}
            // Commit with Shift still held, then release it as a separate phase.
            input.Wait(frame);
        }
        finally{input.Shift(true);}
        input.Wait(Math.Max(frame,release));
        void Confirm(ScreenPoint expected)
        {if(input is IPositionVerifiedStrokeInput verified)verified.ConfirmPosition(expected);}
    }
}

public interface IPositionVerifiedStrokeInput:ICalibratedStrokeInput
{
    void ConfirmPosition(ScreenPoint expected);
}
