using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CanvasForge.Core;

public enum StrokeMethod { Paced, Shift }
public sealed record SpeedSample(double Size, StrokeMethod Method, bool Vertical, double TestedMs,
    double SafeMs, int StepPx, int MaxLength, int Repeats, double Coverage);
public sealed record SpeedProbeProfile(string Context, DateTimeOffset Created, List<SpeedSample> Samples);

public static class SpeedCalibration
{
    public const string Revision = "probe-paced-shift-v1";
    public static readonly int[] CandidatesMs = [32,20,12,8];
    public const int Repeats = 3;
    public static double Margin(double ms) => Math.Ceiling(ms*1.25+2);
    public static string Context(Settings s)
    {
        var cal=s.Calibration;
        var text=$"{Revision}:{AdaptiveBrush.Context(s)}:{s.Data["brush_calibration_points"]}:{cal.SessionDpi}:{cal.SessionSize}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
    public static SpeedProbeProfile? Read(Settings s)
    {
        try
        {
            var profile=s.Data["speed_probe_profile"]?.Deserialize<SpeedProbeProfile>();
            if(profile is null||profile.Context!=Context(s)||profile.Samples is null||!AdaptiveBrush.CalibrationCurrent(s))return null;
            if(profile.Samples.Count>64||profile.Samples.Any(x=>x is null||!double.IsFinite(x.Size)||x.Size is <1 or >20
                ||!Enum.IsDefined(x.Method)||!double.IsFinite(x.TestedMs)||x.TestedMs is <8 or >64
                ||!double.IsFinite(x.SafeMs)||x.SafeMs<Margin(x.TestedMs)||x.SafeMs>100
                ||x.StepPx is <1 or >64||x.StepPx>2*Footprint(s,x.Size).Inner+1
                ||x.MaxLength is <8 or >16384||x.Repeats<Repeats||x.Coverage!=1))return null;
            return profile;
        }
        catch(JsonException){return null;}
        catch(InvalidOperationException){return null;}
        catch(FormatException){return null;}
    }
    public static bool Current(Settings s) => Read(s)?.Samples.Count>0;
    public static bool Use(Settings s) => s.Bool("calibrated_strokes")&&Current(s)
        &&s.Text("coverage_mode","Precision")=="Precision"&&s.Bool("force_precision_controls",true)
        &&s.Bool("use_fixed_opacity",true)&&s.Number("paint_opacity_value",1)==1&&s.Int("brush_shape_slot",3) is 3 or 4;
    public static SpeedSample? Resolve(Settings s,double size,ScreenLine line)
    {
        if(!Use(s)||line.X1!=line.X2&&line.Y1!=line.Y2)return null;
        int length=TransferSchedule.Length(line);bool vertical=line.X1==line.X2&&line.Y1!=line.Y2;
        if(length<8)return null;
        return Read(s)!.Samples.Where(x=>x.Size==size&&x.Vertical==vertical)
            .OrderBy(x=>CalibratedMotion.Estimate(line,x)).FirstOrDefault();
    }
    public static (int Outer,int Inner) Footprint(Settings s,double size)
    {
        foreach(var point in (s.Data["brush_calibration_points"] as System.Text.Json.Nodes.JsonArray??[]).OfType<System.Text.Json.Nodes.JsonArray>())
            if(point.Count>=3&&double.TryParse(point[0]?.ToString(),out var value)&&value==size)
            {
                double outer=point[1]!.GetValue<double>(),inner=point[2]!.GetValue<double>();
                if(!double.IsFinite(outer)||!double.IsFinite(inner)||outer is <1 or >512||inner<1||inner>outer)
                    throw new InvalidOperationException("Некоректне вимірювання пензля. Повтори калібрування.");
                return ((int)Math.Ceiling(outer/2)+2,Math.Max(0,(int)Math.Floor((inner-1)/2)-1));
            }
        throw new InvalidOperationException("Немає вимірювання для цього Size. Повтори калібрування пензля.");
    }
    public static List<(ScreenRect Area,ScreenLine Horizontal,ScreenLine Vertical)> Tiles(ScreenRect canvas,int outer)
    {
        int tile=Math.Max(64,2*outer+48),cols=canvas.Width/tile,rows=canvas.Height/tile;
        const int needed=2+2*2*4*Repeats+2*2*Repeats;
        if(cols*rows<needed)throw new InvalidOperationException($"Для цього Size потрібно {needed} чистих ділянок {tile}×{tile} px. Збільш Canvas або вибери менший Size.");
        var result=new List<(ScreenRect,ScreenLine,ScreenLine)>();
        for(int i=0;i<needed;i++)
        {
            int x=canvas.Left+i%cols*tile,y=canvas.Top+i/cols*tile,edge=outer+6;
            result.Add((new(x,y,x+tile,y+tile),new(x+edge,y+tile/2,x+tile-edge-1,y+tile/2),new(x+tile/2,y+edge,x+tile/2,y+tile-edge-1)));
        }
        return result;
    }
}

public interface ICalibratedStrokeInput : IStrokeInput { void Shift(bool up); }
public static class CalibratedMotion
{
    public static double Estimate(ScreenLine line,SpeedSample sample)
    {
        int n=TransferSchedule.Length(line),parts=Math.Max(1,(n+sample.MaxLength-1)/sample.MaxLength);
        if(sample.Method==StrokeMethod.Shift)return parts*7*sample.SafeMs/1000;
        return (4+Math.Ceiling(n/(double)sample.StepPx))*sample.SafeMs/1000;
    }
    public static void Draw(ScreenLine line,SpeedSample sample,ICalibratedStrokeInput input)
    {
        if(line.X1!=line.X2&&line.Y1!=line.Y2)throw new ArgumentException("Expected an axis-aligned stroke.");
        if(!Enum.IsDefined(sample.Method)||sample.StepPx<1||sample.MaxLength<1||!double.IsFinite(sample.SafeMs)||sample.SafeMs<=0)throw new ArgumentException("Invalid motion timing.");
        int length=TransferSchedule.Length(line),dx=Math.Sign(line.X2-line.X1),dy=Math.Sign(line.Y2-line.Y1);
        double wait=sample.SafeMs/1000;
        if(sample.Method==StrokeMethod.Paced)
        {
            input.Move([new(line.X1,line.Y1)]);input.Wait(wait);
            try
            {
                input.Button(false);input.Wait(wait);
                for(int k=sample.StepPx;k<length+sample.StepPx;k+=sample.StepPx)
                {int step=Math.Min(k,length);input.Move([new(line.X1+step*dx,line.Y1+step*dy)]);input.Wait(wait);}
                input.Wait(wait);
            }
            finally{input.Button(true);}
            input.Wait(wait);return;
        }
        // Split longer lines at the span actually tested; never extrapolate a warp length.
        for(int start=0;start<Math.Max(1,length);start+=sample.MaxLength)
        {
            int end=Math.Min(length,start+sample.MaxLength);
            input.Move([new(line.X1+start*dx,line.Y1+start*dy)]);input.Wait(wait);
            try
            {
                input.Shift(false);input.Wait(wait);
                input.Button(false);input.Wait(wait);
                input.Move([new(line.X1+end*dx,line.Y1+end*dy)]);input.Wait(wait);
                input.Wait(wait);
            }
            finally{try{input.Button(true);}finally{input.Shift(true);}}
            input.Wait(2*wait);
        }
    }
}
