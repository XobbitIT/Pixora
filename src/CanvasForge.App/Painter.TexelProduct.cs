using System.Text.Json;
using CanvasForge.Core;
namespace CanvasForge.App;
internal sealed partial class Painter
{
    private TexelCompensatedPlan? texelProductPlan;
    private bool texelViewRequired=true;
    private void PrepareTexelProduct(PaintPlan plan)
    {
        if(plan.ExperimentalTexel is not {} route)
        {if(TexelExperimentSettings.Enabled(settings))throw new InvalidDataException("Experimental START has no compensated route.");return;}
        TexelProduct.ValidateSettings(settings,route);
        TexelProduct.ValidateLive(route,Native.ClientOrigin(window),Native.ClientSize(window),Native.DpiOf(window));
        texelProductPlan=route;texelViewRequired=true;
        if(runSnapshotDirectory is not null)File.WriteAllText(Path.Combine(runSnapshotDirectory,"experimental-texel-plan.json"),JsonSerializer.Serialize(new
        {
            revision=TexelCompensatedPlanner.Revision,profileSha256=TexelProduct.ProfileSha256,route,
            source="row-calib1-derived1196 / pic8-pic9 full-coverage end b+1.867",actualEmitter=StandardContourMotion.Revision,
            actualSpeed=StandardRepeatSeries.Speed,physicalQualityCertified=false,productionEligible=false,
            targetPixelIsTexel=true,perStrokeNativeCapture=false
        },new JsonSerializerOptions{WriteIndented=true}));
    }
    private void DrawTexelProduct(TexelCompensatedRun run)
    {
        var plan=texelProductPlan??throw new InvalidDataException("No experimental route.");
        CheckBoundary();TexelProduct.ValidateLive(plan,Native.ClientOrigin(window),Native.ClientSize(window),Native.DpiOf(window));
        if(texelViewRequired)
        {
            Native.ReleaseChecked();TexelProduct.RequireView(Native.Screenshot(new(0,0,2560,1440)));
            texelViewRequired=false;CheckBoundary();
        }
        GuardedNativeInput.Path(coordinateGuard,run.Path.Points,CheckBoundary,CoordinateDenied);
        try
        {
            StandardContourMotion.Draw(run.Path.Points,plan.Context.Geometry.Canvas,settings,StandardRepeatSeries.Speed,(ITrackedStrokeInput)motionInput);
            lastPaintPoint=Native.Cursor();
        }
        finally{PersistAfterRelease(Native.Release,PersistPaintOwnership);}
    }
}
