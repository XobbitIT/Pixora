using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

internal sealed partial class Painter
{
    private long emittedMovementPoints,timingObservations,timingPoints;
    private double timingSendSeconds,timingOtherSeconds;
    internal static void LoadTransportTiming(Settings settings,string folder)
    {
        // A rate carried by an imported/previous settings snapshot is not proof
        // of this context. Only the matching keyed history may supply it.
        settings.Data.Remove("motion_transport_rate");
        try
        {
            string path=Path.Combine(folder,"timing-v3.json");
            if(!File.Exists(path)||new FileInfo(path).Length>16384)return;
            var rates=JsonSerializer.Deserialize<Dictionary<string,MotionTransportRate>>(File.ReadAllText(path));
            if(rates?.TryGetValue(MotionTransportTiming.Context(settings),out var rate)==true&&rate.Valid)
                settings.Set("motion_transport_rate",rate);
        }
        catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException){}
    }
    private void ObserveTransport(InputCosts costs,long points,double actual)
    {
        if(points<=0||actual<=0||settings.Bool("diagnostic_debug"))return;
        timingObservations++;timingPoints+=points;timingSendSeconds+=costs.SendInputSeconds;
        // Wait overrun and non-move processing are kept separate from SendInput.
        timingOtherSeconds+=Math.Max(0,actual-costs.RequestedWaitSeconds-costs.SendInputSeconds);
    }
    private void SaveTransportTiming()
    {
        if(timingObservations<20||timingPoints==0)return;
        var rate=new MotionTransportRate(timingObservations,timingSendSeconds/timingPoints,timingOtherSeconds/timingObservations);
        if(!rate.Valid)return;
        string path=Path.Combine(Path.GetDirectoryName(logPath)!,"timing-v3.json");
        try
        {
            Dictionary<string,MotionTransportRate> rates=[];
            if(File.Exists(path)&&new FileInfo(path).Length<=16384)
                rates=JsonSerializer.Deserialize<Dictionary<string,MotionTransportRate>>(File.ReadAllText(path))??[];
            rates[MotionTransportTiming.Context(settings)]=rate;
            File.WriteAllText(path+".tmp",JsonSerializer.Serialize(rates));File.Move(path+".tmp",path,true);
            Log("motion_transport_timing",new{context=MotionTransportTiming.Context(settings),rate,source="completed strokes only"});
        }
        catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException){Log("timing_save_unavailable",new{reason=e.Message});}
    }
}
