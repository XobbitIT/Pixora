namespace CanvasForge.Core;

public sealed record BrushToolLocation(ScreenPoint Brush,ScreenRect Row,int SelectedSlot=0);

// Recognize the three-tool row above the seven brush shapes. A unique selected
// green circle and three separated white icons must agree; never infer a click
// from a fixed offset or from the selected brush-shape circle.
public static class BrushToolLocator
{
    public static BrushToolLocation? Find(PixelImage image,ScreenRect screen,ScreenRect shapes)
    {
        if(!shapes.Valid||image.Width!=screen.Width||image.Height!=screen.Height)return null;
        int left=Math.Max(0,shapes.Left-screen.Left),right=Math.Min(image.Width,shapes.Right-screen.Left);
        int bottom=Math.Min(image.Height,shapes.Top-screen.Top-24);
        if(left>=right||bottom<20)return null;
        bool Green(int x,int y){var c=image.Color(y*image.Width+x);return c.G>=90&&c.G-c.R>=20&&c.G-c.B>=25;}
        var visited=new HashSet<int>();var circles=new List<ScreenRect>();
        for(int y=2;y<bottom;y++)for(int x=left;x<right;x++)
        {
            int id=y*image.Width+x;if(visited.Contains(id)||!Green(x,y))continue;
            int minX=x,maxX=x,minY=y,maxY=y,count=0;var queue=new Queue<ScreenPoint>();queue.Enqueue(new(x,y));visited.Add(id);
            while(queue.TryDequeue(out var p))
            {
                count++;minX=Math.Min(minX,p.X);maxX=Math.Max(maxX,p.X);minY=Math.Min(minY,p.Y);maxY=Math.Max(maxY,p.Y);
                foreach(var next in new[]{new ScreenPoint(p.X-1,p.Y),new(p.X+1,p.Y),new(p.X,p.Y-1),new(p.X,p.Y+1)})
                    if(next.X>=left&&next.X<right&&next.Y>=2&&next.Y<bottom&&Green(next.X,next.Y)&&visited.Add(next.Y*image.Width+next.X))queue.Enqueue(next);
            }
            int width=maxX-minX+1,height=maxY-minY+1;
            if(width>=12&&width<=120&&height>=12&&height<=120&&width>=height*.7&&width<=height*1.4&&count>=width*height*.2)
                circles.Add(new(minX,minY,maxX+1,maxY+1));
        }
        var candidates=new List<BrushToolLocation>();
        foreach(var circle in circles)
        {
            int top=Math.Max(0,circle.Top),end=Math.Min(image.Height,circle.Bottom);var bands=new List<(int Left,int Right)>();int begin=-1,last=-1;
            for(int x=left;x<right;x++)
            {
                int white=0;for(int y=top;y<end;y++){var c=image.Color(y*image.Width+x);if(Math.Min(c.R,Math.Min(c.G,c.B))>=150)white++;}
                if(white>=2){if(begin<0)begin=x;last=x;}
                else if(begin>=0&&x-last>3){bands.Add((begin,last+1));begin=-1;}
            }
            if(begin>=0)bands.Add((begin,last+1));
            bands=bands.Where(b=>b.Right-b.Left>=5&&b.Right-b.Left<=circle.Width*1.2).ToList();
            if(bands.Count!=3)continue;
            var centers=bands.Select(b=>(b.Left+b.Right)/2).ToArray();int a=centers[1]-centers[0],b=centers[2]-centers[1];
            if(a<circle.Width*.65||a>circle.Width*2||b<circle.Width*.65||b>circle.Width*2||Math.Abs(a-b)>Math.Max(a,b)*.3)continue;
            int selected=Array.FindIndex(centers,cx=>cx>=circle.Left&&cx<circle.Right);
            if(selected<0)continue;
            int half=Math.Min(a,b)/2;
            candidates.Add(new(new(centers[0]+screen.Left,circle.Center.Y+screen.Top),
                new(centers[0]-half+screen.Left,top+screen.Top,centers[2]+half+screen.Left,end+screen.Top),selected));
        }
        return candidates.Count==1?candidates[0]:null;
    }
}
