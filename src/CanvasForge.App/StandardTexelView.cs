using System.Security.Cryptography;
using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

// Fixed-view availability for THIS experiment, never an inverse UV locator.
// Keep0022 picks as diagnostics: a nearly tied argmax is not a displacement.
// Compare their entire native support at zero displacement instead. Searching
// only rejects a displaced/ambiguous view; it never moves commands/reference.
internal static class StandardTexelView
{
    internal const string Policy="texel-view-native-support-zero-registration-v2";
    internal sealed record Pick(string Side,int Y,double? X);
    internal sealed record Support(string Side,int Y,double Similarity,double Chroma,bool Match);
    internal sealed record Offset(int Dx,int Dy,double Similarity,double Chroma);
    internal sealed record Registration(string Side,int Dx,int Dy,double Similarity,double PeakMargin,bool Match,Offset[] Offsets);
    internal sealed record Result(string Policy,string ReferenceRgba,string ObservedRgba,int Compared,int Missing,int Mismatched,
        bool Match,Pick[] Reference,Pick[] Observed,Support[] Supports,Registration[] Registrations,string Reason);
    internal static PixelImage ReadFrame(string path)
    {
        var p=Images.LoadNative(path);using var meta=JsonDocument.Parse(File.ReadAllBytes(Path.ChangeExtension(path,"json")));
        if(p.Width!=2560||p.Height!=1440||meta.RootElement.GetProperty("FrameSha256").GetString()?.ToLowerInvariant()!=Convert.ToHexString(SHA256.HashData(p.Rgba)).ToLowerInvariant())
            throw new InvalidDataException("Texel geometry witness is not an intact2560x1440 native frame.");
        return p;
    }
    internal static Pick[] Picks(PixelImage p)
    {
        if(p.Width!=2560||p.Height!=1440)throw new InvalidDataException("Texel view size changed.");
        var result=new List<Pick>();double Gray(int x,int y){var c=p.Color(y*p.Width+x);return (c.R+c.G+c.B)/3.0;}
        foreach(var (side,guess,sign) in new[]{("left",465,1),("right",1510,-1)})
            for(int y=180;y<=1160;y++)
            {
                double best=double.NegativeInfinity;int at=0;
                for(int x=guess-24;x<=guess+24;x++)
                {
                    double a=0,b=0;for(int yy=y-2;yy<=y+2;yy++)for(int k=0;k<5;k++){a+=Gray(x+k,yy);b+=Gray(x-5+k,yy);}
                    double score=sign*(a-b)/25;
                    if(score>best){best=score;at=x;}
                }
                result.Add(new(side,y,best>=30?at-.5:null));
            }
        return result.ToArray();
    }
    internal static Result Compare(PixelImage reference,PixelImage observed)
    {
        var a=Picks(reference);var b=Picks(observed);int missing=a.Zip(b).Count(p=>p.First.X is null||p.Second.X is null),mismatch=a.Zip(b).Count(p=>p.First.X!=p.Second.X);
        var supports=new List<Support>();var registrations=new List<Registration>();
        foreach(var (side,guess) in new[]{("left",465),("right",1510)})
        {
            // Exactly the union of0022's49 candidate supports. Every one of981
            // rows per side must retain its appearance; no majority/skip rule.
            for(int y=180;y<=1160;y++)
            {
                var c=Correlate(reference,observed,new(guess-29,y-2,guess+29,y+3),0,0);
                supports.Add(new(side,y,c.Score,c.Chroma,c.Score>=CanvasStructure.MinimumCorrelation&&c.Chroma<=CanvasStructure.MaximumChromaChange));
            }
            var offsets=new List<Offset>();int radius=CanvasStructure.SearchRadius;
            for(int dy=-radius;dy<=radius;dy++)for(int dx=-radius;dx<=radius;dx++)
            {
                var c=Correlate(reference,observed,new(guess-29,178,guess+29,1163),dx,dy);
                offsets.Add(new(dx,dy,c.Score,c.Chroma));
            }
            var zero=offsets.Single(o=>o.Dx==0&&o.Dy==0);var best=offsets.OrderByDescending(o=>o.Similarity).First();
            double margin=zero.Similarity-offsets.Where(o=>o.Dx!=0||o.Dy!=0).Max(o=>o.Similarity);
            bool match=best.Dx==0&&best.Dy==0&&zero.Similarity>=CanvasStructure.MinimumCorrelation
                &&zero.Chroma<=CanvasStructure.MaximumChromaChange&&margin>=CanvasStructure.MinimumPeakMargin;
            registrations.Add(new(side,best.Dx,best.Dy,zero.Similarity,margin,match,offsets.ToArray()));
        }
        string Hash(PixelImage p)=>Convert.ToHexString(SHA256.HashData(p.Rgba)).ToLowerInvariant();
        string reason=missing>0?"paper_support_missing":supports.Any(s=>!s.Match)?"paper_support_changed":registrations.Any(r=>!r.Match)?"paper_displaced_or_ambiguous":"fixed_view_present";
        return new(Policy,Hash(reference),Hash(observed),a.Length,missing,mismatch,reason=="fixed_view_present",a,b,supports.ToArray(),registrations.ToArray(),reason);
    }
    private static (double Score,double Chroma) Correlate(PixelImage a,PixelImage b,ScreenRect rect,int dx,int dy)
    {
        double[] sa=new double[3],sb=new double[3],aa=new double[3],bb=new double[3],ab=new double[3];int n=rect.Width*rect.Height;
        for(int y=rect.Top;y<rect.Bottom;y++)for(int x=rect.Left;x<rect.Right;x++)
        {
            var p=a.Color(y*a.Width+x);var q=b.Color((y+dy)*b.Width+x+dx);
            for(int k=0;k<3;k++)
            {double v=k==0?p.R:k==1?p.G:p.B,w=k==0?q.R:k==1?q.G:q.B;sa[k]+=v;sb[k]+=w;aa[k]+=v*v;bb[k]+=w*w;ab[k]+=v*w;}
        }
        double va=0,vb=0,cov=0,chroma=0,sumA=sa.Sum(),sumB=sb.Sum();
        if(sumA<=0||sumB<=0)return (-1,1);
        for(int k=0;k<3;k++)
        {va+=aa[k]-sa[k]*sa[k]/n;vb+=bb[k]-sb[k]*sb[k]/n;cov+=ab[k]-sa[k]*sb[k]/n;chroma=Math.Max(chroma,Math.Abs(sa[k]/sumA-sb[k]/sumB));}
        // Unlike support-only flat patches, these strips must locate geometry.
        return va<n*16||vb<n*16?(-1,chroma):(cov/Math.Sqrt(va*vb),chroma);
    }
    internal static void Check(string directory,bool publish)
    {
        var result=Compare(ReadFrame(Path.Combine(directory,"reference/geometry.png")),ReadFrame(Path.Combine(directory,FrontierSessionCapture.FrameName)));
        string path=Path.Combine(directory,"geometry-check.json");
        if(publish)StandardUvPilot.WriteNew(path,result);
        else if(JsonSerializer.Serialize(JsonSerializer.Deserialize<Result>(File.ReadAllText(path)))!=JsonSerializer.Serialize(result))throw new InvalidDataException("Stored texel view check changed.");
        if(!result.Match)throw new InvalidDataException($"Вид полотна не підтверджено: {result.Reason}; незбігів смуг {result.Supports.Count(s=>!s.Match)}/1962, невідомих {result.Missing}. Ввід фарби не розпочато. Деталі: geometry-check.json. Коефіцієнти не підлаштовуються.");
    }
}
