using System.Diagnostics;
using CanvasForge.Core;

namespace CanvasForge.App;

internal readonly record struct InputCosts(long SafetyChecks,double SafetySeconds,long MovementCalls,double SendInputSeconds,
    long WaitCalls,double RequestedWaitSeconds,double ActualWaitSeconds,
    long ToolProofChecks,double ToolProofSeconds,long EditorProofChecks,double EditorProofSeconds)
{
    public CursorReadCosts GuardCursor{get;init;}
    public long MovementPoints{get;init;}
    public NativeSendCosts NativeSend{get;init;}
    public string scope=>"cumulative Run worker; NativeSend measures all its mouse/keyboard SendInput API calls; SendInputSeconds measures the movement path including packing; GuardCursor covers tracked path, endpoint and paint-down reads";
    public string overlap=>"wait actual includes safety/editor/tool/cursor checks; nested diagnostic durations are not additive wall time";
    public static InputCosts operator +(InputCosts a,InputCosts b)=>new(a.SafetyChecks+b.SafetyChecks,a.SafetySeconds+b.SafetySeconds,
        a.MovementCalls+b.MovementCalls,a.SendInputSeconds+b.SendInputSeconds,a.WaitCalls+b.WaitCalls,
        a.RequestedWaitSeconds+b.RequestedWaitSeconds,a.ActualWaitSeconds+b.ActualWaitSeconds,
        a.ToolProofChecks+b.ToolProofChecks,a.ToolProofSeconds+b.ToolProofSeconds,a.EditorProofChecks+b.EditorProofChecks,a.EditorProofSeconds+b.EditorProofSeconds)
        {GuardCursor=new(a.GuardCursor.Reads+b.GuardCursor.Reads,a.GuardCursor.FailedReads+b.GuardCursor.FailedReads,a.GuardCursor.Seconds+b.GuardCursor.Seconds),MovementPoints=a.MovementPoints+b.MovementPoints,
            NativeSend=new(a.NativeSend.Calls+b.NativeSend.Calls,a.NativeSend.RequestedEvents+b.NativeSend.RequestedEvents,a.NativeSend.AcceptedEvents+b.NativeSend.AcceptedEvents,a.NativeSend.FailedCalls+b.NativeSend.FailedCalls,a.NativeSend.Seconds+b.NativeSend.Seconds)};
    public static InputCosts operator -(InputCosts a,InputCosts b)=>new(a.SafetyChecks-b.SafetyChecks,a.SafetySeconds-b.SafetySeconds,
        a.MovementCalls-b.MovementCalls,a.SendInputSeconds-b.SendInputSeconds,a.WaitCalls-b.WaitCalls,
        a.RequestedWaitSeconds-b.RequestedWaitSeconds,a.ActualWaitSeconds-b.ActualWaitSeconds,
        a.ToolProofChecks-b.ToolProofChecks,a.ToolProofSeconds-b.ToolProofSeconds,a.EditorProofChecks-b.EditorProofChecks,a.EditorProofSeconds-b.EditorProofSeconds)
        {GuardCursor=new(a.GuardCursor.Reads-b.GuardCursor.Reads,a.GuardCursor.FailedReads-b.GuardCursor.FailedReads,a.GuardCursor.Seconds-b.GuardCursor.Seconds),MovementPoints=a.MovementPoints-b.MovementPoints,
            NativeSend=new(a.NativeSend.Calls-b.NativeSend.Calls,a.NativeSend.RequestedEvents-b.NativeSend.RequestedEvents,a.NativeSend.AcceptedEvents-b.NativeSend.AcceptedEvents,a.NativeSend.FailedCalls-b.NativeSend.FailedCalls,a.NativeSend.Seconds-b.NativeSend.Seconds)};
}

internal sealed partial class Painter
{
    private long toolProofChecks,editorProofChecks;
    private double toolProofSeconds,editorProofSeconds;
    private readonly NativeSendMetrics nativeSendMetrics=new();
    // Safety checks executed inside a wait overlap ActualWaitSeconds; these costs
    // are diagnostic components, not an additive breakdown of wall-clock time.
    private InputCosts InputSnapshot()
    {
        var waits=inputDelay?.Costs??default;
        return new(safetyCalls,safetySeconds,moveCalls,sendInputSeconds,waits.Calls,waits.RequestedSeconds,waits.ActualSeconds,
            toolProofChecks,toolProofSeconds,editorProofChecks,editorProofSeconds)
            {GuardCursor=cursorReadMetrics?.Costs??default,MovementPoints=emittedMovementPoints,NativeSend=nativeSendMetrics?.Costs??default};
    }
    private void MovePathMeasured(IReadOnlyList<ScreenPoint> points)
    {
        long started=Stopwatch.GetTimestamp();moveCalls++;emittedMovementPoints+=points.Count;
        try{Native.MovePath(points);}
        finally{sendInputSeconds+=Stopwatch.GetElapsedTime(started).TotalSeconds;}
    }
    public void VerifyControlLayout()
    {
        Check();if(Paused){if(probeMode)CheckProbe();throw new InputInterrupted();}
        var expected=settings.PaintCalibration();var kinds=new[]{"size"};
        var found=kinds.Select(k=>ReadSlider(k,expected) is not null).ToArray();
        if(found.All(x=>x))return;
        var other=settings.Clone();other.Set("color_mode",settings.Mode==ColorMode.HexDirect?"Rust Palette":"HEX Direct");
        bool[] alternate=[false];int distinct=0;
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
