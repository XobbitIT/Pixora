namespace CanvasForge.Core;

public static partial class CoverageAudit
{
    public static CoverageRepairPlan PlanRepair(bool[] missing,bool[] expected,ScreenRect canvas,
        IReadOnlyList<BrushFootprint> profiles,int limit=2000,CancellationToken token=default)
    {
        if(!canvas.Valid||(long)canvas.Width*canvas.Height>4_000_000||missing.Length!=canvas.Width*canvas.Height
            ||expected.Length!=missing.Length||limit<1||profiles.Count>7||profiles.Any(p=>!BrushFootprints.Valid(p)||p.Size!=1
            ||p.Possible.Sum(r=>r.Right-r.Left)>4096))throw new ArgumentException("Invalid footprint repair.");
        int w=canvas.Width,h=canvas.Height,targets=0,unreachable=0,possibleOnly=0;
        var planned=new bool[missing.Length];var commands=new Dictionary<string,HashSet<ScreenPoint>>();
        var ordered=profiles.OrderByDescending(p=>p.SolidPixels).ToArray();
        var caches=ordered.ToDictionary(p=>p.Id,_=>new Dictionary<ScreenPoint,bool>());
        bool Safe(BrushFootprint p,ScreenPoint c)
        {
            if(c.X<0||c.Y<0||c.X>=w||c.Y>=h)return false;
            var cache=caches[p.Id];
            if(!cache.TryGetValue(c,out var safe))cache[c]=safe=BrushFootprints.Safe(p,c.X,c.Y,w,h,(x,y)=>expected[y*w+x]);
            return safe;
        }
        foreach(var p in ordered)commands[p.Id]=[];
        for(int i=0;i<missing.Length;i++)if(missing[i]&&expected[i])
        {
            if((i&1023)==0)token.ThrowIfCancellationRequested();targets++;
            if(planned[i])continue;
            int x=i%w,y=i/w;bool found=false,physical=false;
            foreach(var p in ordered)
            {
                foreach(var offset in BrushFootprints.Points(p.Solid))
                {
                    var c=new ScreenPoint(x-offset.X,y-offset.Y);if(!Safe(p,c))continue;
                    commands[p.Id].Add(c);found=true;
                    foreach(var paint in BrushFootprints.Points(p.Solid))planned[(c.Y+paint.Y)*w+c.X+paint.X]=true;
                    break;
                }
                if(found)break;
                if(!physical)physical=BrushFootprints.Points(p.Possible).Any(offset=>Safe(p,new(x-offset.X,y-offset.Y)));
            }
            if(!found){if(physical)possibleOnly++;else unreachable++;}
        }
        var ops=new List<BrushStroke>();
        foreach(var p in ordered)
        {
            var pending=commands[p.Id];
            foreach(var point in pending.OrderBy(c=>c.Y).ThenBy(c=>c.X).ToArray())
            {
                token.ThrowIfCancellationRequested();if(!pending.Remove(point))continue;
                int end=point.X;
                // Join separated repair centres only when every intermediate
                // command keeps the full physical mask in this expected color.
                while(true)
                {
                    int next=-1;
                    for(int x=end+1;x<=Math.Min(w-1,end+TransferSchedule.MaximumConnector);x++)
                    {if(!Safe(p,new(x,point.Y)))break;if(pending.Contains(new(x,point.Y))){next=x;break;}}
                    if(next<0)break;
                    pending.Remove(new(next,point.Y));end=next;
                }
                ops.Add(new(new(canvas.Left+point.X,canvas.Top+point.Y,canvas.Left+end,canvas.Top+point.Y),1,p.Reach,0,p.ShapeSlot,p.Id));
                if(ops.Count>limit)throw new InvalidOperationException("Too many safe repair operations. Run a new Speed Probe.");
            }
        }
        return new(ops.Select(p=>p.Line).ToList(),targets,unreachable){Operations=ops,PossibleOnlyPixels=possibleOnly};
    }
}
