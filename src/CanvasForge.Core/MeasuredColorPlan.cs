namespace CanvasForge.Core;

public sealed record MeasuredColorResult(Dictionary<int,List<BrushStroke>> Groups,int TargetPixels,int CoveredPixels,bool[] UnplannedMask)
{
    public int UnplannedPixels=>TargetPixels-CoveredPixels;
}

// Command coordinates retain the measured offset. The entire possible mask
// must stay in one expected color; only the measured solid mask removes work.
public static class MeasuredColorPlan
{
    public const string Revision="measured-color-boundaries-v1";
    public static MeasuredColorResult? TryBuild(PaintPlan plan,Settings source,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();var s=BrushFootprints.Snapshot(source);
        if(s.Text("coverage_mode","Precision")!="Precision"||!s.Bool("force_precision_controls",true)
            ||!s.Bool("use_fixed_opacity",true)||s.Number("paint_opacity_value",1)!=1||s.Bool("background_fill"))return null;
        var rect=s.Calibration.Rect("canvas");if(!rect.Valid||(long)rect.Width*rect.Height>16_000_000)return null;
        bool mixed=s.Bool("adaptive_brush");double size=PaintTimingPlan.DefaultSize(s);
        var profiles=BrushFootprints.Read(s,mixed&&s.Bool("adaptive_auto_shape")).Where(p=>p.SolidCore.Valid
            &&(mixed?p.Size<=s.Number("adaptive_max_size",20):p.Size==size&&p.ShapeSlot==s.Int("brush_shape_slot",3)))
            .OrderByDescending(p=>p.SolidCore.Width*p.SolidCore.Height).ThenBy(p=>p.ShapeSlot).ThenBy(p=>p.Size).ToArray();
        if(profiles.Length==0)return null;
        int w=rect.Width,h=rect.Height;var labels=new int[checked(w*h)];Array.Fill(labels,-1);
        var xb=Coverage.Partition(0,w,plan.Width);var yb=Coverage.Partition(0,h,plan.Height);
        for(int y=0;y<plan.Height;y++)
        {
            token.ThrowIfCancellationRequested();for(int x=0;x<plan.Width;x++)for(int py=yb[y];py<yb[y+1];py++)
                Array.Fill(labels,plan.Indices[y*plan.Width+x],py*w+xb[x],xb[x+1]-xb[x]);
        }
        var distance=new int[labels.Length];
        for(int y=0;y<h;y++)
        {
            token.ThrowIfCancellationRequested();for(int x=0;x<w;x++)
            {
                int i=y*w+x;bool edge=x==0||y==0||x==w-1||y==h-1||labels[i]<0;
                if(!edge)for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)edge|=labels[i+dy*w+dx]!=labels[i];
                distance[i]=edge?0:Math.Min(w,h);
            }
        }
        for(int y=1;y<h-1;y++){token.ThrowIfCancellationRequested();for(int x=1;x<w-1;x++)
        {int i=y*w+x;distance[i]=Math.Min(distance[i],1+Math.Min(distance[i-1],Math.Min(distance[i-w],Math.Min(distance[i-w-1],distance[i-w+1]))));}}
        for(int y=h-2;y>0;y--){token.ThrowIfCancellationRequested();for(int x=w-2;x>0;x--)
        {int i=y*w+x;distance[i]=Math.Min(distance[i],1+Math.Min(distance[i+1],Math.Min(distance[i+w],Math.Min(distance[i+w-1],distance[i+w+1]))));}}
        var covered=new bool[labels.Length];var groups=plan.Counts.Keys.ToDictionary(c=>c,_=>new List<BrushStroke>());
        int[] SafeMap(BrushFootprint p)
        {
            var map=new int[labels.Length];Array.Fill(map,-1);var b=p.SafetyBounds;var core=p.SolidCore;
            for(int y=Math.Max(0,-b.Top);y<=Math.Min(h-1,h-b.Bottom);y++)
            {
                token.ThrowIfCancellationRequested();for(int x=Math.Max(0,-b.Left);x<=Math.Min(w-1,w-b.Right);x++)
                {
                    int color=labels[(y+core.Top)*w+x+core.Left];if(color<0||!groups.ContainsKey(color))continue;
                    if(labels[y*w+x]==color&&distance[y*w+x]>p.Reach||BrushFootprints.Safe(p,x,y,w,h,(px,py)=>labels[py*w+px]==color))map[y*w+x]=color;
                }
            }
            return map;
        }
        void Add(BrushFootprint p,int color,ScreenLine local)
        {
            groups[color].Add(new(new(rect.Left+local.X1,rect.Top+local.Y1,rect.Left+local.X2,rect.Top+local.Y2),p.Size,p.Reach,0,p.ShapeSlot,p.Id));
            foreach(var row in p.Solid)Array.Fill(covered,true,(local.Y1+row.Y)*w+local.X1+row.Left,local.X2-local.X1+row.Right-row.Left);
        }
        foreach(var p in profiles.Where(p=>p.SolidCore.Width>=2&&p.SolidCore.Height>=2))
        {
            var safe=SafeMap(p);var core=p.SolidCore;var b=p.SafetyBounds;
            for(int y=Math.Max(0,-b.Top);y<=Math.Min(h-1,h-b.Bottom);y+=core.Height)
            {
                token.ThrowIfCancellationRequested();for(int x=Math.Max(0,-b.Left);x<=Math.Min(w-1,w-b.Right);x++)
                {
                    int color=safe[y*w+x];if(color<0||covered[(y+core.Top+core.Height/2)*w+x+core.Left+core.Width/2])continue;
                    int start=x;while(x+1<w&&safe[y*w+x+1]==color)x++;
                    if(x-start<Math.Max(4,core.Width))continue;
                    Add(p,color,new(start,y,x,y));
                    for(int py=y+core.Top;py<y+core.Bottom;py++)Array.Fill(covered,true,py*w+start+core.Left,x-start+core.Width);
                }
            }
        }
        // Project residual target pixels through real solid offsets. No raw
        // fine command is emitted when the physical brush cannot fit there.
        foreach(var p in profiles.OrderBy(p=>p.SolidPixels).ThenBy(p=>p.Size))
        {
            var safe=SafeMap(p);var solid=BrushFootprints.Points(p.Solid).ToArray();
            var offsets=solid.OrderBy(q=>Math.Abs(q.X-p.SolidCore.Center.X)+Math.Abs(q.Y-p.SolidCore.Center.Y)).ToArray();
            var commands=new Dictionary<int,HashSet<ScreenPoint>>();
            for(int i=0;i<labels.Length;i++)
            {
                if((i&1023)==0)token.ThrowIfCancellationRequested();int color=labels[i];if(color<0||covered[i]||!groups.ContainsKey(color))continue;
                int x=i%w,y=i/w;
                foreach(var offset in offsets)
                {
                    int cx=x-offset.X,cy=y-offset.Y;if(cx<0||cy<0||cx>=w||cy>=h||safe[cy*w+cx]!=color)continue;
                    if(!commands.TryGetValue(color,out var points))commands[color]=points=[];points.Add(new(cx,cy));
                    foreach(var paint in solid)covered[(cy+paint.Y)*w+cx+paint.X]=true;
                    break;
                }
            }
            foreach(var (color,points) in commands)
                foreach(var point in points.OrderBy(q=>q.Y).ThenBy(q=>q.X).ToArray())
                {
                    token.ThrowIfCancellationRequested();if(!points.Remove(point))continue;int end=point.X;
                    while(true)
                    {
                        int next=-1;
                        for(int x=end+1;x<=Math.Min(w-1,end+TransferSchedule.MaximumConnector);x++)
                        {
                            if(safe[point.Y*w+x]!=color)break;
                            if(points.Contains(new(x,point.Y))){next=x;break;}
                        }
                        if(next<0)break;points.Remove(new(next,point.Y));end=next;
                    }
                    Add(p,color,new(point.X,point.Y,end,point.Y));
                }
        }
        foreach(var color in groups.Keys.ToArray())groups[color]=groups[color].OrderBy(p=>p.ShapeSlot).ThenByDescending(p=>p.Size).ToList();
        var unplanned=new bool[labels.Length];int targets=0,count=0;
        for(int i=0;i<labels.Length;i++)if(labels[i]>=0){targets++;if(covered[i])count++;else unplanned[i]=true;}
        return new(groups,targets,count,unplanned);
    }
}
