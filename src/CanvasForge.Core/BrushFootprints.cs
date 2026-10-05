using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.CompilerServices;

namespace CanvasForge.Core;

// Inclusive Left, exclusive Right; coordinates are relative to SendInput, never
// relative to a fitted visual centroid. Bounds and physical reach are separate.
public readonly record struct BrushSpan(int Y, int Left, int Right);
public sealed record BrushFootprint(string Id, string Context, int ShapeSlot, double Size,
    int Repeats, BrushSpan[] Possible, BrushSpan[] Solid, ScreenRect SafetyBounds, ScreenRect SolidCore)
{
    public int Reach => Math.Max(Math.Max(Math.Abs(SafetyBounds.Left), Math.Abs(SafetyBounds.Right-1)),
        Math.Max(Math.Abs(SafetyBounds.Top), Math.Abs(SafetyBounds.Bottom-1)));
    public int SolidPixels => Solid.Sum(x=>x.Right-x.Left);
}
public sealed record BrushStamp(BrushSpan[] Possible, BrushSpan[] Solid, Rgb Reference);
public readonly record struct BrushCalibrationTile(double Size, int Repeat, ScreenRect Area, ScreenPoint Command);

public static class BrushFootprints
{
    public const string Revision = "command-masks-v1";
    public static readonly double[] Sizes = [1,3,10,20,40,60,100];
    public const int Repeats = 3;
    private const int Limit = 256;
    private sealed record CachedRows(List<BrushFootprint> Rows);
    // Settings snapshots used by the planner/Painter are immutable. Set replaces
    // this JSON node, so changed calibration gets a new cache entry.
    private static readonly ConditionalWeakTable<JsonNode,CachedRows> cache = new();
    private static readonly ConditionalWeakTable<Settings,Dictionary<int,Settings>> shapes = new();
    // Only private execution snapshots cache parsed masks. Editable UI/import
    // settings always revalidate, including in-place JSON edits.
    public static Settings Snapshot(Settings source)
    {
        if(source.MeasurementSnapshot)return source;
        var clone=source.Clone();clone.MeasurementSnapshot=true;return clone;
    }

    public static string Context(Settings s,int shape)
    {
        var cal=s.PaintCalibration();
        string text=$"{Revision}:{s.Mode}:{shape}:{s.Calibration.Rect("canvas")}:{cal.SessionClient}:{cal.SessionSize}:{cal.SessionDpi}:{cal.Rect("size_track")}:{cal.Rect("brush_shapes")}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
    public static Settings ForShape(Settings source,int shape)
    {
        if(shape==source.Int("brush_shape_slot",3))return source;
        if(source.MeasurementSnapshot)
        {
            var configs=shapes.GetOrCreateValue(source);
            lock(configs){if(!configs.TryGetValue(shape,out var found))configs[shape]=found=CreateShape(source,shape);return found;}
        }
        return CreateShape(source,shape);
    }
    private static Settings CreateShape(Settings source,int shape)
    {
        var s=source.Clone();s.Set("brush_shape_slot",shape);s.Set("brush_shape",shape==4?"Square":shape==3?"Round":$"Shape {shape}");
        // Never inherit another shape's radii or speed proof. Its measured masks
        // are looked up by context; legacy rows belong to only the selected shape.
        s.Data.Remove("brush_calibration_points");s.Data.Remove("brush_calibration_context");
        s.MeasurementSnapshot=source.MeasurementSnapshot;
        return s;
    }
    public static List<BrushFootprint> Read(Settings s,bool allShapes=false)
    {
        try
        {
            var node=s.Data["brush_footprints"];if(node is null)return [];
            CachedRows Parse(JsonNode n)
            {
                var parsed=n.Deserialize<List<BrushFootprint>>();
                if(parsed is null||parsed.Count>49||parsed.Any(p=>p is null)
                    ||parsed.GroupBy(p=>(p.ShapeSlot,p.Size)).Any(g=>g.Count()!=1))return new([]);
                return new(parsed.Where(Valid).ToList());
            }
            var rows=(s.MeasurementSnapshot?cache.GetValue(node,Parse):Parse(node)).Rows;
            if(rows is null||rows.Count>49||rows.Any(p=>p is null))return [];
            if(rows.GroupBy(p=>(p.ShapeSlot,p.Size)).Any(g=>g.Count()!=1))return [];
            return rows.Where(p=>p.Context==Context(s,p.ShapeSlot)
                &&(allShapes||p.ShapeSlot==s.Int("brush_shape_slot",3))).ToList();
        }
        catch(Exception e) when(e is JsonException or InvalidOperationException or FormatException or OverflowException){return [];}
    }
    public static BrushFootprint? Find(Settings s,double size,int? shape=null)
        =>Read(s,true).FirstOrDefault(p=>p.Size==size&&p.ShapeSlot==(shape??s.Int("brush_shape_slot",3)));
    public static void Save(Settings s,IReadOnlyList<BrushFootprint> profiles)
    {
        if(profiles.Count==0||profiles.Any(p=>!Valid(p)||p.Context!=Context(s,p.ShapeSlot))
            ||profiles.GroupBy(p=>(p.ShapeSlot,p.Size)).Any(g=>g.Count()!=1))throw new InvalidDataException("Invalid brush profiles.");
        var kept=Read(s,true).Where(p=>!profiles.Any(n=>n.ShapeSlot==p.ShapeSlot&&n.Size==p.Size)).ToList();
        kept.AddRange(profiles);s.Set("brush_footprints",kept.OrderBy(p=>p.ShapeSlot).ThenBy(p=>p.Size));
    }
    private static bool SpansValid(BrushSpan[]? spans,bool empty=false)
    {
        if(spans is null||spans.Length>263169||(!empty&&spans.Length==0))return false;
        int y=int.MinValue,right=int.MinValue,total=0;
        foreach(var p in spans)
        {
            if(p.Y is <-Limit or >Limit||p.Left < -Limit||p.Right>Limit+1||p.Right<=p.Left
                ||p.Y<y||p.Y==y&&p.Left<right)return false;
            y=p.Y;right=p.Right;total+=p.Right-p.Left;
        }
        return total<=263169;
    }
    public static bool Valid(BrushFootprint p)
    {
        if(p.ShapeSlot is <1 or >7||!Sizes.Contains(p.Size)||p.Repeats!=Repeats||string.IsNullOrEmpty(p.Context)
            ||!SpansValid(p.Possible)||!SpansValid(p.Solid,true))return false;
        if(p.Reach>Math.Min(Limit,(int)p.Size*2+12))return false;
        var possible=Points(p.Possible).ToHashSet();
        if(Points(p.Solid).Any(x=>!possible.Contains(x))||Bounds(p.Possible)!=p.SafetyBounds||Core(p.Solid)!=p.SolidCore)return false;
        return p.Id==Identity(p with{Id=""});
    }
    private static string Identity(BrushFootprint p)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(p with{Id=""}))));
    public static IEnumerable<ScreenPoint> Points(IEnumerable<BrushSpan> spans)
    {foreach(var row in spans)for(int x=row.Left;x<row.Right;x++)yield return new(x,row.Y);}
    public static BrushSpan[] Spans(IEnumerable<ScreenPoint> points)
    {
        var result=new List<BrushSpan>();
        foreach(var row in points.Distinct().GroupBy(p=>p.Y).OrderBy(g=>g.Key))
        {
            int begin=int.MinValue,end=int.MinValue;
            foreach(int x in row.Select(p=>p.X).Order())
            {if(x!=end){if(begin!=int.MinValue)result.Add(new(row.Key,begin,end));begin=x;}end=x+1;}
            if(begin!=int.MinValue)result.Add(new(row.Key,begin,end));
        }
        return result.ToArray();
    }
    public static ScreenRect Bounds(BrushSpan[] rows)=>rows.Length==0?default:
        new(rows.Min(p=>p.Left),rows.Min(p=>p.Y),rows.Max(p=>p.Right),rows.Max(p=>p.Y)+1);
    // Largest completely solid axis-aligned rectangle; its offset is retained.
    public static ScreenRect Core(BrushSpan[] rows)
    {
        var b=Bounds(rows);if(!b.Valid)return default;
        var heights=new int[b.Width];var solid=Points(rows).ToHashSet();ScreenRect best=default;
        for(int y=b.Top;y<b.Bottom;y++)
        {
            for(int x=0;x<b.Width;x++)heights[x]=solid.Contains(new(x+b.Left,y))?heights[x]+1:0;
            var stack=new Stack<int>();
            for(int x=0;x<=b.Width;x++)
            {
                int height=x==b.Width?0:heights[x];
                while(stack.Count>0&&heights[stack.Peek()]>height)
                {
                    int h=heights[stack.Pop()],left=stack.Count==0?0:stack.Peek()+1;
                    if((x-left)*h>best.Width*best.Height)best=new(b.Left+left,y-h+1,b.Left+x,y+1);
                }
                stack.Push(x);
            }
        }
        return best;
    }
    public static BrushStamp Measure(PixelImage before,PixelImage after,ScreenPoint command,double size,Rgb? frozen=null)
    {
        if(before.Width!=after.Width||before.Height!=after.Height||!Sizes.Contains(size)
            ||command.X<0||command.Y<0||command.X>=before.Width||command.Y>=before.Height)
            throw new ArgumentException("Invalid brush capture.");
        int extent=Math.Min(Limit,(int)Math.Ceiling(size*2)+12);
        var changed=new List<ScreenPoint>();var colors=new List<(Rgb Color,int Delta)>();
        for(int y=0;y<before.Height;y++)for(int x=0;x<before.Width;x++)
        {
            int i=y*before.Width+x,delta=RustSlider.Delta(before.Color(i),after.Color(i));
            if(delta<=12)continue;
            int dx=x-command.X,dy=y-command.Y;
            if(x<2||y<2||x>=before.Width-2||y>=before.Height-2||Math.Abs(dx)>extent||Math.Abs(dy)>extent)
                throw new InvalidOperationException("Brush capture is clipped or the scene changed. Clear Canvas and retry.");
            changed.Add(new(dx,dy));colors.Add((after.Color(i),delta));
        }
        if(changed.Count==0||colors.Max(p=>p.Delta)<80)throw new InvalidOperationException("Brush dot has insufficient contrast. Clear Canvas and retry.");
        // The strongest changed pixels establish the opaque reference once.
        // Every repeat is compared against this frozen color, including textures.
        var top=colors.OrderByDescending(p=>p.Delta).Take(Math.Max(1,colors.Count/10)).Select(p=>p.Color).ToArray();
        var reference=frozen??new Rgb(top.Select(p=>p.R).Order().ElementAt(top.Length/2),
            top.Select(p=>p.G).Order().ElementAt(top.Length/2),top.Select(p=>p.B).Order().ElementAt(top.Length/2));
        var solid=changed.Where(p=>RustSlider.Delta(after.Color((command.Y+p.Y)*after.Width+command.X+p.X),reference)<=12);
        return new(Spans(changed),Spans(solid),reference);
    }
    public static BrushFootprint Build(Settings s,int shape,double size,IReadOnlyList<BrushStamp> stamps)
    {
        if(stamps.Count!=Repeats||!Sizes.Contains(size)||shape is <1 or >7
            ||stamps.Any(p=>!SpansValid(p.Possible)||!SpansValid(p.Solid,true)||RustSlider.Delta(p.Reference,stamps[0].Reference)>12))
            throw new InvalidDataException("Three consistent brush measurements are required.");
        var possible=Spans(stamps.SelectMany(p=>Points(p.Possible)));
        var core=Points(stamps[0].Solid).ToHashSet();
        foreach(var stamp in stamps.Skip(1))core.IntersectWith(Points(stamp.Solid));
        var solid=Spans(core);
        var profile=new BrushFootprint("",Context(s,shape),shape,size,Repeats,possible,solid,Bounds(possible),Core(solid));
        profile=profile with{Id=Identity(profile)};
        if(!Valid(profile))throw new InvalidDataException("Invalid measured footprint.");
        return profile;
    }
    public static List<BrushCalibrationTile> Tiles(ScreenRect canvas,IReadOnlyList<double> sizes)
    {
        if(!canvas.Valid||sizes.Count==0||sizes.Count>7||sizes.Any(s=>!Sizes.Contains(s))||sizes.Distinct().Count()!=sizes.Count)
            throw new ArgumentException("Invalid calibration layout.");
        int tile=Math.Max(96,(int)sizes.Max()*4+48),cols=Math.Min(canvas.Width/tile,Math.Max(2,sizes.Count)),rows=canvas.Height/tile;
        int needed=sizes.Count*Repeats;
        if(cols<1||rows<1||cols*rows<needed)throw new InvalidOperationException($"Calibration needs {needed} clean areas of {tile} × {tile} px. Use a larger Canvas or calibrate one Size.");
        rows=(needed+cols-1)/cols;
        var result=new List<BrushCalibrationTile>();
        for(int repeat=0;repeat<Repeats;repeat++)for(int j=0;j<sizes.Count;j++)
        {
            int i=repeat*sizes.Count+j,x=canvas.Left+(i%cols)*canvas.Width/cols,y=canvas.Top+(i/cols)*canvas.Height/rows;
            var area=new ScreenRect(x,y,x+tile,y+tile);
            result.Add(new(sizes[j],repeat,area,area.Center));
        }
        return result;
    }
    public static bool Safe(BrushFootprint profile,int x,int y,int width,int height,Func<int,int,bool> expected)
    {
        var b=profile.SafetyBounds;
        if(x+b.Left<0||x+b.Right>width||y+b.Top<0||y+b.Bottom>height)return false;
        foreach(var row in profile.Possible)for(int px=row.Left;px<row.Right;px++)if(!expected(x+px,y+row.Y))return false;
        return true;
    }
}
