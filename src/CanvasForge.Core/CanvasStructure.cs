using System.Security.Cryptography;
using System.Text.Json;

namespace CanvasForge.Core;

public sealed record CanvasPatch(ScreenRect Rect,byte[] Rgb,int Side=-1);
public sealed record CanvasStructureReference(int Revision,ScreenRect Canvas,int Dpi,ScreenSize Client,
    string FrameSha256,DateTimeOffset CapturedUtc,CanvasPatch[] Patches)
{
    public int SelectorRevision { get;init; }
    public ScreenRect[] Exclusions { get;init; }=[];
    public CanvasSideAvailability[] Availability { get;init; }=[];
    public CanvasBoundsReference? Bounds { get;init; }
}
public sealed record CanvasStructureResult(bool Passed,string Reason,int Dx,int Dy,double Similarity);

// Session-specific exterior strips, never pixels inside the paintable Canvas.
// Registration searches for evidence; it never changes the command geometry.
public static class CanvasStructure
{
    public const int Revision=2,SearchRadius=3;
    // Existing capture envelope for ordinary Size3: 2*3+12. Templates must
    // survive the program's own edge strokes, not watch their possible fringe.
    public const int PaintExclusionMargin=18;
    public const double MinimumCorrelation=.985,MinimumPeakMargin=.001,MaximumChromaChange=.05;
    public static CanvasStructureReference? Reference(Settings s)
    {try{return s.Data["canvas_structure"]?.Deserialize<CanvasStructureReference>();}catch(JsonException){return null;}}
    public static void Store(Settings s,CanvasStructureReference reference)=>s.Set("canvas_structure",reference);
    public static CanvasStructureReference CaptureForEditor(ScreenRect canvas,PixelImage image,ScreenRect screen,ScreenPoint origin,int dpi,ScreenSize client,
        Calibration calibration,Action<CanvasPatchCandidate>? record=null)
    {
        var exclusions=EditorExclusions.Find(image,screen,canvas,calibration);
        if(!exclusions.Available)throw new InvalidDataException("Не вдалося відокремити кнопки Rust від рамки. Відкрий редактор без накладок і повтори захоплення полотна.");
        if(exclusions.Regions.Any(r=>EditorExclusions.Overlap(canvas,r)))
            throw new InvalidDataException("Область малювання перекриває кнопки Rust. Зміни виділення так, щоб кнопки залишилися зовні.");
        var bounds=CanvasBounds.ConfirmSelection(image,screen,canvas,origin);
        return Capture(canvas,image,screen,origin,dpi,client,exclusions.Regions,record) with{Bounds=bounds};
    }
    public static CanvasStructureReference Capture(ScreenRect canvas,PixelImage image,ScreenRect screen,ScreenPoint origin,int dpi,ScreenSize client,
        IReadOnlyList<ScreenRect>? exclusions=null,Action<CanvasPatchCandidate>? record=null)
    {
        if(!canvas.Valid||image.Width!=screen.Width||image.Height!=screen.Height)throw new InvalidDataException("Invalid Canvas capture.");
        var noGo=exclusions??[];
        var selected=CanvasPatchSelector.Select(canvas,image,screen,origin,noGo,record);
        return new(Revision,new(canvas.Left-origin.X,canvas.Top-origin.Y,canvas.Right-origin.X,canvas.Bottom-origin.Y),dpi,client,
            Convert.ToHexString(SHA256.HashData(image.Rgba)),DateTimeOffset.UtcNow,selected.Patches)
            {SelectorRevision=CanvasPatchSelector.Revision,Availability=selected.Availability,
                Exclusions=noGo.Select(r=>new ScreenRect(r.Left-origin.X,r.Top-origin.Y,r.Right-origin.X,r.Bottom-origin.Y)).ToArray()};
    }
    public static bool Current(CanvasStructureReference? r,ScreenRect canvas,int dpi,ScreenSize client)
        =>r is {Revision:Revision,SelectorRevision:CanvasPatchSelector.Revision,Patches.Length:16,FrameSha256.Length:64}&&r.Canvas==canvas&&r.Dpi==dpi&&r.Client==client
            &&r.Exclusions is not null&&r.Patches.All(p=>p is not null)
            &&Enumerable.Range(0,4).All(side=>r.Patches.Count(p=>p.Side==side)==4)
            &&r.Patches.All(p=>p is not null&&p.Rect.Valid&&(long)p.Rect.Width*p.Rect.Height==288&&p.Rgb is {Length:864}
                &&!r.Exclusions.Any(n=>EditorExclusions.Overlap(EditorExclusions.Expand(p.Rect,SearchRadius),n))
                &&(p.Rect.Right<=canvas.Left-PaintExclusionMargin||p.Rect.Left>=canvas.Right+PaintExclusionMargin
                    ||p.Rect.Bottom<=canvas.Top-PaintExclusionMargin||p.Rect.Top>=canvas.Bottom+PaintExclusionMargin));
    private static (double Score,double Chroma) Compare(CanvasPatch patch,PixelImage image,ScreenRect screen,ScreenPoint origin,int dx,int dy)
    {
        var r=patch.Rect;int left=r.Left+origin.X-screen.Left+dx,top=r.Top+origin.Y-screen.Top+dy;
        if(left<0||top<0||left+r.Width>image.Width||top+r.Height>image.Height)return (-1,1);
        double[] a=new double[3],b=new double[3],aa=new double[3],bb=new double[3],ab=new double[3];int n=r.Width*r.Height,i=0;
        for(int y=0;y<r.Height;y++)for(int x=0;x<r.Width;x++)
        {
            var c=image.Color((top+y)*image.Width+left+x);
            for(int channel=0;channel<3;channel++)
            {double v=patch.Rgb[i++],w=channel==0?c.R:channel==1?c.G:c.B;a[channel]+=v;b[channel]+=w;aa[channel]+=v*v;bb[channel]+=w*w;ab[channel]+=v*w;}
        }
        double numerator=0,va=0,vb=0,chroma=0,sumA=a.Sum(),sumB=b.Sum();
        if(sumA<=0||sumB<=0)return (-1,1);
        for(int k=0;k<3;k++){numerator+=ab[k]-a[k]*b[k]/n;va+=aa[k]-a[k]*a[k]/n;vb+=bb[k]-b[k]*b[k]/n;chroma=Math.Max(chroma,Math.Abs(a[k]/sumA-b[k]/sumB));}
        if(va<n*16)
        {
            // A flat support patch cannot locate geometry, but may confirm its
            // appearance. Global peak uniqueness still needs textured patches.
            bool same=Enumerable.Range(0,3).All(k=>Math.Abs(a[k]-b[k])/n<=16)&&vb<=n*48;
            return (same?1:-1,chroma);
        }
        return (vb<1?-1:numerator/Math.Sqrt(va*vb),chroma);
    }
    public static CanvasStructureResult Read(CanvasStructureReference? reference,ScreenRect canvas,PixelImage image,ScreenRect screen,ScreenPoint origin,int dpi,ScreenSize client)
    {
        if(!Current(reference,canvas,dpi,client))return new(false,"editor_structure_recapture_required",0,0,0);
        var r=reference!;var scores=new List<(int X,int Y,double Score)>();
        foreach(int dy in Enumerable.Range(-SearchRadius,2*SearchRadius+1))foreach(int dx in Enumerable.Range(-SearchRadius,2*SearchRadius+1))
        {
            double score=0;bool valid=true;
            foreach(var p in r.Patches)
            {
                var match=Compare(p,image,screen,origin,dx,dy);score+=match.Score;
                if(match.Chroma>MaximumChromaChange||match.Score<MinimumCorrelation)valid=false;
            }
            scores.Add((dx,dy,valid?score/r.Patches.Length:-1));
        }
        var best=scores.OrderByDescending(p=>p.Score).First();
        if(best.Score<MinimumCorrelation)return new(false,"editor_structure_mismatch",0,0,best.Score);
        if(best.X!=0||best.Y!=0)return new(false,"editor_structure_geometry_changed",best.X,best.Y,best.Score);
        if(scores.Where(p=>p.X!=0||p.Y!=0).Max(p=>p.Score)>best.Score-MinimumPeakMargin)
            return new(false,"editor_structure_ambiguous",0,0,best.Score);
        return new(true,"editor_structure_present",0,0,best.Score);
    }
}
