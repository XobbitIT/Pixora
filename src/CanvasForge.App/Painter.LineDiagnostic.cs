using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

internal sealed partial class Painter
{
    public StandardLineDiagnosticRunner.Result DiagnoseStandardLines(StandardLineDiagnosticPlan plan,string directory)
    {
        Rgb? requested=null;
        void Validate()
        {
            CheckProbe();
            if(!ReleaseProfile.Standard(settings)||settings.Calibration.Rect("canvas")!=plan.Canvas)
                throw new InvalidOperationException("Diagnostic Canvas or Standard context changed.");
            plan.Validate(coordinateGuard);
            foreach(var line in plan.Lines)
                GuardedNativeInput.Line(coordinateGuard,line,CheckBoundary,CoordinateDenied,deniedWithReason:CoordinateDenied);
        }
        void Prepare()
        {
            probeMode=true;clock.Start();Native.ReleaseChecked();CheckProbe();RequireEditorProof();Validate();
            // Source proof is mandatory for this experiment, even when ordinary
            // Standard is allowed to draw with only a verified palette cell.
            PrepareBrushCalibration(3);
            var background=StableProbeShot(plan.Area,(_,_,_)=>{});
            requested=SelectCalibrationColor(background.Color(background.Width*background.Height/2));
            VerifyCalibrationPaint(requested.Value);RequireEditorProof();Validate();
            File.WriteAllText(Path.Combine(directory,"context.json"),JsonSerializer.Serialize(new
            {
                build=BuildInfo.Full,windowProcessId,windowDpi,windowRect,client=Native.ClientSize(window),origin=Native.ClientOrigin(window),
                requested,shape=3,size=3,settings=settings.Data,emitter=StandardLineMotion.Revision,
                effectiveFrameMs=plan.FrameMs,cleanCanvas="Manual precondition; not automatically certified",
                manualPrecondition="Interval 0.01 / Opacity 1.00; not read or written by Pixora",
                physicalResultVerified=false
            },new JsonSerializerOptions{WriteIndented=true}));
        }
        void Draw(int index)
        {
            RequireEditorProof();VerifyCalibrationPaint(requested??throw new InvalidOperationException("Diagnostic source is missing."));
            RequireEditorProof();Validate();
            // No user-held modifier is allowed to change the baseline or the
            // explicit key order. Do not disable Sticky Keys or alter the OS.
            if(Native.Down(0x10)||Native.Down(0x11)||Native.Down(0x12)||Native.Down(0x01))
                throw new PreparationInterruptedException("Release mouse and modifier keys before the diagnostic.");
            var trace=new StrokeTraceInput(motionInput,ReadCursor,2048);string? failure=null;
            try{plan.Draw(index,settings,trace);CheckProbe();}
            catch(Exception error){failure=error.GetType().Name+": "+error.Message;throw;}
            finally
            {
                Native.Release();
                // Retain partial traces too. IO never occurs while input is held.
                File.WriteAllText(Path.Combine(directory,$"line-{index+1}-input.json"),JsonSerializer.Serialize(new
                {index,line=plan.Lines[index],reverse=index==1,plan.Route,plan.FrameMs,requested,failure,trace.Truncated,trace.Samples,
                    observedGameFps=(double?)null,physicalResultVerified=false},new JsonSerializerOptions{WriteIndented=true}));
            }
        }
        var result=StandardLineDiagnosticRunner.Run(plan,directory,new(Prepare,()=>StableProbeShot(plan.Area,(_,_,_)=>{}),Draw,Native.Release));
        Log("standard_line_diagnostic",new{directory,result,plan.Route,plan.FrameMs,automaticRetry=false});
        return result;
    }
}
