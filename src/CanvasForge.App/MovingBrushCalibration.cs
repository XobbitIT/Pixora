using CanvasForge.Core;
using System.Text.Json;

namespace CanvasForge.App;

internal sealed partial class MainWindow
{
    private async Task CalibrateMovingBrushes(nint target,IReadOnlyList<BrushFootprint> profiles,IReadOnlyList<BrushCalibrationTile> dots,Action check)
    {
        var selected=profiles.Where(p=>p.Size>=3&&BrushFootprints.CalibrationSizes.Contains(p.Size)).OrderByDescending(p=>p.Size).ToArray();
        if(selected.Length==0)return;
        var expected=settings;var expectedContent=ConfigOwnership.ContentIdentity(expected);var owner=expected.Clone();
        bool Current()=>Dispatcher.Invoke(()=>ReferenceEquals(expected,settings)&&ConfigOwnership.ContentIdentity(settings)==expectedContent);
        var workspace=setupWorkspace??new SetupWorkspace(settings.Calibration.Rect("canvas"));
        if(setupWorkspace is null)workspace.ReserveExisting(dots.Select(t=>t.Area));
        string directory=Path.Combine(folder,"brush-calibration","stroke-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        DiagnosticRetention.Prune(Path.GetDirectoryName(directory)!,directory);
        await Task.Run(()=>
        {
            var route=BrushMotionEvidence.Route(AdaptiveBrush.CalibrationSettings(owner,3));
            using var worker=new Painter(route,target,ResumePath,LogPath,_=>{},SetupToken);
            if(selected.FirstOrDefault(p=>p.Size==3) is {} control)
            {
                try
                {
                    var exposures=workspace.Brush([3d]);worker.PrepareBrushCalibration(3);
                    int exposureSeed=Random.Shared.Next();var exposureRandom=new Random(exposureSeed);
                    var exposureOrder=Enumerable.Range(0,3).OrderBy(_=>exposureRandom.Next()).ToArray();
                    foreach(int index in exposureOrder)
                    {
                        var tile=exposures[index];int milliseconds=new[]{0,64,128}[index];
                        var before=worker.StableProbeShot(tile.Area,(_,_,_)=>{});check();
                        var requested=worker.SelectCalibrationColor(before.Color(before.Width*before.Height/2));
                        if(!worker.VerifyCalibrationPaint(requested))throw new InvalidDataException("stroke_source_unverified");
                        before=worker.StableProbeShot(tile.Area,(_,_,_)=>{});worker.ProbeExposureDot(tile.Command,milliseconds);
                        var after=worker.StableProbeShot(tile.Area,(_,_,_)=>{});check();
                        Images.Save(before,Path.Combine(directory,$"exposure-{milliseconds}-before.png"));Images.Save(after,Path.Combine(directory,$"exposure-{milliseconds}-after.png"));
                        SessionLog.Write(LogPath,"brush_exposure_observed",new{milliseconds,control.Size,tile.Command,seed=exposureSeed,order=exposureOrder,
                            contrast=BrushFootprints.Contrast(before,after),sourceAfter=worker.VerifyCalibrationPaint(requested),directory,certifiesProfile=false});
                    }
                }
                catch(Exception error) when(error is InvalidDataException||error is InvalidOperationException&&error.Message.StartsWith("Not enough clean space",StringComparison.Ordinal))
                {SessionLog.Write(LogPath,"brush_exposure_unavailable",new{reason=error.Message,directory});}
            }
            foreach(var dot in selected)
            {
                check();if(!Current()||BrushFootprints.Find(owner,dot.Size,dot.ShapeSlot)?.Id!=dot.Id)
                    throw new PreparationInterruptedException("Brush settings changed during moving calibration.");
                StrokeProfiles.Invalidate(owner,dot);
                foreach(bool vertical in new[]{false,true})
                {
                    int seed=Random.Shared.Next();var rng=new Random(seed);var observations=new StrokeObservation?[3];
                    try
                    {
                        var tiles=workspace.Stroke(dot.Size);
                        var order=Enumerable.Range(0,3).OrderBy(_=>rng.Next()).ToArray();
                        worker.PrepareBrushCalibration(dot.Size);check();Rgb? requested=null;
                        foreach(int position in order)
                        {
                            var area=tiles[position].Area;int length=StrokeProfiles.CalibrationLength;
                            var line=vertical?new ScreenLine(area.Center.X,area.Center.Y-length/2,area.Center.X,area.Center.Y+length/2)
                                :new ScreenLine(area.Center.X-length/2,area.Center.Y,area.Center.X+length/2,area.Center.Y);
                            var local=new ScreenLine(line.X1-area.Left,line.Y1-area.Top,line.X2-area.Left,line.Y2-area.Top);
                            PixelImage? background=null;var before=worker.StableProbeShot(area,(_,_,_)=>{},(p,_)=>background=p);check();
                            requested??=worker.SelectCalibrationColor(before.Color(before.Width*before.Height/2));
                            bool sourceBefore=worker.VerifyCalibrationPaint(requested.Value);check();
                            if(!sourceBefore)throw new InvalidDataException("stroke_source_unverified");
                            before=worker.StableProbeShot(area,(_,_,_)=>{},(p,_)=>background=p);check();
                            worker.ProbeBrushStroke(line);var first=worker.StableProbeShot(area,(_,_,_)=>{});check();
                            bool sourceMiddle=worker.VerifyCalibrationPaint(requested.Value);check();
                            if(!sourceMiddle)throw new InvalidDataException("stroke_source_unverified");
                            worker.ProbeBrushStroke(line);var repeated=worker.StableProbeShot(area,(_,_,_)=>{});check();
                            worker.ProbeBrushStroke(line);var third=worker.StableProbeShot(area,(_,_,_)=>{});check();
                            bool sourceAfter=worker.VerifyCalibrationPaint(requested.Value);check();
                            string stem=$"shape-{dot.ShapeSlot}-size-{dot.Size}-{(vertical?"V":"H")}-position-{position}";
                            foreach(var(im,name) in new[]{(background??before,"background"),(before,"before"),(first,"first"),(repeated,"repeated"),(third,"third")})
                                Images.Save(im,Path.Combine(directory,stem+"-"+name+".png"));
                            File.WriteAllText(Path.Combine(directory,stem+".json"),JsonSerializer.Serialize(new{dot.Id,dot.Size,dot.ShapeSlot,
                                area,line,local,seed,order,position,role=position==2?"heldout":"training",requested,
                                sourceBefore,sourceMiddle,sourceAfter,emitter=StandardStrokeMotion.Revision,context=StrokeProfiles.Context(owner,dot)}));
                            observations[position]=StrokeProfiles.Measure(dot,local,background??before,before,first,repeated,requested.Value,sourceBefore&&sourceMiddle&&sourceAfter);
                            var stableThird=StrokeProfiles.Measure(dot,local,background??before,before,first,third,requested.Value,sourceBefore&&sourceMiddle&&sourceAfter);
                            var prior=observations[position]!;
                            observations[position]=prior with{Possible=BrushSweep.Merge(prior.Possible.Concat(stableThird.Possible)),
                                Solid=BrushFootprints.Spans(BrushFootprints.Points(prior.Solid).Intersect(BrushFootprints.Points(stableThird.Solid)))};
                        }
                        var profile=StrokeProfiles.Fit(owner,dot,vertical,[observations[0]!,observations[1]!],observations[2]!,seed);
                        if(!Current())throw new PreparationInterruptedException("Brush settings changed during moving calibration.");
                        StrokeProfiles.Save(owner,profile);
                        StrokeProfiles.Report(owner,dot,vertical,seed,"verified");
                        File.WriteAllText(Path.Combine(directory,$"profile-{profile.Id}.json"),JsonSerializer.Serialize(profile));
                        SessionLog.Write(LogPath,"stroke_profile_verified",new{dot.Size,vertical,profile.Id,seed,directory,length=profile.Length});
                    }
                    catch(Exception error) when(error is InvalidDataException or BrushContrastException)
                    {StrokeProfiles.Report(owner,dot,vertical,seed,error.Message);SessionLog.Write(LogPath,"stroke_profile_rejected",new{dot.Size,vertical,seed,directory,reason=error.Message});}
                    catch(InvalidOperationException error) when(error.Message.StartsWith("Not enough clean space",StringComparison.Ordinal))
                    {StrokeProfiles.Report(owner,dot,vertical,seed,"insufficient_clean_space");SessionLog.Write(LogPath,"stroke_profile_rejected",new{dot.Size,vertical,seed,directory,reason="insufficient_clean_space"});}
                    Dispatcher.Invoke(()=>
                    {
                        check();
                        if(!PublishSettingsCandidate(expected,expectedContent,owner,"moving_stroke_calibration"))
                            throw new PreparationInterruptedException("Brush settings changed during moving calibration.");
                        expected=settings;expectedContent=ConfigOwnership.ContentIdentity(expected);owner=expected.Clone();
                    });
                }
            }
        },SetupToken);
    }
}
