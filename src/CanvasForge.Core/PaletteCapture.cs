namespace CanvasForge.Core;

public static class PaletteCapture
{
    public static int QuickRows(PixelImage image,ScreenRect screen,ScreenRect rect,string selection="Auto")
    {
        if(!rect.Valid||rect.Left<screen.Left||rect.Top<screen.Top||rect.Right>screen.Right||rect.Bottom>screen.Bottom)
            throw new InvalidDataException("Capture only the quick color cells.");
        if(int.TryParse(selection,out int explicitRows)&&explicitRows is >=1 and <=16)return explicitRows;
        var gaps=new List<(int Top,int Bottom)>();int begin=-1;
        for(int y=rect.Top;y<=rect.Bottom;y++)
        {
            bool dark=y<rect.Bottom&&Enumerable.Range(1,3).All(i=>
            {
                var c=image.Color((y-screen.Top)*image.Width+rect.Left+rect.Width*i/4-screen.Left);
                return Math.Max(c.R,Math.Max(c.G,c.B))<=20;
            });
            if(dark&&begin<0)begin=y;
            if(!dark&&begin>=0){gaps.Add((begin,y));begin=-1;}
        }
        // Rust gives each cell a black frame. Adjacent cells therefore have
        // two nearby black bands with a narrow grey gutter between them.
        var merged=new List<(int Top,int Bottom)>();
        foreach(var gap in gaps)
            if(merged.Count>0&&gap.Top-merged[^1].Bottom<=rect.Width*.15)merged[^1]=(merged[^1].Top,gap.Bottom);
            else merged.Add(gap);
        gaps=merged.Where(g=>g.Bottom-g.Top<=rect.Width*.35).ToList();
        var pitches=gaps.Zip(gaps.Skip(1),(a,b)=>(b.Top+b.Bottom-a.Top-a.Bottom)/2d)
            .Where(p=>p>=rect.Width*.65&&p<=rect.Width*1.5).OrderBy(p=>p).ToArray();
        double pitch=pitches.Length>0?pitches[pitches.Length/2]:rect.Width;
        int rows=(int)Math.Round(rect.Height/pitch);
        if(pitches.Length==0&&rows>1)throw new InvalidDataException("Could not count quick colors. Select their count in Rust capture settings.");
        if(rows is <1 or >16||Math.Abs(rect.Height/rows-pitch)>pitch*.25)
            throw new InvalidDataException("Could not count quick colors. Select their count in Rust capture settings.");
        return rows;
    }

    // Same capture medians and deduplication as the previous UI implementation.
    // Main and quick entries retain their own click points and source geometry.
    public static List<PaletteEntry> Read(PixelImage shot,ScreenRect screen,Calibration cal)
    {
        var entries=new List<PaletteEntry>();
        foreach(var (key,cols,rows,kind) in new[]{("palette",4,16,"main"),("quick",1,cal.Get("quick_rows",10),"quick")})
        {
            var rect=cal.Rect(key);if(!rect.Valid)continue;
            foreach(var p in cal.GridCenters(key,cols,rows))
            {
                int hw=Math.Max(1,(int)(rect.Width/(double)cols*.21)),hh=Math.Max(1,(int)(rect.Height/(double)rows*.21));
                var rr=new List<byte>();var gg=new List<byte>();var bb=new List<byte>();
                for(int y=Math.Max(0,p.Y-screen.Top-hh);y<Math.Min(shot.Height,p.Y-screen.Top+hh+1);y++)
                    for(int x=Math.Max(0,p.X-screen.Left-hw);x<Math.Min(shot.Width,p.X-screen.Left+hw+1);x++)
                    {var c=shot.Color(y*shot.Width+x);rr.Add(c.R);gg.Add(c.G);bb.Add(c.B);}
                if(rr.Count==0)continue;rr.Sort();gg.Sort();bb.Sort();var color=new Rgb(rr[rr.Count/2],gg[gg.Count/2],bb[bb.Count/2]);
                if(!entries.Any(e=>RustSlider.Delta(e.Color,color)<=3))entries.Add(new(color,p,kind));
            }
        }
        return entries;
    }
}
