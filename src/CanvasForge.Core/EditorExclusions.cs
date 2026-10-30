namespace CanvasForge.Core;

public sealed record EditorExclusionResult(bool Available,string Reason,ScreenRect[] Regions)
{
    public ScreenRect[] CandidateTiles { get;init; }=[];
    public int CandidateRows { get;init; }
}

public static class EditorExclusions
{
    public const int Padding=6;
    public static ScreenRect Expand(ScreenRect r,int n)=>new(r.Left-n,r.Top-n,r.Right+n,r.Bottom+n);
    public static bool Overlap(ScreenRect a,ScreenRect b)=>a.Left<b.Right&&a.Right>b.Left&&a.Top<b.Bottom&&a.Bottom>b.Top;
    // Recognize a row of square colored toolbar tiles, then exclude its whole
    // band including inactive tiles/gaps. Never treat green pixels as "wood".
    public static EditorExclusionResult Find(PixelImage image,ScreenRect screen,ScreenRect canvas,Calibration calibration)
    {
        if(image.Width!=screen.Width||image.Height!=screen.Height||!canvas.Valid)return new(false,"capture_geometry_invalid",[]);
        int right=Math.Clamp(canvas.Right-screen.Left+64,0,image.Width),bottom=Math.Clamp(canvas.Top-screen.Top,0,image.Height);
        bool Tile(int x,int y)
        {
            var c=image.Color(y*image.Width+x);
            return c.G>=65&&c.G>c.R*1.1&&c.G>c.B*1.1
                ||c.R>=100&&c.R>c.G*1.3&&c.R>c.B*1.3
                ||c.B>=95&&c.B>c.R*1.2&&c.B>c.G*.9
                ||Math.Max(c.R,Math.Max(c.G,c.B)) is >=45 and <=180&&Math.Max(c.R,Math.Max(c.G,c.B))-Math.Min(c.R,Math.Min(c.G,c.B))<=6;
        }
        var seen=new HashSet<int>();var tiles=new List<ScreenRect>();
        for(int y=0;y<bottom;y++)for(int x=0;x<right;x++)
        {
            int id=y*image.Width+x;if(seen.Contains(id)||!Tile(x,y))continue;
            var seed=image.Color(id);
            int l=x,t=y,r=x,b=y,count=0;var queue=new Queue<ScreenPoint>();queue.Enqueue(new(x,y));seen.Add(id);
            while(queue.TryDequeue(out var p))
            {
                count++;l=Math.Min(l,p.X);r=Math.Max(r,p.X);t=Math.Min(t,p.Y);b=Math.Max(b,p.Y);
                foreach(var n in new[]{new ScreenPoint(p.X-1,p.Y),new(p.X+1,p.Y),new(p.X,p.Y-1),new(p.X,p.Y+1)})
                    if(n.X>=0&&n.X<right&&n.Y>=0&&n.Y<bottom&&Tile(n.X,n.Y)
                        &&RustSlider.Delta(image.Color(n.Y*image.Width+n.X),seed)<=16&&seen.Add(n.Y*image.Width+n.X))queue.Enqueue(n);
            }
            int w=r-l+1,h=b-t+1;
            if(w>=12&&w<=160&&h>=12&&h<=160&&w>=h*.85&&w<=h*1.15&&count>=w*h*.55)
                tiles.Add(new(l+screen.Left,t+screen.Top,r+1+screen.Left,b+1+screen.Top));
        }
        // Icon glyphs can themselves form colored squares. A contained glyph
        // is not another toolbar button or an independent toolbar row.
        tiles=tiles.Where(p=>!tiles.Any(q=>q!=p&&q.Left<=p.Left&&q.Top<=p.Top&&q.Right>=p.Right&&q.Bottom>=p.Bottom)).ToList();
        var rows=tiles.Select(anchor=>tiles.Where(p=>Math.Abs(p.Top-anchor.Top)<=4&&p.Height>=anchor.Height*.6&&p.Height<=anchor.Height*1.6).OrderBy(p=>p.Left).ToArray())
            .Where(row=>row.Length>=4).GroupBy(row=>string.Join(";",row.Select(p=>p.Left+","+p.Top))).Select(g=>g.First()).ToArray();
        if(rows.Length!=1)return new(false,"toolbar_no_go_unavailable",[]){CandidateTiles=tiles.ToArray(),CandidateRows=rows.Length};
        var row=rows[0];int pitch=row.Zip(row.Skip(1),(a,b)=>b.Left-a.Left).Order().ElementAt((row.Length-1)/2);
        if(pitch<row[0].Width*.9||pitch>row[0].Width*3)return new(false,"toolbar_no_go_ambiguous",[]);
        var regions=new List<ScreenRect>{new(screen.Left,row.Min(p=>p.Top),row.Max(p=>p.Right),row.Max(p=>p.Bottom))};
        foreach(string key in new[]{"palette","quick","brush_shapes","size_track","size_value_field","tool_row","topbar","top_bar","save_cancel","clan_logo"})
            if(calibration.Rect(key) is {Valid:true} rect)regions.Add(rect);
        return new(true,"editor_no_go_present",regions.Select(r=>Expand(r,Padding)).ToArray()){CandidateTiles=tiles.ToArray(),CandidateRows=rows.Length};
    }
}
