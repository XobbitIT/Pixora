namespace CanvasForge.Core;

public sealed record CanvasPatchCandidate(int Side,ScreenRect Rect,bool Accepted,string Reason,double Variance);
public sealed record CanvasSideAvailability(int Side,int Candidates,int Available,int Selected);

public static class CanvasPatchSelector
{
    public const int Revision=1,PerSide=4,TangentStep=6,MinimumSpacing=24,MaximumOutward=66;
    public static (CanvasPatch[] Patches,CanvasSideAvailability[] Availability) Select(ScreenRect canvas,PixelImage image,ScreenRect screen,
        ScreenPoint origin,IReadOnlyList<ScreenRect> noGo,Action<CanvasPatchCandidate>? record=null)
    {
        var chosen=new List<CanvasPatch>();var availability=new List<CanvasSideAvailability>();
        for(int side=0;side<4;side++)
        {
            bool vertical=side is 1 or 3;int length=vertical?canvas.Height:canvas.Width;
            int count=0;var candidates=new List<(ScreenRect Rect,int Along,double Variance)>();
            for(int along=12;along<length-12;along+=TangentStep)
                for(int outward=CanvasStructure.PaintExclusionMargin;outward<=MaximumOutward;outward+=4)
                {
                    int x=canvas.Left+along,y=canvas.Top+along;
                    var r=side switch{0=>new ScreenRect(x-4,canvas.Top-outward-32,x+5,canvas.Top-outward),
                        1=>new ScreenRect(canvas.Right+outward,y-4,canvas.Right+outward+32,y+5),
                        2=>new ScreenRect(x-4,canvas.Bottom+outward,x+5,canvas.Bottom+outward+32),
                        _=>new ScreenRect(canvas.Left-outward-32,y-4,canvas.Left-outward,y+5)};
                    count++;string reason="available";double variance=0;
                    var envelope=EditorExclusions.Expand(r,CanvasStructure.SearchRadius);
                    if(envelope.Left<screen.Left||envelope.Top<screen.Top||envelope.Right>screen.Right||envelope.Bottom>screen.Bottom)reason="outside_capture";
                    else if(noGo.Any(n=>EditorExclusions.Overlap(envelope,n)))reason="interactive_ui_excluded";
                    else
                    {
                        double sum=0,sum2=0;int low=255,high=0;
                        for(int py=r.Top;py<r.Bottom;py++)for(int px=r.Left;px<r.Right;px++)
                        {var c=image.Color((py-screen.Top)*image.Width+px-screen.Left);int v=(c.R+c.G+c.B)/3;sum+=v;sum2+=v*v;low=Math.Min(low,v);high=Math.Max(high,v);}
                        int n=r.Width*r.Height;variance=sum2/n-Math.Pow(sum/n,2);
                        if(high-low<24||variance<16)reason="insufficient_texture";
                    }
                    record?.Invoke(new(side,r,reason=="available",reason,variance));
                    if(reason=="available")candidates.Add((r,along,variance));
                }
            var positions=candidates.GroupBy(p=>p.Along).Select(g=>g.OrderByDescending(p=>p.Variance).First()).OrderBy(p=>p.Along).ToArray();
            List<(ScreenRect Rect,int Along,double Variance)> Pick(int spacing)
            {
                var list=new List<(ScreenRect Rect,int Along,double Variance)>();
                foreach(var point in positions)
                    if(list.Count==0||point.Along-list[^1].Along>=spacing){list.Add(point);if(list.Count==PerSide)break;}
                return list;
            }
            var selected=Pick(MinimumSpacing);
            if(selected.Count==PerSide)
            {
                int low=MinimumSpacing,high=length;
                while(low<high){int mid=(low+high+1)/2;if(Pick(mid).Count==PerSide)low=mid;else high=mid-1;}
                selected=Pick(low);
            }
            availability.Add(new(side,count,candidates.Count,selected.Count));
            foreach(var item in selected.OrderBy(p=>p.Along))
            {
                var r=item.Rect;var rgb=new byte[r.Width*r.Height*3];int i=0;
                for(int py=r.Top;py<r.Bottom;py++)for(int px=r.Left;px<r.Right;px++)
                {var c=image.Color((py-screen.Top)*image.Width+px-screen.Left);rgb[i++]=c.R;rgb[i++]=c.G;rgb[i++]=c.B;}
                chosen.Add(new(new(r.Left-origin.X,r.Top-origin.Y,r.Right-origin.X,r.Bottom-origin.Y),rgb,side));
                record?.Invoke(new(side,r,true,"selected",item.Variance));
            }
        }
        if(availability.Any(p=>p.Selected!=PerSide))throw new InvalidDataException("Недостатньо надійних ділянок рамки поза кнопками Rust. Відкрий усю рамку без накладок і повтори захоплення полотна.");
        return (chosen.ToArray(),availability.ToArray());
    }
}
