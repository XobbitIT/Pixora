namespace CanvasForge.Core;

// Owns a private, immutable copy of the fully validated proof. Each shape is
// validated once by the same resolver used for timing, not once per input event.
// The Painter still checks the live window, DPI, focus and cancellation at input.
public sealed class StrokeExecutionPlan
{
    public const string Revision="private-proof-snapshot-v1";
    private readonly Settings settings;
    private readonly SpeedProfile speed;
    private readonly Func<double,ScreenLine,int,SpeedSample?> resolve;
    private readonly double defaultSize;
    public bool Fast {get;}
    public StrokeExecutionPlan(Settings source)
    {
        settings=BrushFootprints.Snapshot(source.Clone());
        speed=SpeedProfile.Get(settings.Text("speed_profile","Rapid"));
        defaultSize=PaintTimingPlan.DefaultSize(settings,speed);
        resolve=SpeedCalibration.CreateEstimateResolver(settings);
        Fast=TransferSchedule.Fast(settings);
    }
    public SpeedSample? Resolve(double size,ScreenLine line,int shape=0)=>resolve(size,line,shape);
    public bool FastBatch(PaintBatch batch)=>Fast&&(batch.Segments.Count!=1||Resolve(batch.Size>0?batch.Size:defaultSize,batch.Segments[0],batch.ShapeSlot) is null);
    public double Estimate(PaintBatch batch)=>TransferSchedule.EstimateBatch(settings,speed,batch,resolve,Fast);
}
