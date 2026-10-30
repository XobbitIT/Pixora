using System.Security.Cryptography;

namespace CanvasForge.Core;

public sealed record CanvasBoundarySide(int Side,int Coordinate,double Jump,double PeakGap)
{
    public int[] Samples { get;init; }=[];
    public int[] Tangents { get;init; }=[];
    public double Slope { get;init; }
    public double Intercept { get;init; }
    public double MaximumResidual { get;init; }
}
public sealed record CanvasBoundsResult(bool Found,string Reason,ScreenRect Rect,CanvasBoundarySide[] Sides);
public sealed record CanvasBoundsReference(int Revision,ScreenRect Rect,string SourceFrameSha256,CanvasBoundarySide[] Sides)
{
    public string SelectionKind {get;init;}="detected-interior";
}

// Initial locator on a clean, brighter paintable interior. This proposes a
// visible rectangle for confirmation; it never adjusts a runtime command.
public static class CanvasBounds
{
    public const int FreeSelectionRevision=8;
    public const int Revision=7,SearchRadius=24,SamplesPerSide=16,MinimumJump=20,MaximumEdgeSpread=2,InwardMargin=1;
    private static bool Interior(Rgb c)
    {
        int low=Math.Min(c.R,Math.Min(c.G,c.B)),high=Math.Max(c.R,Math.Max(c.G,c.B));
        return c.R+c.G+c.B>=420&&low>=95&&low>=high*.65;
    }
    public static CanvasBoundsResult Find(PixelImage frame,ScreenRect screen,ScreenRect selected)
    {
        var initial=selected;
        for(int attempt=0;attempt<3;attempt++)
        {
            var found=FindOnce(frame,screen,selected);
            if(!found.Found)return found;
            if(new[]{Math.Abs(found.Rect.Left-initial.Left),Math.Abs(found.Rect.Top-initial.Top),Math.Abs(found.Rect.Right-initial.Right),Math.Abs(found.Rect.Bottom-initial.Bottom)}.Max()>SearchRadius)
                return new(false,"canvas_bounds_outside_initial_search",initial,found.Sides);
            if(found.Rect==selected)return found;
            selected=found.Rect;
        }
        return new(false,"canvas_bounds_not_repeatable",initial,[]);
    }
    private static CanvasBoundsResult FindOnce(PixelImage frame,ScreenRect screen,ScreenRect selected)
    {
        if(frame.Width!=screen.Width||frame.Height!=screen.Height||selected.Width<64||selected.Height<64)
            return new(false,"canvas_bounds_invalid",selected,[]);
        var sides=new List<CanvasBoundarySide>();
        for(int side=0;side<4;side++)
        {
            bool horizontal=side is 0 or 2;int coordinate=side switch{0=>selected.Top,1=>selected.Right,2=>selected.Bottom,_=>selected.Left};
            int from=horizontal?selected.Left:selected.Top,length=horizontal?selected.Width:selected.Height;
            bool In(ScreenPoint p)=>p.X>=screen.Left+(horizontal?2:0)&&p.X<screen.Right-(horizontal?2:0)
                &&p.Y>=screen.Top+(horizontal?0:2)&&p.Y<screen.Bottom-(horizontal?0:2);
            Rgb Read(ScreenPoint point)
            {
                int r=0,g=0,b=0;
                for(int d=-2;d<=2;d++)
                {var c=frame.Color((point.Y-screen.Top+(horizontal?0:d))*frame.Width+point.X-screen.Left+(horizontal?d:0));r+=c.R;g+=c.G;b+=c.B;}
                return new((byte)(r/5),(byte)(g/5),(byte)(b/5));
            }
            var scanlines=new List<List<(int Coordinate,double Score)>>();var tangents=new List<int>();
            for(int i=0;i<SamplesPerSide;i++)
            {
                var scores=new List<(int Coordinate,double Score)>();
                for(int at=coordinate-SearchRadius;at<=coordinate+SearchRadius;at++)
                {
                    int tangent=from+length*(SamplesPerSide+8*i+4)/(10*SamplesPerSide);
                    var inside=side switch{0=>new ScreenPoint(tangent,at),1=>new(at-1,tangent),2=>new(tangent,at-1),_=>new(at,tangent)};
                    var outside=side switch{0=>new ScreenPoint(tangent,at-2),1=>new(at+1,tangent),2=>new(tangent,at+1),_=>new(at-2,tangent)};
                    if(!In(inside)||!In(outside))continue;
                    var a=Read(inside);
                    var b=Read(outside);
                    var adjacent=side switch{0=>inside with{Y=inside.Y-1},1=>inside with{X=inside.X+1},2=>inside with{Y=inside.Y+1},_=>inside with{X=inside.X-1}};
                    if(!In(adjacent)||!Interior(a)||Interior(Read(adjacent)))continue;
                    bool material=true;int sumR=0,sumG=0,sumB=0;
                    for(int depth=0;depth<8;depth++)
                    {
                        var p=side switch{0=>inside with{Y=inside.Y+depth},1=>inside with{X=inside.X-depth},2=>inside with{Y=inside.Y-depth},_=>inside with{X=inside.X+depth}};
                        if(!In(p)){material=false;break;}
                        var sample=Read(p);sumR+=sample.R;sumG+=sample.G;sumB+=sample.B;
                    }
                    if(!material||!Interior(new((byte)(sumR/8),(byte)(sumG/8),(byte)(sumB/8))))continue;
                    double jump=(a.R+a.G+a.B-b.R-b.G-b.B)/3d;
                    if(jump>=MinimumJump)scores.Add((at,jump));
                }
                if(scores.Count==0)return new(false,"canvas_bounds_unavailable",selected,sides.ToArray());
                if(scores.Max(p=>p.Coordinate)-scores.Min(p=>p.Coordinate)>MaximumEdgeSpread)
                    return new(false,"canvas_bounds_ambiguous",selected,sides.ToArray());
                scanlines.Add(scores);
                tangents.Add(from+length*(SamplesPerSide+8*i+4)/(10*SamplesPerSide));
            }
            var points=scanlines.Select(line=>side is 0 or 3?line.Max(p=>p.Coordinate):line.Min(p=>p.Coordinate)).ToArray();
            double mx=tangents.Average(),my=points.Average(),denominator=tangents.Sum(x=>(x-mx)*(x-mx));
            double slope=tangents.Select((x,i)=>(x-mx)*(points[i]-my)).Sum()/denominator,intercept=my-slope*mx;
            double residual=tangents.Select((x,i)=>Math.Abs(points[i]-(slope*x+intercept))).Max();
            // Do not certify a straight paper edge by an unmeasured residual
            // budget. Use the inward envelope of every observed contour point;
            // the fit is diagnostic only. This still needs native qualification.
            int safe=side is 0 or 3?points.Max()+InwardMargin:points.Min()-InwardMargin;
            sides.Add(new(side,safe,scanlines.Min(line=>line.Min(p=>p.Score)),0)
                {Samples=points,Tangents=tangents.ToArray(),Slope=slope,Intercept=intercept,MaximumResidual=residual});
        }
        var rect=new ScreenRect(sides[3].Coordinate,sides[0].Coordinate,sides[1].Coordinate,sides[2].Coordinate);
        return rect.Valid?new(true,"canvas_bounds_found",rect,sides.ToArray()):new(false,"canvas_bounds_invalid",selected,sides.ToArray());
    }
    public static CanvasBoundsReference Confirm(PixelImage frame,ScreenRect screen,ScreenRect selected,ScreenPoint origin)
    {
        var found=Find(frame,screen,selected);
        if(!found.Found||found.Rect!=selected)
            throw new InvalidDataException("Межі полотна не підтверджені. Очисти Canvas, обведи його внутрішню область і підтвердь знайдені зелені межі клавішею Enter.");
        return new(Revision,new(selected.Left-origin.X,selected.Top-origin.Y,selected.Right-origin.X,selected.Bottom-origin.Y),
            Convert.ToHexString(SHA256.HashData(frame.Rgba)),found.Sides.Select(s=>s with
                {Coordinate=s.Coordinate-(s.Side is 0 or 2?origin.Y:origin.X),
                    Samples=s.Samples.Select(p=>p-(s.Side is 0 or 2?origin.Y:origin.X)).ToArray(),
                    Tangents=s.Tangents.Select(p=>p-(s.Side is 0 or 2?origin.X:origin.Y)).ToArray(),
                    Intercept=s.Intercept-(s.Side is 0 or 2?origin.Y:origin.X)+s.Slope*(s.Side is 0 or 2?origin.X:origin.Y)}).ToArray());
    }
    // The user chooses the drawing rectangle, including intentional frame
    // padding. This records that choice, not a certificate of paper contours.
    public static CanvasBoundsReference ConfirmSelection(PixelImage frame,ScreenRect screen,ScreenRect selected,ScreenPoint origin)
    {
        if(frame.Width!=screen.Width||frame.Height!=screen.Height||selected.Width<=2||selected.Height<=2
            ||selected.Left<screen.Left||selected.Top<screen.Top||selected.Right>screen.Right||selected.Bottom>screen.Bottom)
            throw new InvalidDataException("Invalid Canvas capture.");
        return new(FreeSelectionRevision,new(selected.Left-origin.X,selected.Top-origin.Y,selected.Right-origin.X,selected.Bottom-origin.Y),
            Convert.ToHexString(SHA256.HashData(frame.Rgba)),[]){SelectionKind="user-selected"};
    }
    public static bool Current(CanvasBoundsReference? bounds,ScreenRect canvas,string frameSha256)
    {
        if(bounds is null||!canvas.Valid||bounds.Rect!=canvas||bounds.SourceFrameSha256!=frameSha256||frameSha256.Length!=64)return false;
        if(bounds.SelectionKind=="user-selected")return bounds.Revision==FreeSelectionRevision&&bounds.Sides is {Length:0};
        return bounds is {Revision:Revision,SelectionKind:"detected-interior",Sides.Length:4}
            &&bounds.Sides.All(p=>p is not null&&p.Samples is {Length:SamplesPerSide}&&p.Tangents is {Length:SamplesPerSide}
                &&p.Coordinate==(p.Side switch{0=>canvas.Top,1=>canvas.Right,2=>canvas.Bottom,_=>canvas.Left})
                &&double.IsFinite(p.Slope)&&double.IsFinite(p.Intercept)&&double.IsFinite(p.MaximumResidual))
            &&Enumerable.Range(0,4).All(side=>bounds.Sides.Count(p=>p.Side==side)==1);
    }
}
