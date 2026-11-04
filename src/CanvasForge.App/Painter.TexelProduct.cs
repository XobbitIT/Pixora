using System.Text.Json;
using CanvasForge.Core;
namespace CanvasForge.App;
internal sealed partial class Painter
{
    private TexelCompensatedPlan? texelProductPlan;
    private bool texelViewRequired=true;
    private TexelProductSession.Admission? texelSession;
    private void PrepareTexelProduct(PaintPlan plan)
    {
        if(plan.ExperimentalTexel is not {} route)
        {if(TexelExperimentSettings.Enabled(settings))throw new InvalidDataException("Experimental START has no compensated route.");return;}
        texelSession=TexelProductSession.Resolve(settings);
        TexelProduct.ValidateSettings(settings,route,texelSession);
        TexelProduct.ValidateLive(route,Native.ClientOrigin(window),Native.ClientSize(window),Native.DpiOf(window));
        texelProductPlan=route;texelViewRequired=true;
        if(texelSession is {} bound&&runSnapshotDirectory is not null)
        {
            File.WriteAllBytes(Path.Combine(runSnapshotDirectory,"session-binding.json"),bound.Record);
            File.WriteAllText(Path.Combine(runSnapshotDirectory,"texel-session.json"),JsonSerializer.Serialize(new{
                revision=TexelProductSession.Revision,bindingId=bound.Binding.Id,bindingRecordSha256=bound.RecordSha256,
                requestedSpeed=settings.Text("speed_profile"),effectiveSpeed=bound.Binding.EffectiveEmitterSpeed,rect=bound.Binding.UserRect,
                modelRevision=TexelCompensatedPlanner.Revision,profileSha256=TexelProduct.ProfileSha256,routeId=route.Id,planIdentity=plan.Identity}));
        }
        if(runSnapshotDirectory is not null)File.WriteAllText(Path.Combine(runSnapshotDirectory,"experimental-texel-plan.json"),JsonSerializer.Serialize(new
        {
            revision=TexelCompensatedPlanner.Revision,profileSha256=TexelProduct.ProfileSha256,route,
            source="row-calib1-derived1196 / pic8-pic9 full-coverage end b+1.867",actualEmitter=StandardContourMotion.Revision,
            actualSpeed=StandardRepeatSeries.Speed,physicalQualityCertified=false,productionEligible=false,
            targetPixelIsTexel=true,perStrokeNativeCapture=false
        },new JsonSerializerOptions{WriteIndented=true}));
    }
    // Lineage for the offline review of a run resumed with «Продовжити»: the checkpoint RunKey survives every resume of one
    // START, so a resumed run can be chained only to the interrupted runs of that same START (TexelProductReview --resumed-from).
    private void SaveTexelCheckpointLink(PaintPlan plan,ResumeCheckpoint? resume,int done)
    {
        // Diagnostic only: a missing key (legacy checkpoint) or a failed write never blocks painting; the review then refuses to chain.
        if(plan.ExperimentalTexel is null||runSnapshotDirectory is null||checkpoint?.Latest.RunKey is not {} key)return;
        try
        {
            File.WriteAllText(Path.Combine(runSnapshotDirectory,TexelProductReview.CheckpointFile),JsonSerializer.Serialize(new TexelProductReview.CheckpointLink(
                TexelProductReview.CheckpointRevision,key,plan.Identity,resume is not null,done,plan.ExperimentalTexel.Runs.Count)));
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){Log("texel_checkpoint_link_unavailable",new{reason=e.Message});}
    }
    private void DrawTexelProduct(TexelCompensatedRun run)
    {
        var plan=texelProductPlan??throw new InvalidDataException("No experimental route.");
        // Re-read before every stroke: deletion, deactivation or an edit fails before down.
        if(texelSession is not null&&TexelProductSession.Resolve(settings)?.Identity!=texelSession.Identity)
            throw new InvalidDataException("Session binding changed after preparation.");
        CheckBoundary();TexelProduct.ValidateLive(plan,Native.ClientOrigin(window),Native.ClientSize(window),Native.DpiOf(window));
        if(texelViewRequired)
        {
            Native.ReleaseChecked();var full=Native.Screenshot(new(0,0,2560,1440));TexelProduct.RequireView(full);
            if(texelSession is {} session)
            {
                var b=session.Binding;var local=new ScreenRect(b.UserRect.Left-b.ClientOrigin.X,b.UserRect.Top-b.ClientOrigin.Y,b.UserRect.Right-b.ClientOrigin.X,b.UserRect.Bottom-b.ClientOrigin.Y);
                var proof=CanvasStructure.Read(CanvasStructure.Reference(settings),local,full,new(0,0,2560,1440),b.ClientOrigin,b.Dpi,b.ClientSize);
                if(!proof.Passed)throw new InvalidDataException("Session view refused: "+proof.Reason);
                if(runSnapshotDirectory is null)throw new InvalidDataException("Session START requires a run snapshot.");
                Images.Save(full,Path.Combine(runSnapshotDirectory,"texel-fresh-view.png"));
                File.WriteAllText(Path.Combine(runSnapshotDirectory,"texel-fresh-view.json"),JsonSerializer.Serialize(new{
                    bindingId=b.Id,rgbaSha256=TexelProductSession.Hash(full.Rgba),proof,physicalQualityCertified=false}));
            }
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
