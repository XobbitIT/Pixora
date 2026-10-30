namespace CanvasForge.Core;

// Tracking is scoped to the ordinary Standard transaction. Frozen calibration
// protocols must not acquire a different emitter or measurement cost silently.
public interface ITrackedStrokeInput : IPositionVerifiedStrokeInput
{
    void BeginCursorTracking(ScreenPoint expected);
    void EndCursorTracking();
}
