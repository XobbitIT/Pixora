using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CanvasForge.Core;

public enum StrokeMethod { Paced, Shift }
public enum SpeedProfileState { NotTested, Current, BrushUnavailable, MeasurementsChanged, SpatialChanged, InvalidProof }
public sealed record SpeedSample(double Size, StrokeMethod Method, bool Vertical, double TestedMs,
    double SafeMs, int StepPx, int MaxLength, int Repeats, double Coverage, string? SpatialId=null);
public sealed record SpeedProbeProfile(string Context, DateTimeOffset Created, List<SpeedSample> Samples);

public static class SpeedCalibration
{
    public const string Revision = "probe-moving-scene-guard-v8";
    public static readonly int[] CandidatesMs = [32,20,12,8];
    public const int Repeats = 3;
    public const int RequiredTiles=2+2*2*4*Repeats+2*2*Repeats;
    public static double Margin(double ms) => Math.Ceiling(ms*1.25+2);
    public static bool BrushReady(Settings s,double size)
    {
        if(!BrushFootprints.Sizes.Contains(size)||s.Mode==ColorMode.HexDirect&&!s.HexControlsReady)return false;
        if(s.Data["brush_footprints"] is not null)return BrushFootprints.Find(s,size) is {SolidCore.Valid:true};
        if(!AdaptiveBrush.CalibrationCurrent(s))return false;
        try{Footprint(s,size);return true;}catch(InvalidOperationException){return false;}
    }
    public static string Context(Settings s)
    {
        var cal=s.Calibration;
        var masks=string.Join(";",BrushFootprints.Read(s).OrderBy(p=>p.Size).Select(p=>p.Id));
        var text=$"{Revision}:{AdaptiveBrush.Context(s)}:{CanonicalJson.Serialize(s.Data["brush_calibration_points"])}:{cal.SessionDpi}:{cal.SessionSize}";
        if(masks.Length>0)text=$"{Revision}:{AdaptiveBrush.Context(s)}:{cal.SessionDpi}:{cal.SessionSize}:{BrushFootprints.Revision}:{masks}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
    public static SpeedProbeProfile? Read(Settings s)
    {
        try
        {
            var profile=(s.Data["shape_speed_profiles"]?[s.Int("brush_shape_slot",3).ToString()]??s.Data["speed_probe_profile"])?.Deserialize<SpeedProbeProfile>();
            if(profile is null||profile.Context!=Context(s)||profile.Samples is null)return null;
            if(profile.Samples.Count>64||profile.Samples.Any(x=>x is null||!double.IsFinite(x.Size)||!BrushFootprints.Sizes.Contains(x.Size)
                ||!BrushReady(s,x.Size)||!Enum.IsDefined(x.Method)||!double.IsFinite(x.TestedMs)||x.TestedMs is <8 or >64
                ||!double.IsFinite(x.SafeMs)||x.SafeMs<Margin(x.TestedMs)||x.SafeMs>100
                ||x.StepPx is <1 or >64||x.StepPx>2*Footprint(s,x.Size).Inner+1
                ||x.MaxLength is <8 or >16384||x.Repeats<Repeats||x.Coverage!=1
                ||ProbeSpatialCalibration.Read(s,x.Size) is not { } spatial||x.SpatialId!=spatial.Id))return null;
            return profile;
        }
        catch(JsonException){return null;}
        catch(InvalidOperationException){return null;}
        catch(FormatException){return null;}
    }
    public static bool Current(Settings s) => Read(s)?.Samples.Count>0;
    public static SpeedProfileState Status(Settings s)
    {
        if(Current(s))return SpeedProfileState.Current;
        try
        {
            var node=s.Data["shape_speed_profiles"]?[s.Int("brush_shape_slot",3).ToString()]??s.Data["speed_probe_profile"];
            if(node is null)return SpeedProfileState.NotTested;
            var p=node.Deserialize<SpeedProbeProfile>();
            if(p?.Samples is null||p.Samples.Count>64||p.Samples.Any(x=>x is null))return SpeedProfileState.InvalidProof;
            if(p.Samples.Any(x=>!BrushReady(s,x.Size)))return SpeedProfileState.BrushUnavailable;
            if(p.Context!=Context(s))return SpeedProfileState.MeasurementsChanged;
            if(p.Samples.Any(x=>ProbeSpatialCalibration.Read(s,x.Size) is not { } spatial||x.SpatialId!=spatial.Id))return SpeedProfileState.SpatialChanged;
            return SpeedProfileState.InvalidProof;
        }
        catch(Exception e) when(e is JsonException or InvalidOperationException or FormatException){return SpeedProfileState.InvalidProof;}
    }
    private static bool Allowed(Settings s) => s.Bool("calibrated_strokes")
        &&s.Text("coverage_mode","Precision")=="Precision"&&s.Bool("force_precision_controls",true)
        &&s.Bool("use_fixed_opacity",true)&&s.Number("paint_opacity_value",1)==1
        &&(s.Int("brush_shape_slot",3) is 3 or 4||BrushFootprints.Read(s).Any(p=>p.SolidCore.Valid));
    public static bool Use(Settings s)=>Allowed(s)&&Current(s);
    public static SpeedSample? Resolve(Settings s,double size,ScreenLine line,int shapeSlot=0)
    {
        if(shapeSlot>0&&shapeSlot!=s.Int("brush_shape_slot",3))
        {
            // Most alternative shapes have no evidence yet. Avoid cloning all
            // measured masks for every estimated operation in that common case.
            if(s.Data["shape_speed_profiles"] is not System.Text.Json.Nodes.JsonObject profiles||profiles[shapeSlot.ToString()] is null)return null;
            s=BrushFootprints.ForShape(s,shapeSlot);
        }
        if(!Allowed(s)||line.X1!=line.X2&&line.Y1!=line.Y2)return null;
        return Select(Read(s),size,line);
    }
    private static SpeedSample? Select(SpeedProbeProfile? profile,double size,ScreenLine line)
    {
        if(profile is null||line.X1!=line.X2&&line.Y1!=line.Y2)return null;
        int length=TransferSchedule.Length(line);bool vertical=line.X1==line.X2&&line.Y1!=line.Y2;
        if(length<8)return null;
        return profile.Samples.Where(x=>x.Size==size&&x.Vertical==vertical)
            .OrderBy(x=>CalibratedMotion.Estimate(line,x)).FirstOrDefault();
    }
    // Only estimates use this frozen copy. Live input still validates current
    // measurements through Resolve, so moving Rust cannot reuse cached proof.
    internal static Func<double,ScreenLine,int,SpeedSample?> CreateEstimateResolver(Settings source)
    {
        var snapshot=BrushFootprints.Snapshot(source.Clone());
        int selected=snapshot.Int("brush_shape_slot",3);
        var profiles=new Dictionary<int,SpeedProbeProfile?>();
        return (size,line,shape)=>
        {
            if(line.X1!=line.X2&&line.Y1!=line.Y2||TransferSchedule.Length(line)<8)return null;
            int slot=shape>0?shape:selected;
            if(!profiles.TryGetValue(slot,out var profile))
            {
                var config=BrushFootprints.ForShape(snapshot,slot);
                profile=Allowed(config)?Read(config):null;
                profiles[slot]=profile;
            }
            return Select(profile,size,line);
        };
    }
    public static (int Outer,int Inner) Footprint(Settings s,double size)
    {
        if(BrushFootprints.Find(s,size) is { } p)
        {
            int inner=0;var solid=BrushFootprints.Points(p.Solid).ToHashSet();
            if(solid.Contains(new(0,0)))
                for(int r=1;r<=p.Reach;r++)
                {
                    bool full=true;for(int k=-r;k<=r;k++)full&=solid.Contains(new(k,-r))&&solid.Contains(new(k,r))&&solid.Contains(new(-r,k))&&solid.Contains(new(r,k));
                    if(!full)break;inner=r;
                }
            return (p.Reach,inner);
        }
        foreach(var point in (s.Data["brush_calibration_points"] as System.Text.Json.Nodes.JsonArray??[]).OfType<System.Text.Json.Nodes.JsonArray>())
            if(AdaptiveBrush.TryLegacySample(point,out var value,out var outer,out var inner)&&value==size)
            {
                return ((int)Math.Ceiling(outer/2)+2,Math.Max(0,(int)Math.Floor((inner-1)/2)-1));
            }
        throw new InvalidOperationException("Немає вимірювання для цього Size. Повтори калібрування пензля.");
    }
    public static int PhysicalReach(Settings s,double size)
    {
        if(BrushFootprints.Find(s,size) is { } p)return p.Reach;
        foreach(var row in (s.Data["brush_calibration_points"] as System.Text.Json.Nodes.JsonArray??[]).OfType<System.Text.Json.Nodes.JsonArray>())
            if(AdaptiveBrush.TryLegacySample(row,out var value,out var diameter,out _)&&value==size)
                return (int)Math.Floor((diameter-1)/2);
        throw new InvalidOperationException("No physical brush measurement for this Size.");
    }
    public static List<SpeedProbeTile> Tiles(ScreenRect canvas,int outer,IReadOnlyList<ScreenRect>? excluded=null)
    {
        if(!canvas.Valid||outer is <0 or >512)throw new ArgumentException("Invalid probe Canvas or footprint.");
        const int needed=RequiredTiles;
        List<ScreenRect> Areas(int side)
        {
            int cols=canvas.Width/side,rows=canvas.Height/side;var available=new List<ScreenRect>();
            for(int i=0;i<cols*rows;i++)
            {
                int x=canvas.Left+i%cols*side,y=canvas.Top+i/cols*side;
                var area=new ScreenRect(x,y,x+side,y+side);
                if(excluded is null||!excluded.Any(r=>area.Left<r.Right&&area.Right>r.Left&&area.Top<r.Bottom&&area.Bottom>r.Top))available.Add(area);
            }
            return available;
        }
        // Collinear pairs share a perpendicular coordinate, avoiding a reference
        // taken from a different texture sampling row/column. Prefer 31 px spans;
        // use shorter fully checked spans when clean space is limited. Shift may
        // only execute the actual tested span (MaxLength), never extrapolate it.
        int tile=Math.Max(64,4*outer+80);var areas=Areas(tile);
        if(areas.Count<needed){tile=Math.Max(64,4*outer+48);areas=Areas(tile);}
        if(areas.Count<needed)throw new InvalidOperationException($"Для цього Size потрібно {needed} чистих ділянок {tile}×{tile} px. Збільш Canvas або вибери менший Size.");
        var result=new List<SpeedProbeTile>();
        for(int i=0;i<Math.Min(areas.Count,needed+ProbeControlRetry.MaximumRetries);i++)
        {
            int x=areas[i].Left,y=areas[i].Top,edge=outer+6;
            int separation=2*outer+4,length=(tile-1-2*edge-separation)/2,far=edge+length+separation,center=tile/2;
            result.Add(new(new(x,y,x+tile,y+tile),new(x+far,y+center,x+far+length,y+center),
                new(x+center,y+far,x+center,y+far+length),new(x+edge,y+center,x+edge+length,y+center),
                new(x+center,y+edge,x+center,y+edge+length)));
        }
        return result;
    }
}

// Independent collinear slow and fast spans share their perpendicular coordinate
// but never a physical brush envelope. The slow reference is frozen first.
public sealed record SpeedProbeTile(ScreenRect Area,ScreenLine Horizontal,ScreenLine Vertical,
    ScreenLine ControlHorizontal,ScreenLine ControlVertical);

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
