namespace CanvasForge.Core;

public sealed record MotionTransportRate(long Observations,double SecondsPerPoint,double OtherSecondsPerStroke)
{
    public bool Valid=>Observations>=20&&double.IsFinite(SecondsPerPoint)&&SecondsPerPoint is >=0 and <=.01
        &&double.IsFinite(OtherSecondsPerStroke)&&OtherSecondsPerStroke is >=0 and <=.1;
}

// Uses the same emitter with a recording clock. Counts exactly the packets and
// points the emitter sends; neither path spacing nor input waits are changed.
public static class MotionTransportTiming
{
    private sealed class Clock(MotionTransportRate? costs):ICalibratedStrokeInput
    {
        public double Seconds{get;private set;}
        public void Move(IReadOnlyList<ScreenPoint> points)=>Seconds+=(costs?.SecondsPerPoint??0)*points.Count;
        public void Button(bool up){}
        public void Shift(bool up){}
        public void Wait(double seconds)=>Seconds+=seconds;
    }
    public static double Standard(Settings s,SpeedProfile speed,ScreenLine line,MotionTransportRate? rate=null)
    {
        var input=new Clock(rate is {Valid:true}?rate:null);
        StandardStrokeMotion.Draw(line,s,speed,false,input);
        return input.Seconds+(rate is {Valid:true}?rate.OtherSecondsPerStroke:0);
    }
    public static string Context(Settings s)=>$"sendinput-v3:{s.Text("input_engine","Stable")}:{StrokeTiming.Frame(s):R}:{s.Text("speed_profile","Rapid")}"
        +(StandardStrokeMotion.Emitter(s)!=StandardStrokeMotion.Revision?$":{StandardStrokeMotion.Emitter(s)}":"");
}
