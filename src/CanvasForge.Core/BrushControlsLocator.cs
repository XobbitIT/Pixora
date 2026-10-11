namespace CanvasForge.Core;

// Search only the previously captured right-hand panel. Accept the complete
// three-bar group and the seven-icon row together; never guess one field from
// a single green patch or silently relocate painting coordinates at runtime.
public static class BrushControlsLocator
{
    // Include the entire tool-row scan as well as numeric and shape searches.
    // The cursor must be outside this region before taking a locator frame.
    public static ScreenRect CaptureRegion(ScreenRect window,Calibration captured)
    {
        var size=captured.Rect("size_track");var opacity=captured.Rect("opacity_track");var shapes=captured.Rect("brush_shapes");
        if(!size.Valid||!opacity.Valid||!shapes.Valid)return default;
        int left=Math.Max(window.Left+2,Math.Min(shapes.Left,size.Left)-48);
        int right=Math.Min(window.Right-2,Math.Max(shapes.Right,size.Right+120)+48);
        if(captured.Point("brush_tool") is {} tool)
        {
            int radius=Math.Clamp(shapes.Width/7/2,12,32);
            left=Math.Max(window.Left+2,Math.Min(left,tool.X-radius));
            right=Math.Min(window.Right-2,Math.Max(right,tool.X+radius+1));
        }
        return new(left,window.Top+2,right,Math.Min(window.Bottom-2,opacity.Bottom+200));
    }

    public static Calibration? Find(PixelImage image,ScreenRect window,Calibration captured)
    {
        if(image.Width!=window.Width||image.Height!=window.Height)return null;
        var kinds=new[]{"size","interval","opacity"};
        var old=kinds.Select(k=>captured.Rect(k+"_track")).ToArray();
        var shapes=captured.Rect("brush_shapes");
        if(old.Any(r=>!r.Valid)||!shapes.Valid)return null;
        var search=new ScreenRect(Math.Max(2,Math.Min(shapes.Left,old[0].Left)-window.Left-48),
            Math.Max(2,Math.Min(shapes.Top,old[0].Top)-window.Top-220),
            Math.Min(image.Width-2,Math.Max(shapes.Right,old[0].Right+120)-window.Left+48),
            Math.Min(image.Height-2,old[2].Bottom-window.Top+200));
        var rows=RustSlider.Find(image,search).Where(r=>r.Track.Width>=old[0].Width*.75&&r.Track.Width<=old[0].Width*1.25).ToArray();
        if(rows.Length!=3||rows.Any(r=>Math.Abs(r.Track.Left-rows[0].Track.Left)>3||Math.Abs(r.Track.Width-rows[0].Track.Width)>3)
            ||rows[1].Track.Top-rows[0].Track.Top<20
            ||Math.Abs((rows[2].Track.Top-rows[1].Track.Top)-(rows[1].Track.Top-rows[0].Track.Top))>3)return null;
        int spacing=shapes.Width/7;if(spacing<16)return null;
        var centers=Enumerable.Range(0,7).Select(i=>shapes.Left-window.Left+(2*i+1)*shapes.Width/14).ToArray();
        bool White(int x,int y)=>x>=0&&x<image.Width&&y>=0&&y<image.Height&&image.Color(y*image.Width+x) is var c&&Math.Min(c.R,Math.Min(c.G,c.B))>=125;
        bool Ring(int x,int y)=>x>=0&&x<image.Width&&y>=0&&y<image.Height&&image.Color(y*image.Width+x) is var c&&c.G>=90&&c.G-c.R>=20&&c.G-c.B>=25;
        int radius=Math.Clamp(spacing/3,8,24), half=Math.Max(4,spacing/9);
        var candidates=new List<int>();
        for(int y=search.Top+radius;y<rows[0].Track.Top-radius;y++)
        {
            // Both solid icons and at least two other brushes must be present.
            bool Solid(int slot)=>Enumerable.Range(-half,2*half+1).All(d=>White(centers[slot],y+d));
            if(!Solid(2)||!Solid(3)||centers.Count(x=>White(x,y))<4)continue;
            bool ring=centers.Any(x=>Enumerable.Range(-half,2*half+1).Count(d=>Ring(x-radius,y+d)||Ring(x+radius,y+d))>=half);
            if(ring)candidates.Add(y);
        }
        var bands=new List<List<int>>();foreach(int y in candidates)
        {if(bands.Count==0||y>bands[^1][^1]+1)bands.Add([]);bands[^1].Add(y);}
        if(bands.Count!=1)return null;
        int center=bands[0][bands[0].Count/2]+window.Top;
        var result=new Calibration((System.Text.Json.Nodes.JsonObject)captured.Data.DeepClone());
        result.SetRect("brush_shapes",new(shapes.Left,center-radius,shapes.Right,center+radius+1));
        if(captured.Point("brush_tool") is { } tool)
        {
            // When the brush tool is already selected, its separate green
            // circle also identifies a moved tool row. Do not infer a tool from
            // a selected brush-shape circle below it or from unselected pixels.
            int tx=tool.X-window.Left,tr=Math.Clamp(spacing/2,12,32);
            var toolRows=new List<int>();
            for(int y=2;y<center-window.Top-radius-24;y++)
                if(Enumerable.Range(-tr,2*tr+1).Count(dx=>Ring(tx+dx,y))>=tr/2)toolRows.Add(y);
            if(toolRows.Count>=16&&toolRows[^1]-toolRows[0]<96
                &&toolRows.Zip(toolRows.Skip(1),(a,b)=>b-a).All(gap=>gap<=2))
                result.SetPoint("brush_tool",new(tool.X,window.Top+(toolRows[0]+toolRows[^1])/2));
        }
        for(int i=0;i<3;i++)
        {
            ScreenRect World(ScreenRect r)=>new(r.Left+window.Left,r.Top+window.Top,r.Right+window.Left,r.Bottom+window.Top);
            var row=rows[i];var track=World(row.Track);var field=World(row.ValueField);
            result.SetRect(kinds[i]+"_track",track);result.SetRect(kinds[i]+"_value_field",field);
            result.SetPoint(kinds[i]+"_min",new(track.Left,track.Center.Y));
            result.SetPoint(kinds[i]+"_max",new(track.Right-1,track.Center.Y));
        }
        return result;
    }
}
