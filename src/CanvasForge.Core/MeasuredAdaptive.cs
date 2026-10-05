namespace CanvasForge.Core;

internal static class MeasuredAdaptive
{
    public static Dictionary<int,List<BrushStroke>> Build(PaintPlan plan,Settings s,Dictionary<int,List<ScreenLine>> basic,CancellationToken token)
    {
        var output=basic.ToDictionary(p=>p.Key,p=>p.Value.Select(l=>new BrushStroke(l,0)).ToList());
        var rect=s.Calibration.Rect("canvas");int w=rect.Width,h=rect.Height;
        var profiles=BrushFootprints.Read(s,s.Bool("adaptive_auto_shape")).Where(p=>p.Size>1&&p.Size<=s.Int("adaptive_max_size",20)
            &&p.SolidCore.Width>=2&&p.SolidCore.Height>=2).OrderByDescending(p=>p.SolidCore.Width*p.SolidCore.Height).ToArray();
        if(profiles.Length==0)return output;
        var labels=new int[checked(w*h)];Array.Fill(labels,-1);
        var xb=Coverage.Partition(0,w,plan.Width);var yb=Coverage.Partition(0,h,plan.Height);
        for(int y=0;y<plan.Height;y++)
        {
            token.ThrowIfCancellationRequested();
            for(int x=0;x<plan.Width;x++)for(int py=yb[y];py<yb[y+1];py++)
                Array.Fill(labels,plan.Indices[y*plan.Width+x],py*w+xb[x],xb[x+1]-xb[x]);
        }
        // A distance bound speeds up interior checks. Near a boundary the exact
        // measured mask, including asymmetric corners and holes, decides safety.
        var distance=new int[labels.Length];
        for(int y=0;y<h;y++)
        {
            token.ThrowIfCancellationRequested();
            for(int x=0;x<w;x++)
            {
                int i=y*w+x;bool edge=x==0||y==0||x==w-1||y==h-1||labels[i]<0;
                if(!edge)for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)edge|=labels[i+dy*w+dx]!=labels[i];
                distance[i]=edge?0:Math.Min(w,h);
            }
        }
        for(int y=1;y<h-1;y++)for(int x=1;x<w-1;x++)
        {int i=y*w+x;distance[i]=Math.Min(distance[i],1+Math.Min(distance[i-1],Math.Min(distance[i-w],Math.Min(distance[i-w-1],distance[i-w+1]))));}
        for(int y=h-2;y>0;y--)for(int x=w-2;x>0;x--)
        {int i=y*w+x;distance[i]=Math.Min(distance[i],1+Math.Min(distance[i+1],Math.Min(distance[i+w],Math.Min(distance[i+w-1],distance[i+w+1]))));}
        var covered=new bool[labels.Length];var wide=basic.ToDictionary(p=>p.Key,_=>new List<BrushStroke>());
        foreach(var p in profiles)
        {
            var b=p.SafetyBounds;var core=p.SolidCore;
            bool Safe(int x,int y,int color)=>labels[y*w+x]==color&&distance[y*w+x]>p.Reach
                ||BrushFootprints.Safe(p,x,y,w,h,(px,py)=>labels[py*w+px]==color);
            for(int y=Math.Max(0,-b.Top);y<h-b.Bottom+1;y+=core.Height)
            {
                token.ThrowIfCancellationRequested();int x=Math.Max(0,-b.Left);
                while(x<w-b.Right+1)
                {
                    int ax=x+core.Left+core.Width/2,ay=y+core.Top+core.Height/2;
                    int color=labels[ay*w+ax];
                    if(color<0||!wide.ContainsKey(color)||covered[ay*w+ax]||!Safe(x,y,color)){x++;continue;}
                    int start=x;
                    while(x+1<w-b.Right+1)
                    {
                        int next=x+1+core.Left+core.Width/2;
                        if(covered[ay*w+next]||labels[ay*w+next]!=color||!Safe(x+1,y,color))break;
                        x++;
                    }
                    int end=x++;
                    if(end-start<Math.Max(4,core.Width))continue;
                    wide[color].Add(new(new(rect.Left+start,rect.Top+y,rect.Left+end,rect.Top+y),p.Size,p.Reach,0,p.ShapeSlot,p.Id));
                    for(int py=y+core.Top;py<y+core.Bottom;py++)Array.Fill(covered,true,py*w+start+core.Left,end-start+core.Width);
                }
            }
        }
        var speed=SpeedProfile.Get(s.Text("speed_profile","Rapid"));
        double Cost(IEnumerable<BrushStroke> strokes)
        {
            double cost=0,size=speed.BrushSize;int shape=s.Int("brush_shape_slot",3);
            foreach(var op in strokes)
            {
                double nextSize=op.Size>0?op.Size:speed.BrushSize;int nextShape=op.ShapeSlot>0?op.ShapeSlot:s.Int("brush_shape_slot",3);
                if(nextSize!=size||nextShape!=shape)cost+=StrokeTiming.SliderChangeEstimate(s);
                if(nextShape!=shape)cost+=StrokeTiming.ClickEstimate(s);
                size=nextSize;shape=nextShape;
                cost+=TransferSchedule.EstimateBatch(s,speed,new(op.Size,[op.Line],1,op.ShapeSlot,op.ProfileId));
            }
            return cost;
        }
        foreach(var (color,lines) in basic)
        {
            token.ThrowIfCancellationRequested();if(wide[color].Count==0)continue;
            var fine=new List<BrushStroke>();
            foreach(var l in lines)
            {
                int dx=Math.Sign(l.X2-l.X1),dy=Math.Sign(l.Y2-l.Y1),n=TransferSchedule.Length(l),start=-1;
                for(int k=0;k<=n+1;k++)
                {
                    bool keep=k<=n&&!covered[(l.Y1+k*dy-rect.Top)*w+l.X1+k*dx-rect.Left];
                    if(keep&&start<0)start=k;
                    if(!keep&&start>=0){fine.Add(new(new(l.X1+start*dx,l.Y1+start*dy,l.X1+(k-1)*dx,l.Y1+(k-1)*dy),0));start=-1;}
                }
            }
            // Wide fills leave narrow edge strips. Regroup their exact command
            // pixels vertically when that avoids hundreds of tiny horizontal
            // fragments; preserve sparse Precision pitches and transparent gaps.
            var pending=new bool[labels.Length];
            foreach(var op in fine)
                for(int k=0;k<=TransferSchedule.Length(op.Line);k++)
                    pending[(op.Line.Y1+k*Math.Sign(op.Line.Y2-op.Line.Y1)-rect.Top)*w+op.Line.X1+k*Math.Sign(op.Line.X2-op.Line.X1)-rect.Left]=true;
            var regrouped=new List<BrushStroke>();
            for(int y=0;y<h;y++)
            {
                token.ThrowIfCancellationRequested();
                for(int x=0;x<w;x++)if(pending[y*w+x])
                {
                    int right=x,bottom=y;
                    while(right+1<w&&pending[y*w+right+1])right++;
                    while(bottom+1<h&&pending[(bottom+1)*w+x])bottom++;
                    if(bottom-y>right-x)
                    {regrouped.Add(new(new(rect.Left+x,rect.Top+y,rect.Left+x,rect.Top+bottom),0));for(int py=y;py<=bottom;py++)pending[py*w+x]=false;}
                    else
                    {regrouped.Add(new(new(rect.Left+x,rect.Top+y,rect.Left+right,rect.Top+y),0));Array.Fill(pending,false,y*w+x,right-x+1);}
                }
            }
            var proposed=wide[color].OrderBy(op=>op.ShapeSlot).ThenByDescending(op=>op.Size).Concat(regrouped).ToList();
            if(Cost(proposed)<Cost(output[color]))output[color]=proposed;
        }
        return output;
    }
}
