using System.Diagnostics;
using CanvasForge.Core;

namespace CanvasForge.App;

internal readonly record struct InputCosts(long SafetyChecks,double SafetySeconds,long MovementCalls,double SendInputSeconds,
    long WaitCalls,double RequestedWaitSeconds,double ActualWaitSeconds)
{
    public static InputCosts operator +(InputCosts a,InputCosts b)=>new(a.SafetyChecks+b.SafetyChecks,a.SafetySeconds+b.SafetySeconds,
        a.MovementCalls+b.MovementCalls,a.SendInputSeconds+b.SendInputSeconds,a.WaitCalls+b.WaitCalls,
        a.RequestedWaitSeconds+b.RequestedWaitSeconds,a.ActualWaitSeconds+b.ActualWaitSeconds);
    public static InputCosts operator -(InputCosts a,InputCosts b)=>new(a.SafetyChecks-b.SafetyChecks,a.SafetySeconds-b.SafetySeconds,
        a.MovementCalls-b.MovementCalls,a.SendInputSeconds-b.SendInputSeconds,a.WaitCalls-b.WaitCalls,
        a.RequestedWaitSeconds-b.RequestedWaitSeconds,a.ActualWaitSeconds-b.ActualWaitSeconds);
}

internal sealed partial class Painter
{
    // Safety checks executed inside a wait overlap ActualWaitSeconds; these costs
    // are diagnostic components, not an additive breakdown of wall-clock time.
    private InputCosts InputSnapshot()
    {
        var waits=inputDelay?.Costs??default;
        return new(safetyCalls,safetySeconds,moveCalls,sendInputSeconds,waits.Calls,waits.RequestedSeconds,waits.ActualSeconds);
    }
    private void MovePathMeasured(IReadOnlyList<ScreenPoint> points)
    {
        long started=Stopwatch.GetTimestamp();moveCalls++;
        try{Native.MovePath(points);}
        finally{sendInputSeconds+=Stopwatch.GetElapsedTime(started).TotalSeconds;}
    }
    public void VerifyControlLayout()
    {
        Check();if(Paused){if(probeMode)CheckProbe();throw new InputInterrupted();}
        var expected=settings.PaintCalibration();var kinds=new[]{"size","interval","opacity"};
        var found=kinds.Select(k=>ReadSlider(k,expected) is not null).ToArray();
        if(found.All(x=>x))return;
        var other=settings.Clone();other.Set("color_mode",settings.Mode==ColorMode.HexDirect?"Rust Palette":"HEX Direct");
        bool[] alternate=[false,false,false];int distinct=0;
        if(other.Mode!=ColorMode.HexDirect||other.HexControlsReady)
        {
            var calibration=other.PaintCalibration();
            alternate=kinds.Select(k=>ReadSlider(k,calibration) is not null).ToArray();
            distinct=kinds.Count(k=>Math.Abs(calibration.Rect(k+"_track").Center.Y-expected.Rect(k+"_track").Center.Y)>24);
        }
        var state=ControlLayout.Inspect(found,alternate,distinct);
        Log("control_layout",new{mode=settings.Mode.ToString(),state=state.ToString(),expected=found,alternative=alternate,distinctTracks=distinct});
        if(state==ControlLayoutState.DifferentMode)throw new InvalidOperationException(settings.Mode==ColorMode.HexDirect?ControlLayout.HexMismatch:ControlLayout.PaletteMismatch);
        throw new InvalidOperationException(ControlLayoutProblem(kinds[Array.FindIndex(found,x=>!x)]));
    }
    private string ControlLayoutProblem(string kind)=>
        $"Не вдалося прочитати {kind} у режимі {(settings.Mode==ColorMode.HexDirect?"HEX Direct":"Rust Palette")}. Перевір, що в Rust відкрита відповідна палітра, і захопи повзунок із числом справа.";
}
