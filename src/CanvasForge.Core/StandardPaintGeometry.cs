using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CanvasForge.Core;

// Coordinates are physical screen pixels. No inferred DPI or paint transform.
public sealed record StandardGeometryContext(ScreenRect Canvas,ScreenPoint ClientOrigin,ScreenSize ClientSize,int Dpi,
    int ShapeSlot,double Size,SpeedProfile Speed,double FrameSeconds,double EndHoldSeconds,double ReleaseSeconds,
    string ColorMode,string Emitter=StandardStrokeMotion.DenseRevision,string MappingRevision="identity-v1",string RequestedHex="000000")
{
    public const string Revision="standard-physical-context-v1";
    public string EnvironmentId=>GeometryHash.Of(new{revision=Revision,Canvas,ClientOrigin,ClientSize,Dpi,ShapeSlot,Size,Speed,
        FrameSeconds,EndHoldSeconds,ReleaseSeconds,ColorMode,Emitter,MappingRevision});
    public string Id=>GeometryHash.Of(new{environment=EnvironmentId,RequestedHex});
    public int RequestedColorKey=>Convert.ToInt32(RequestedHex,16);
    public void Validate()
    {
        if(!Canvas.Valid||(long)Canvas.Width*Canvas.Height>16_000_000||ClientSize.Width<=0||ClientSize.Height<=0||Dpi<=0
            ||ShapeSlot is <1 or >7||!BrushFootprints.CalibrationSizes.Contains(Size)||Speed is null||Speed.Pitch is <1 or >3
            ||Speed.BatchSize is <1 or >16||string.IsNullOrWhiteSpace(Speed.Name)||!double.IsFinite(Speed.BrushSize)||Speed.BrushSize<=0
            ||new[]{FrameSeconds,EndHoldSeconds,ReleaseSeconds,Speed.PointDelay,Speed.StartDelay,Speed.UpDelay}.Any(v=>!double.IsFinite(v)||v<0)
            ||FrameSeconds<=0||Canvas.Left<ClientOrigin.X||Canvas.Top<ClientOrigin.Y||(long)Canvas.Right>(long)ClientOrigin.X+ClientSize.Width||(long)Canvas.Bottom>(long)ClientOrigin.Y+ClientSize.Height
            ||Emitter!=StandardStrokeMotion.DenseRevision||MappingRevision!="identity-v1"||ColorMode is not ("Rust Palette" or "HEX Direct")
            ||RequestedHex is not {Length:6}||RequestedHex.Any(c=>!(c is >= '0' and <= '9' or >= 'A' and <= 'F')))
            throw new InvalidDataException("Invalid Standard physical geometry context.");
    }
}

internal static class GeometryHash
{
    internal static readonly string ImplementationSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(GeometryHash).Assembly.Location))).ToLowerInvariant();
    internal static string Of<T>(T value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        CanonicalJson.Serialize(JsonSerializer.SerializeToNode(value))))).ToLowerInvariant();
    internal static BrushSpan[] Mask(IEnumerable<BrushSpan> source)
    {
        var rows=source.ToArray();
        if(rows.Length>100_000||rows.Any(r=>r.Left>=r.Right||Math.Abs((long)r.Y)>32_768||Math.Abs((long)r.Left)>32_768||Math.Abs((long)r.Right)>32_768)
            ||rows.Sum(r=>(long)r.Right-r.Left)>1_000_000)throw new InvalidDataException("Invalid physical mask or mask resource budget exceeded.");
        return BrushSweep.Merge(rows);
    }
    internal static HashSet<ScreenPoint> Points(IEnumerable<BrushSpan> spans)=>BrushFootprints.Points(spans.ToArray()).ToHashSet();
}

// A native difference is observational Possible, never an invented Solid.
// HighContrast is a diagnostic segmentation and carries no planner credit.
public sealed class StandardPaintObservation
{
    public string SourceId{get;}
    public StandardGeometryContext Context{get;}
    public ScreenLine Command{get;}
    public bool Vertical=>Command.X1==Command.X2;
    public bool Reverse=>Command.X2<Command.X1||Command.Y2<Command.Y1;
    public int Length=>TransferSchedule.Length(Command);
    public IReadOnlyList<BrushSpan> Possible{get;}
    public IReadOnlyList<BrushSpan> HighContrast{get;}
    public string BeforeRgbaSha256{get;}
    public string AfterRgbaSha256{get;}
    public int PeakDelta{get;}
    public bool ProductionEligible=>false;
    public string SolidStatus=>"not_measured; high contrast is not certified Solid";
    public string Id=>GeometryHash.Of(new{SourceId,context=Context.Id,Command,Possible,HighContrast,BeforeRgbaSha256,AfterRgbaSha256});
    private StandardPaintObservation(string id,StandardGeometryContext context,ScreenLine command,BrushSpan[] possible,BrushSpan[] contrast,
        string before,string after,int peak)
    {SourceId=id;Context=context;Command=command;Possible=Array.AsReadOnly(possible);HighContrast=Array.AsReadOnly(contrast);BeforeRgbaSha256=before;AfterRgbaSha256=after;PeakDelta=peak;}
    public static StandardPaintObservation Measure(string sourceId,StandardGeometryContext context,ScreenLine command,
        PixelImage before,PixelImage after,ScreenRect reservation)
    {
        context.Validate();var canvas=context.Canvas;
        if(string.IsNullOrWhiteSpace(sourceId)||command.X1!=command.X2&&command.Y1!=command.Y2||TransferSchedule.Length(command)<1
            ||before.Width!=canvas.Width||before.Height!=canvas.Height||after.Width!=before.Width||after.Height!=before.Height
            ||!reservation.Valid||reservation.Left<canvas.Left||reservation.Top<canvas.Top||reservation.Right>canvas.Right||reservation.Bottom>canvas.Bottom
            ||new[]{new ScreenPoint(command.X1,command.Y1),new(command.X2,command.Y2)}.Any(p=>p.X<canvas.Left||p.X>=canvas.Right||p.Y<canvas.Top||p.Y>=canvas.Bottom))
            throw new InvalidDataException("Native geometry differs from frozen command.");
        var possible=new List<ScreenPoint>();var strong=new List<ScreenPoint>();int peak=0;
        for(int y=0;y<before.Height;y++)for(int x=0;x<before.Width;x++)
        {
            int d=RustSlider.Delta(before.Color(y*before.Width+x),after.Color(y*after.Width+x));peak=Math.Max(peak,d);
            if(d<=12)continue;
            int sx=canvas.Left+x,sy=canvas.Top+y;
            if(sx<reservation.Left||sx>=reservation.Right||sy<reservation.Top||sy>=reservation.Bottom)
                throw new InvalidDataException("Native paint or scene change exceeded reservation.");
            var point=new ScreenPoint(sx-command.X1,sy-command.Y1);possible.Add(point);if(d>80)strong.Add(point);
        }
        return new(sourceId,context,command,BrushFootprints.Spans(possible),BrushFootprints.Spans(strong),
            Convert.ToHexString(SHA256.HashData(before.Rgba)).ToLowerInvariant(),Convert.ToHexString(SHA256.HashData(after.Rgba)).ToLowerInvariant(),peak);
    }
}

// Stage masks are absolute local-canvas pixels, including the unstable fringe.
// This reports overwrite risk; it does not turn a changed pixel into coverage.
public sealed record BoundaryLayerResult(int Layer,int Color,int PossiblePixels,int TouchedEarlierPixels,int TouchedOtherColorPixels);
public sealed record BoundaryLayerReplay(IReadOnlyList<BoundaryLayerResult> Layers,int[] LastPossibleOwner)
{public bool PhysicalQualityCertified=>false;}
public static class StandardBoundaryLayers
{
    public static BoundaryLayerReplay Replay(int width,int height,IEnumerable<(int Color,IReadOnlyList<BrushSpan> Possible)> stages)
    {
        if(width<1||height<1||(long)width*height>16_000_000)throw new ArgumentOutOfRangeException(nameof(width));
        var owners=new int[width*height];Array.Fill(owners,-1);var layers=new List<BoundaryLayerResult>();
        foreach(var (color,possible) in stages)
        {
            if(color is <0 or >0xffffff)throw new InvalidDataException("Invalid RGB color key.");
            var rows=GeometryHash.Mask(possible);
            if(rows.Any(r=>r.Y<0||r.Y>=height||r.Left<0||r.Right>width))throw new InvalidDataException("Layer mask outside Canvas.");
            int count=0,touched=0,other=0;
            foreach(var row in rows)for(int x=row.Left;x<row.Right;x++)
            {int i=row.Y*width+x;count++;if(owners[i]>=0){touched++;if(owners[i]!=color)other++;}owners[i]=color;}
            layers.Add(new(layers.Count,color,count,touched,other));
        }
        return new(layers.AsReadOnly(),owners);
    }
}
