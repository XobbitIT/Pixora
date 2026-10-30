using CanvasForge.Core;
namespace CanvasForge.App;
internal interface IIntentStrokeInput:ICalibratedStrokeInput
{
    void Move(IReadOnlyList<ScreenPoint> points,InputIntent intent);
}
