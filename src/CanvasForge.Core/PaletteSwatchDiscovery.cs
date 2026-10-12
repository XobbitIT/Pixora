namespace CanvasForge.Core;

// Discover an independent active-color display. A matching palette cell or a
// static color elsewhere is never evidence: the same pixels must follow both
// requested colors in two frames each, using the existing RGB tolerance.
public static class PaletteSwatchDiscovery
{
    public static bool Independent(ScreenRect area,ScreenRect frame,IReadOnlyList<ScreenRect> excluded)
        =>area.Valid&&area.Left>=frame.Left&&area.Top>=frame.Top&&area.Right<=frame.Right&&area.Bottom<=frame.Bottom
            &&!excluded.Any(r=>r.Valid&&area.Left<r.Right&&area.Right>r.Left&&area.Top<r.Bottom&&area.Bottom>r.Top);
    public static ScreenRect? Find(ScreenRect frame,PixelImage alternateFirst,PixelImage alternateSecond,
        PixelImage expectedFirst,PixelImage expectedSecond,Rgb alternate,Rgb expected,IReadOnlyList<ScreenRect> excluded,
        CancellationToken token=default)
    {
        var images=new[]{alternateFirst,alternateSecond,expectedFirst,expectedSecond};
        if(!frame.Valid||images.Any(p=>p.Width!=frame.Width||p.Height!=frame.Height)
            ||RustSlider.Delta(alternate,expected)<80)throw new ArgumentException("Invalid palette swatch discovery frames.");
        var pixels=new HashSet<ScreenPoint>();
        for(int y=2;y<frame.Height-2;y++)
        {
            token.ThrowIfCancellationRequested();
            for(int x=2;x<frame.Width-2;x++)
            {
                var point=new ScreenPoint(frame.Left+x,frame.Top+y);
                if(excluded.Any(r=>r.Valid&&point.X>=r.Left&&point.X<r.Right&&point.Y>=r.Top&&point.Y<r.Bottom))continue;
                int i=y*frame.Width+x;var a=alternateFirst.Color(i);var b=alternateSecond.Color(i);
                var c=expectedFirst.Color(i);var d=expectedSecond.Color(i);
                if(RustSlider.Delta(a,alternate)<=12&&RustSlider.Delta(b,alternate)<=12&&RustSlider.Delta(a,b)<=12
                    &&RustSlider.Delta(c,expected)<=12&&RustSlider.Delta(d,expected)<=12&&RustSlider.Delta(c,d)<=12)
                    pixels.Add(point);
            }
        }
        ScreenRect? candidate=null;
        while(pixels.Count>0)
        {
            var seed=pixels.First();pixels.Remove(seed);var connected=new List<ScreenPoint>();var queue=new Queue<ScreenPoint>();queue.Enqueue(seed);
            while(queue.TryDequeue(out var p))
            {
                token.ThrowIfCancellationRequested();connected.Add(p);
                foreach(var next in new[]{new ScreenPoint(p.X-1,p.Y),new(p.X+1,p.Y),new(p.X,p.Y-1),new(p.X,p.Y+1)})
                    if(pixels.Remove(next))queue.Enqueue(next);
            }
            var core=BrushFootprints.Core(BrushFootprints.Spans(connected));
            if(!core.Valid)continue;
            // Ambiguity is a refusal, never a guess between two displays.
            if(candidate is not null)return null;
            int w=Math.Min(5,core.Width),h=Math.Min(5,core.Height);
            candidate=new(core.Center.X-w/2,core.Center.Y-h/2,core.Center.X-w/2+w,core.Center.Y-h/2+h);
        }
        return candidate;
    }
    public static ScreenRect Exclude(ScreenRect rect,int margin=24)=>rect.Valid
        ?new(rect.Left-margin,rect.Top-margin,rect.Right+margin,rect.Bottom+margin):default;
}
