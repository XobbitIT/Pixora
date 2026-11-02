using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

internal sealed partial class Painter
{
    internal StandardEvidenceSeriesRunner.Result CollectStandardEvidence(StandardEvidenceSeries protocol,string directory)
    {
        protocol.Validate();return CollectEvidenceCases(protocol.Canvas,protocol.Identity(),directory,
            "native before/after each single stroke; compound bands before/after each colour; every stroke has cursor trace",
            hooks=>StandardEvidenceSeriesRunner.Run(protocol,directory,hooks));
    }
    internal StandardEvidenceSeriesRunner.Result CollectStandardRepeats(StandardRepeatSeries protocol,string directory)
    {
        protocol.Validate();return CollectEvidenceCases(protocol.Canvas,protocol.Identity(),directory,
            "native before/after passes1/2/3 at exactly the same coordinates; training24 heldout8; four colour-neighbour cases; final native capture",
            hooks=>StandardEvidenceSeriesRunner.Run(protocol,directory,hooks));
    }
    internal StandardEvidenceSeriesRunner.Result CollectContourPilot(StandardContourPilotCli.Manifest manifest,string directory)
    {
        var paths=manifest.Cases.ToDictionary(c=>c.Sample.Index,c=>c.Paths);
        PixelImage? baseline=manifest.Condition=="candidate"?StandardBoundaryReview.ReadNative(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(directory))!,"control","baseline.png"),manifest.Context.Canvas):null;
        return CollectEvidenceCases(manifest.Context.Canvas,manifest.ProtocolId,directory,
            "paired control/candidate band experiment; native before/after each band; full actual command path retained; no production qualification",
            hooks=>StandardEvidenceSeriesRunner.RunCases(manifest.Context.Canvas,manifest.Cases.Select(c=>c.Sample).ToArray(),
                StandardContourPilotCli.Revision,manifest.ProtocolId,directory,hooks,finalCapture:true),paths,manifest.Context,
            baseline is null?null:image=>StandardContourPilotCli.ValidateCleanBaseline(manifest,baseline,image));
    }
    internal StandardEvidenceSeriesRunner.Result CollectStraightPilot(StandardStraightPilotCli.Manifest manifest,string directory)
    {
        var paths=manifest.Cases.ToDictionary(c=>c.Sample.Index,c=>c.Paths);
        var baseline=StandardBoundaryReview.ReadNative(Path.Combine(directory,"reference","baseline.png"),manifest.Context.Canvas);
        return CollectEvidenceCases(manifest.Context.Canvas,manifest.ProtocolId,directory,
            "measured full-band placement with constant straight tracks; no endpoint ramp; physical width and caps unqualified",
            hooks=>StandardEvidenceSeriesRunner.RunCases(manifest.Context.Canvas,manifest.Cases.Select(c=>c.Sample).ToArray(),
                StandardStraightPilotCli.Revision,manifest.ProtocolId,directory,hooks,finalCapture:true),paths,manifest.Context,
            image=>StandardStraightPilotCli.CleanBaseline(manifest,baseline,image));
    }
    internal StandardEvidenceSeriesRunner.Result CollectTrackPair(StandardTrackPairCli.Manifest manifest,string directory)
    {
        var paths=manifest.Cases.ToDictionary(c=>c.Sample.Index,c=>c.Paths);string parent=Path.GetDirectoryName(Path.GetFullPath(directory))!;
        var historical=StandardBoundaryReview.ReadNative(Path.Combine(parent,"source","baseline.png"),manifest.Context.Canvas);
        PixelImage? control=manifest.Condition=="shifted"?StandardBoundaryReview.ReadNative(Path.Combine(parent,"control","baseline.png"),manifest.Context.Canvas):null;
        if(control is not null)
        {
            using var live=JsonDocument.Parse(File.ReadAllText(Path.Combine(parent,"control","live-context.json")));
            if(live.RootElement.GetProperty("windowProcessId").GetUInt32()!=windowProcessId)
                throw new InvalidDataException("Rust перезапущено між двома умовами. Для порівняння потрібен той самий процес гри.");
        }
        return CollectEvidenceCases(manifest.Context.Canvas,manifest.ProtocolId,directory,StandardTrackPairCli.CapturePolicy,
            hooks=>StandardEvidenceSeriesRunner.RunCases(manifest.Context.Canvas,manifest.Cases.Select(c=>c.Sample).ToArray(),
                StandardTrackPairCli.Revision,manifest.ProtocolId,directory,hooks,finalCapture:true,captureEachStroke:true),paths,manifest.Context,
            image=>{StandardTrackPairCli.CleanBaseline(manifest,historical,image);if(control is not null)StandardTrackPairCli.CleanBaseline(manifest,control,image);},
            ()=>StandardTrackPairCli.ValidateCompleted(directory,manifest,requireCompleteStatus:false));
    }
    internal StandardEvidenceSeriesRunner.Result CollectBandInversePilot(StandardBandInversePilotCli.Manifest manifest,string directory)
    {
        var paths=manifest.Cases.ToDictionary(c=>c.Sample.Index,c=>c.Paths);
        var baseline=StandardBandInversePilotCli.Baseline(directory,manifest);
        var view=StandardBandInversePilotCli.ValidationView(manifest);
        return CollectEvidenceCases(manifest.Context.Canvas,manifest.ProtocolId,directory,StandardTrackPairCli.CapturePolicy,
            hooks=>StandardEvidenceSeriesRunner.RunCases(manifest.Context.Canvas,manifest.Cases.Select(c=>c.Sample).ToArray(),
                StandardBandInversePilotCli.Revision,manifest.ProtocolId,directory,hooks,finalCapture:true,captureEachStroke:true),paths,manifest.Context,
            image=>StandardTrackPairCli.CleanBaseline(view,baseline,image),
            ()=>StandardTrackPairCli.ValidateCompleted(directory,view,requireCompleteStatus:false));
    }
    internal StandardEvidenceSeriesRunner.Result CollectDiscreteFrontiers(StandardDiscreteBandCli.Pair pair,StandardDiscreteBandCli.Condition condition,string directory)
    {
        string parent=Path.GetDirectoryName(Path.GetFullPath(directory))!;var view=StandardDiscreteBandCli.View(pair,condition);
        bool sessionReference=!string.IsNullOrEmpty(settings.Text(FrontierSessionCapture.Key));
        var baseline=sessionReference?FrontierSessionCapture.Baseline(Path.Combine(parent,"ascending"),pair)
            :StandardBoundaryReview.ReadNative(Path.Combine(parent,"reference/baseline.png"),pair.Context.Canvas);
        PixelImage? prior=null;
        if(condition.Direction=="descending")
        {
            string ascending=Path.Combine(parent,"ascending");
            using var live=JsonDocument.Parse(File.ReadAllText(Path.Combine(ascending,"live-context.json")));
            if(live.RootElement.GetProperty("windowProcessId").GetUInt32()!=windowProcessId)
                throw new InvalidDataException("Rust перезапущено між напрямками. Потрібен той самий процес гри.");
            prior=StandardBoundaryReview.ReadNative(Path.Combine(ascending,"baseline.png"),pair.Context.Canvas);
        }
        return CollectEvidenceCases(pair.Context.Canvas,condition.ProtocolId,directory,StandardTrackPairCli.CapturePolicy,
            hooks=>StandardEvidenceSeriesRunner.RunCases(pair.Context.Canvas,condition.Cases.Select(c=>c.Sample).ToArray(),
                StandardDiscreteBandCli.Revision,condition.ProtocolId,directory,hooks,finalCapture:true,captureEachStroke:true),
            condition.Cases.ToDictionary(c=>c.Sample.Index,c=>c.Paths),pair.Context,
            image=>{if(sessionReference)FrontierSessionCapture.RequireBaseline(baseline,image,pair.Context.Canvas);
                StandardTrackPairCli.CleanBaseline(view,baseline,image);if(prior is not null)StandardTrackPairCli.CleanBaseline(view,prior,image);},
            ()=>{StandardTrackPairCli.ValidateCompleted(directory,view,requireCompleteStatus:false);
                if(sessionReference)FrontierSessionCapture.VerifyUse(directory,pair);});
    }
    internal StandardEvidenceSeriesRunner.Result CollectTexelPilot(StandardTexelPilot.Manifest manifest,string directory)
    {
        var baseline=FrontierSessionCapture.Baseline(directory,StandardTexelPilot.Binding(manifest));
        var historical=StandardBoundaryReview.ReadNative(Path.Combine(directory,"reference/baseline.png"),manifest.Context.Canvas);
        return CollectEvidenceCases(manifest.Context.Canvas,manifest.ProtocolId,directory,StandardTrackPairCli.CapturePolicy,
            hooks=>StandardEvidenceSeriesRunner.RunCases(manifest.Context.Canvas,manifest.Cases.Select(c=>c.Sample).ToArray(),
                StandardTexelPilot.Revision,manifest.ProtocolId,directory,hooks,finalCapture:true,captureEachStroke:true),
            manifest.Cases.ToDictionary(c=>c.Sample.Index,c=>c.Paths),manifest.Context,
            image=>{FrontierSessionCapture.RequireBaseline(baseline,image,manifest.Context.Canvas);FrontierSessionCapture.RequireBaseline(historical,image,manifest.Context.Canvas);},
            ()=>StandardTexelPilot.Verify(directory,manifest,requireCompleteStatus:false));
    }
    internal StandardEvidenceSeriesRunner.Result CollectOffgridPilot(StandardRowOffgridPilot.Manifest manifest,string directory)
    {
        var baseline=FrontierSessionCapture.Baseline(directory,StandardRowOffgridPilot.Binding(manifest));
        return CollectEvidenceCases(manifest.Context.Canvas,manifest.ProtocolId,directory,StandardTrackPairCli.CapturePolicy,
            hooks=>StandardEvidenceSeriesRunner.RunCases(manifest.Context.Canvas,manifest.Cases.Select(c=>c.Sample).ToArray(),
                StandardRowOffgridPilot.Revision,manifest.ProtocolId,directory,hooks,finalCapture:true,captureEachStroke:true),
            manifest.Cases.ToDictionary(c=>c.Sample.Index,c=>c.Paths),manifest.Context,
            image=>StandardRowPilot.RequireFreshBaseline(baseline,image,manifest.Context.Canvas),
            ()=>StandardRowOffgridPilot.Verify(directory,manifest,requireCompleteStatus:false));
    }
    internal StandardEvidenceSeriesRunner.Result CollectCalibPilot(StandardRowCalibPilot.Manifest manifest,string directory)
    {
        var baseline=FrontierSessionCapture.Baseline(directory,StandardRowCalibPilot.Binding(manifest));
        return CollectEvidenceCases(manifest.Context.Canvas,manifest.ProtocolId,directory,StandardTrackPairCli.CapturePolicy,
            hooks=>StandardEvidenceSeriesRunner.RunCases(manifest.Context.Canvas,manifest.Cases.Select(c=>c.Sample).ToArray(),
                StandardRowCalibPilot.Revision,manifest.ProtocolId,directory,hooks,finalCapture:true,captureEachStroke:true),
            manifest.Cases.ToDictionary(c=>c.Sample.Index,c=>c.Paths),manifest.Context,
            image=>StandardRowPilot.RequireFreshBaseline(baseline,image,manifest.Context.Canvas),
            ()=>StandardRowCalibPilot.Verify(directory,manifest,requireCompleteStatus:false));
    }
    internal StandardEvidenceSeriesRunner.Result CollectPicturePilot(StandardRowPicturePilot.Manifest manifest,string directory)
    {
        var baseline=FrontierSessionCapture.Baseline(directory,StandardRowPicturePilot.Binding(manifest));
        return CollectEvidenceCases(manifest.Context.Canvas,manifest.ProtocolId,directory,StandardTrackPairCli.CapturePolicy,
            hooks=>StandardEvidenceSeriesRunner.RunCases(manifest.Context.Canvas,manifest.Cases.Select(c=>c.Sample).ToArray(),
                manifest.Revision,manifest.ProtocolId,directory,hooks,finalCapture:true,captureEachStroke:true),
            manifest.Cases.ToDictionary(c=>c.Sample.Index,c=>c.Paths),manifest.Context,
            image=>StandardRowPilot.RequireFreshBaseline(baseline,image,manifest.Context.Canvas),
            ()=>StandardRowPicturePilot.Verify(directory,manifest,requireCompleteStatus:false));
    }
    // Session calibration 1: the requested speed is the user's own frozen setting (Max Speed); the emitter speed stays sample.Speed.
    internal StandardEvidenceSeriesRunner.Result CollectSessionCalibPilot(StandardSessionCalibPilot.Manifest manifest,string directory)
    {
        var baseline=FrontierSessionCapture.Baseline(directory,StandardSessionCalibPilot.Binding(manifest));
        return CollectEvidenceCases(manifest.Context.Canvas,manifest.ProtocolId,directory,StandardTrackPairCli.CapturePolicy,
            hooks=>StandardEvidenceSeriesRunner.RunCases(manifest.Context.Canvas,manifest.Cases.Select(c=>c.Sample).ToArray(),
                StandardSessionCalibPilot.Revision,manifest.ProtocolId,directory,hooks,finalCapture:true,captureEachStroke:true),
            manifest.Cases.ToDictionary(c=>c.Sample.Index,c=>c.Paths),manifest.Context,
            image=>StandardRowPilot.RequireFreshBaseline(baseline,image,manifest.Context.Canvas),
            ()=>StandardSessionCalibPilot.Verify(directory,manifest,requireCompleteStatus:false),manifest.Binding.RequestedSpeed);
    }
    internal StandardEvidenceSeriesRunner.Result CollectPlanePilot(StandardPlanePilot.Manifest manifest,string directory)
    {
        var baseline=FrontierSessionCapture.Baseline(directory,StandardPlanePilot.Binding(manifest));
        return CollectEvidenceCases(manifest.Context.Canvas,manifest.ProtocolId,directory,StandardTrackPairCli.CapturePolicy,
            hooks=>StandardEvidenceSeriesRunner.RunCases(manifest.Context.Canvas,manifest.Cases.Select(c=>c.Sample).ToArray(),
                StandardPlanePilot.Revision,manifest.ProtocolId,directory,hooks,finalCapture:true,captureEachStroke:true),
            manifest.Cases.ToDictionary(c=>c.Sample.Index,c=>c.Paths),manifest.Context,
            image=>StandardRowPilot.RequireFreshBaseline(baseline,image,manifest.Context.Canvas),
            ()=>StandardPlanePilot.Verify(directory,manifest,requireCompleteStatus:false));
    }
    internal StandardEvidenceSeriesRunner.Result CollectRowPilot(StandardRowPilot.Manifest manifest,string directory)
    {
        var baseline=FrontierSessionCapture.Baseline(directory,StandardRowPilot.Binding(manifest));
        return CollectEvidenceCases(manifest.Context.Canvas,manifest.ProtocolId,directory,StandardTrackPairCli.CapturePolicy,
            hooks=>StandardEvidenceSeriesRunner.RunCases(manifest.Context.Canvas,manifest.Cases.Select(c=>c.Sample).ToArray(),
                StandardRowPilot.Revision,manifest.ProtocolId,directory,hooks,finalCapture:true,captureEachStroke:true),
            manifest.Cases.ToDictionary(c=>c.Sample.Index,c=>c.Paths),manifest.Context,
            image=>StandardRowPilot.RequireFreshBaseline(baseline,image,manifest.Context.Canvas),
            ()=>StandardRowPilot.Verify(directory,manifest,requireCompleteStatus:false));
    }
    internal StandardEvidenceSeriesRunner.Result CollectUvPilot(StandardUvPilot.Manifest manifest,string directory)
    {
        var binding=StandardUvPilot.Binding(manifest);
        var baseline=FrontierSessionCapture.Baseline(directory,binding);
        var historical=StandardBoundaryReview.ReadNative(Path.Combine(directory,"reference/baseline.png"),manifest.Context.Canvas);
        return CollectEvidenceCases(manifest.Context.Canvas,manifest.ProtocolId,directory,StandardTrackPairCli.CapturePolicy,
            hooks=>StandardEvidenceSeriesRunner.RunCases(manifest.Context.Canvas,manifest.Cases.Select(c=>c.Sample).ToArray(),
                StandardUvPilot.Revision,manifest.ProtocolId,directory,hooks,finalCapture:true,captureEachStroke:true),
            manifest.Cases.ToDictionary(c=>c.Sample.Index,c=>c.Paths),manifest.Context,
            image=>{FrontierSessionCapture.RequireBaseline(baseline,image,manifest.Context.Canvas);
                FrontierSessionCapture.RequireBaseline(historical,image,manifest.Context.Canvas);},
            ()=>StandardUvPilot.Verify(directory,manifest,requireCompleteStatus:false));
    }
    internal const string ResearchShapeKey="research_brush_shape_slot";
    // Standard normalisation (DrawingWorkflow.Effective) always resets the brush to Round slot 3. A research
    // pilot that froze another shape (pic5: square slot 4) carries it under its own key; products never set it.
    internal static void ApplyResearchShape(Settings settings)
    {
        int slot=settings.Int(ResearchShapeKey,0);if(slot==0)return;
        if(slot is not (3 or 4))throw new InvalidDataException("Unsupported research brush shape.");
        settings.Set("brush_shape_slot",slot);settings.Set("brush_shape",slot==4?"Square":"Round");
    }
    private StandardEvidenceSeriesRunner.Result CollectEvidenceCases(ScreenRect canvas,string protocolId,string directory,string captures,
        Func<StandardEvidenceSeriesRunner.Hooks,StandardEvidenceSeriesRunner.Result> run,
        IReadOnlyDictionary<int,ScreenPoint[][]>? contourPaths=null,StandardGeometryContext? sourceContext=null,Action<PixelImage>? validateBaseline=null,Action? validateFinalArtifacts=null,
        string requestedSpeed="Rapid")
    {
        ApplyResearchShape(settings);
        probeMode=true;clock.Start();Rgb? selected=null;
        void CheckEvidence()
        {
            CheckProbe();RequireEditorProof();CheckProbe();
            if(!ReleaseProfile.Standard(settings)||settings.Calibration.Rect("canvas")!=canvas||StandardLineMotion.Enabled(settings)
                ||StrokeTiming.Frame(settings)!=.020||settings.Text("speed_profile")!=requestedSpeed)throw new InvalidDataException("Frozen evidence context changed.");
            if(sourceContext is not null&&(Native.ClientOrigin(window)!=sourceContext.ClientOrigin||Native.ClientSize(window)!=sourceContext.ClientSize
                ||Native.DpiOf(window)!=sourceContext.Dpi||(settings.Mode==ColorMode.HexDirect)!=(sourceContext.ColorMode=="HEX Direct")))
                throw new InvalidDataException("Contour source geometry, DPI or colour context changed.");
            if(Native.Down(0x10)||Native.Down(0x11)||Native.Down(0x12)||Native.Down(0x01))
                throw new PreparationInterruptedException("Release mouse buttons and modifier keys before collecting evidence.");
        }
        CheckEvidence();PrepareBrushCalibration(3);CheckEvidence();
        File.WriteAllText(Path.Combine(directory,"live-context.json"),JsonSerializer.Serialize(new
        {
            createdUtc=DateTimeOffset.UtcNow,build=BuildInfo.Full,windowProcessId,windowDpi,windowRect,
            origin=Native.ClientOrigin(window),client=Native.ClientSize(window),protocolId,
            emitter=contourPaths is null?StandardStrokeMotion.DenseRevision:StandardContourMotion.Revision,speed=StandardEvidenceSeries.Speed,productionBridgeSpeed=StandardEvidenceSeries.ProductionSpeed,settings=settings.Data,
            mapping="identity",size=3,shape=settings.Int("brush_shape_slot",3),manualPreconditions="Initial Canvas clean; Interval0.01 Opacity1.00; no modifiers held",
            captures,
            observations="GetCursorPos after every issued packet and existing wait; bridge packets retain all issued points with one actual read; immediate reads may precede transport acknowledgement",
            instrumented=true,performanceQualified=false,physicalQualityCertified=false
        }));
        void Select(Rgb colour)
        {
            CheckEvidence();
            if(settings.Mode==ColorMode.HexDirect)
            {if(!ApplyHex(colour,true))throw new InvalidOperationException("Evidence HEX colour was not confirmed.");}
            else
            {
                var entry=settings.Palette().FirstOrDefault(p=>p.Color==colour&&p.ClickPoint.HasValue)
                    ??throw new InvalidOperationException("Evidence needs exact black and white entries with captured click points.");
                ApplyPalette(entry,alreadyAligned:true,requireAppliedProof:true);
            }
            VerifyCalibrationPaint(colour);CheckEvidence();selected=colour;
        }
        void Draw(EvidenceSeriesCase sample,ScreenLine line,bool reverse,string tracePath)
        {
            CheckEvidence();if(selected is null)throw new InvalidOperationException("Evidence colour was not selected.");
            ScreenPoint[]? path=null;
            if(contourPaths is not null)
            {
                int index=Array.IndexOf(sample.Stages[0].Lines,line);
                if(index<0||!contourPaths.TryGetValue(sample.Index,out var tracks)||index>=tracks.Length)throw new InvalidDataException("Contour track differs from frozen case.");
                path=tracks[index];StandardBandContour.Validate(path,canvas);
                GuardedNativeInput.Path(coordinateGuard,path,CheckBoundary,CoordinateDenied);
            }
            else GuardedNativeInput.Line(coordinateGuard,line,CheckBoundary,CoordinateDenied,deniedWithReason:CoordinateDenied);
            var trace=new EvidenceSeriesTraceInput(motionInput,ReadCursor,sample.Speed.Pitch*sample.Speed.BatchSize);string? problem=null;
            try
            {
                if(path is null)StandardStrokeMotion.Draw(line,settings,sample.Speed,reverse,trace);
                else StandardContourMotion.Draw(path,canvas,settings,sample.Speed,trace);
                CheckProbe();
            }
            catch(Exception error){problem=error.GetType().Name+": "+error.Message;throw;}
            finally
            {
                Native.Release();
                using var file=new FileStream(tracePath,FileMode.CreateNew,FileAccess.Write);
                using var zip=new GZipStream(file,CompressionLevel.Fastest);
                if(path is not null)JsonSerializer.Serialize(zip,new
                {
                    sample=sample.Index,line,reverse,actualPath=path,requested=selected.Value.Hex,problem,pauseReason,
                    trace.Samples,qpcFrequency=Stopwatch.Frequency,coordinateSpace="absolute screen physical pixels",
                    emitter=StandardContourMotion.Revision,speed=sample.Speed,nativeInput=true,
                    continuousCursorPathProven=false,physicalQualityCertified=false,performanceQualified=false
                });
                else JsonSerializer.Serialize(zip,new
                {
                    sample=sample.Index,line,reverse,requested=selected.Value.Hex,problem,pauseReason,
                    trace.Samples,qpcFrequency=Stopwatch.Frequency,coordinateSpace="absolute screen physical pixels",
                    emitter=StandardStrokeMotion.DenseRevision,speed=sample.Speed,
                    cursorScope="one real GetCursorPos after every issued packet and existing wait; IssuedPoints are commands, not intermediate cursor observations",
                    nativeInput=true,continuousCursorPathProven=false,physicalQualityCertified=false,performanceQualified=false
                });
            }
        }
        return run(new(CheckEvidence,()=>StableProbeShot(canvas,(_,_,_)=>{}),Select,Draw,Native.ReleaseChecked,true,validateBaseline,validateFinalArtifacts));
    }
}
