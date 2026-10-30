using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

internal sealed partial class Painter
{
    internal StandardQualityEvidenceRunner.Result DiagnoseStandardQuality(StandardQualityExperiment protocol,QualityLineCase sample,string directory,PixelImage? baseline)
    {
        protocol.Validate();
        if(!protocol.Cases.Contains(sample))throw new InvalidDataException("Unregistered evidence sample.");
        return DiagnoseStandardQuality(protocol.Canvas,sample,directory,StandardQualityExperiment.Revision,false,
            hooks=>StandardQualityEvidenceRunner.Run(protocol,sample,directory,baseline,hooks));
    }
    internal StandardQualityEvidenceRunner.Result DiagnoseStandardQuality(StandardDirectionExperiment protocol,QualityLineCase sample,string directory,PixelImage? baseline)
    {
        protocol.Validate();
        if(!protocol.Cases.Contains(sample))throw new InvalidDataException("Unregistered evidence sample.");
        return DiagnoseStandardQuality(protocol.Canvas,sample,directory,StandardDirectionExperiment.Revision,true,
            hooks=>StandardQualityEvidenceRunner.Run(protocol,sample,directory,baseline,hooks));
    }
    private StandardQualityEvidenceRunner.Result DiagnoseStandardQuality(ScreenRect canvas,QualityLineCase sample,string directory,string revision,bool packetPilot,
        Func<StandardQualityEvidenceRunner.Hooks,StandardQualityEvidenceRunner.Result> run)
    {
        Rgb? requested=null;
        void Validate()
        {
            CheckProbe();
            if(!ReleaseProfile.Standard(settings)||settings.Calibration.Rect("canvas")!=canvas||StandardLineMotion.Enabled(settings)
                ||settings.Text("speed_profile")!=sample.SpeedPreset||StrokeTiming.Frame(settings)!=.020)
                throw new InvalidDataException("Evidence context changed.");
            foreach(var line in sample.Segments)GuardedNativeInput.Line(coordinateGuard,line,CheckBoundary,CoordinateDenied,deniedWithReason:CoordinateDenied);
        }
        void Prepare()
        {
            probeMode=true;clock.Start();Native.ReleaseChecked();RequireEditorProof();Validate();
            PrepareBrushCalibration(3);
            DelayTimer.CaptureGuardCosts=packetPilot;
            var empty=StableProbeShot(canvas,(_,_,_)=>{});
            requested=SelectCalibrationColor(empty.Color(empty.Width*empty.Height/2));
            VerifyCalibrationPaint(requested.Value);RequireEditorProof();Validate();
            File.WriteAllText(Path.Combine(directory,"context.json"),JsonSerializer.Serialize(new
            {createdUtc=DateTimeOffset.UtcNow,build=BuildInfo.Full,windowProcessId,windowDpi,windowRect,origin=Native.ClientOrigin(window),client=Native.ClientSize(window),
                requested,size=3,shape=3,protocol=revision,emitter=StandardStrokeMotion.Revision,settings=settings.Data,sample,
                mapping="identity",manualPrecondition="First Canvas clean; Interval0.01 Opacity1.00; baseline clean comparison thereafter",
                instrumented=true,performanceQualified=false}));
        }
        void Draw(ScreenLine line,int segment)
        {
            Validate();RequireEditorProof();VerifyCalibrationPaint(requested!.Value);RequireEditorProof();
            if(Native.Down(0x10)||Native.Down(0x11)||Native.Down(0x12)||Native.Down(0x01))
                throw new PreparationInterruptedException("Release mouse and modifier keys before the diagnostic.");
            var guardBefore=DelayTimer.GuardCosts;var waitsBefore=DelayTimer.Costs;
            var input=new QualityStepInput(motionInput,ReadCursor,allowPackets:packetPilot);string? error=null;
            try{StandardStrokeMotion.DrawLegacy(line,settings,SpeedProfile.Get(sample.SpeedPreset),sample.Reverse,input);CheckProbe();}
            catch(Exception e){error=e.GetType().Name+": "+e.Message;throw;}
            finally
            {
                Native.Release();
                File.WriteAllText(Path.Combine(directory,$"segment-{segment:D2}-cursor.json"),JsonSerializer.Serialize(new
                    {line,error,input.Samples,coordinateSpace="absolute screen physical pixels",nativeInput=true,
                        packetSizesPreserved=true,onePointPerPacket=sample.SpeedPreset=="Safe",cursorScope=sample.SpeedPreset=="Safe"?"each single command":"packet end only",
                        waitGuardCosts=DelayTimer.GuardCosts-guardBefore,
                        waits=new{Calls=DelayTimer.Costs.Calls-waitsBefore.Calls,RequestedSeconds=DelayTimer.Costs.RequestedSeconds-waitsBefore.RequestedSeconds,
                            ActualSeconds=DelayTimer.Costs.ActualSeconds-waitsBefore.ActualSeconds,includesGuard=true,pureTimerLatency=false},
                        continuousCursorPathProven=false,performanceQualified=false}));
            }
        }
        return run(new(Prepare,()=>StableProbeShot(canvas,(_,_,_)=>{}),Draw,Native.Release));
    }
}

// Legacy Safe uses one cursor read per single command. The direction pilot
// preserves larger packets and explicitly labels its batch-end observations.
internal sealed class QualityStepInput(ICalibratedStrokeInput inner,Func<ScreenPoint> cursor,bool allowPackets=false):ICalibratedStrokeInput
{
    internal sealed record Sample(string Phase,double Seconds,ScreenPoint? Command,ScreenPoint? Cursor,double RequestedWait=0)
    {
        public double StartedSeconds {get;init;}
        public ScreenPoint[] Commands {get;init;}=[];
        public bool CursorReadSucceeded {get;init;}=true;
        public string CursorScope {get;init;}="last command";
    }
    internal List<Sample> Samples {get;}=[];
    public double Seconds=>inner.Seconds;
    private ScreenPoint? command;
    private void Record(string phase,double started,double wait=0,ScreenPoint[]? commands=null)
    {
        if(Samples.Count>=8192)throw new InvalidDataException("Evidence trace limit reached.");
        ScreenPoint actual;
        try{actual=cursor();}
        catch{Samples.Add(new(phase,Seconds,command,null,wait){StartedSeconds=started,Commands=commands??[],CursorReadSucceeded=false});throw;}
        Samples.Add(new(phase,Seconds,command,actual,wait){StartedSeconds=started,Commands=commands??[],CursorScope=commands is {Length:>1}?"packet end only":"last command"});
        if(command is {} expected&&actual!=expected)throw new PreparationInterruptedException(RecoveryMessages.CursorPosition);
    }
    public void Move(IReadOnlyList<ScreenPoint> points)
    {if(points.Count==0||points.Count>16||!allowPackets&&points.Count!=1)throw new InvalidDataException("Evidence packet size is not allowed.");
        double at=Seconds;command=points[^1];inner.Move(points);Record("move",at,commands:points.ToArray());}
    public void Button(bool up){double at=Seconds;inner.Button(up);Record(up?"released":"held",at);}
    public void Wait(double seconds){double at=Seconds;inner.Wait(seconds);Record("wait",at,seconds);}
    public void Shift(bool up){double at=Seconds;inner.Shift(up);Record(up?"shift-up":"shift-down",at);}
}
