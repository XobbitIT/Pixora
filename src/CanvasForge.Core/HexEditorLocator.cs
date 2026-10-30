namespace CanvasForge.Core;

// The click point can be anywhere in the text. Find the field's neutral
// background component, not a band relative to that arbitrary click. Text,
// selection, the active swatch and the hue gradient never become landmarks.
public static class HexEditorLocator
{
    private static bool Background(Rgb c)=>Math.Min(c.R,Math.Min(c.G,c.B))>=24
        &&Math.Max(c.R,Math.Max(c.G,c.B))<=80
        &&Math.Max(c.R,Math.Max(c.G,c.B))-Math.Min(c.R,Math.Min(c.G,c.B))<=8;
    public static ScreenRect? Find(PixelImage image,ScreenRect screen,ScreenPoint click,int dpi=96)
    {
        if(image.Width!=screen.Width||image.Height!=screen.Height||click.X<screen.Left||click.Y<screen.Top||click.X>=screen.Right||click.Y>=screen.Bottom)return null;
        double scale=Math.Clamp(dpi/96d,.75,3);int rx=(int)Math.Ceiling(200*scale),ry=(int)Math.Ceiling(120*scale);
        int left=Math.Max(screen.Left,click.X-rx),top=Math.Max(screen.Top,click.Y-ry),right=Math.Min(screen.Right,click.X+rx),bottom=Math.Min(screen.Bottom,click.Y+ry);
        int w=right-left,h=bottom-top;var seen=new bool[w*h];var queue=new Queue<int>();ScreenRect? found=null;int closest=int.MaxValue;
        Rgb Color(int x,int y)=>image.Color((y-screen.Top)*image.Width+x-screen.Left);
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)
        {
            int start=y*w+x;if(seen[start]||!Background(Color(left+x,top+y)))continue;
            seen[start]=true;queue.Enqueue(start);int l=x,r=x,t=y,b=y,count=0;
            while(queue.Count>0)
            {
                int i=queue.Dequeue(),cx=i%w,cy=i/w;count++;l=Math.Min(l,cx);r=Math.Max(r,cx);t=Math.Min(t,cy);b=Math.Max(b,cy);
                void Visit(int nx,int ny)
                {
                    if(nx<0||ny<0||nx>=w||ny>=h)return;int ni=ny*w+nx;
                    if(!seen[ni]&&Background(Color(left+nx,top+ny))){seen[ni]=true;queue.Enqueue(ni);}
                }
                Visit(cx-1,cy);Visit(cx+1,cy);Visit(cx,cy-1);Visit(cx,cy+1);
            }
            var rect=new ScreenRect(left+l,top+t,left+r+1,top+b+1);
            // A uniform panel or synthetic grey fill touches the search border.
            if(l==0||t==0||r==w-1||b==h-1||rect.Width<24||rect.Height<20
                ||rect.Width>240*scale||rect.Height>150*scale||count<rect.Width*rect.Height/3
                ||click.X<rect.Left||click.X>=rect.Right||click.Y<rect.Top||click.Y>=rect.Bottom||!Structure(image,screen,rect))continue;
            int distance=Math.Abs(rect.Center.X-click.X)+Math.Abs(rect.Center.Y-click.Y);
            if(distance<closest){closest=distance;found=rect;}
        }
        return found;
    }
    public static bool Structure(PixelImage image,ScreenRect screen,ScreenRect field)
    {
        if(!field.Valid||field.Left<=screen.Left||field.Top<=screen.Top||field.Right+3>=screen.Right||field.Bottom+3>=screen.Bottom)return false;
        int good=0,total=0;
        // Bottom/right transitions are independent of the changing swatch above.
        for(int k=2;k<=8;k++)
        {
            int x=field.Left+k*field.Width/10,y=field.Top+k*field.Height/10;
            Rgb At(int px,int py)=>image.Color((py-screen.Top)*image.Width+px-screen.Left);
            foreach(var pair in new[]{(Inside:At(x,field.Bottom-3),Outside:At(x,field.Bottom+2)),
                (Inside:At(field.Right-3,y),Outside:At(field.Right+2,y))})
            {total++;if(Background(pair.Inside)&&RustSlider.Delta(pair.Inside,pair.Outside)>=12)good++;}
        }
        return good>=Math.Ceiling(total*.9);
    }
    public static ScreenPoint[] Landmarks(ScreenRect field)
    {
        // Only padding at the bottom and right: never sample the centred text.
        var pixels=new List<ScreenPoint>();
        for(int k=2;k<=8;k++)
        {
            int x=field.Left+k*field.Width/10,y=field.Top+k*field.Height/10;
            pixels.Add(new(x,field.Bottom-3));pixels.Add(new(x,field.Bottom-6));pixels.Add(new(x,field.Bottom+2));
            pixels.Add(new(field.Right-3,y));pixels.Add(new(field.Right+2,y));
        }
        return pixels.Distinct().ToArray();
    }
}
