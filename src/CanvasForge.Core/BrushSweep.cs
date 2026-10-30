namespace CanvasForge.Core;

// Exact unions of the already-certified spans along a cardinal command path.
// This is geometry only: it neither recentres nor expands the physical mask.
public static class BrushSweep
{
    public static BrushSpan[] Transpose(IEnumerable<BrushSpan> spans)
        =>BrushFootprints.Spans(BrushFootprints.Points(spans.ToArray()).Select(p=>new ScreenPoint(p.Y,p.X)));
    public static BrushSpan[] Project(BrushSpan[] mask,ScreenLine local)
    {
        bool vertical=local.X1==local.X2;
        if(!vertical&&local.Y1!=local.Y2)throw new ArgumentException("Measured sweeps require a cardinal line.");
        if(!vertical)
        {
            int start=Math.Min(local.X1,local.X2),end=Math.Max(local.X1,local.X2);
            return Merge(mask.Select(r=>new BrushSpan(local.Y1+r.Y,start+r.Left,end+r.Right)));
        }
        int top=Math.Min(local.Y1,local.Y2),bottom=Math.Max(local.Y1,local.Y2);
        var columns=Merge(Transpose(mask).Select(r=>new BrushSpan(local.X1+r.Y,top+r.Left,bottom+r.Right)));
        var rows=new List<BrushSpan>();
        foreach(var col in columns)for(int y=col.Left;y<col.Right;y++)rows.Add(new(y,col.Y,col.Y+1));
        return Merge(rows);
    }
    public static BrushSpan[] Merge(IEnumerable<BrushSpan> spans)
    {
        var result=new List<BrushSpan>();
        foreach(var span in spans.OrderBy(r=>r.Y).ThenBy(r=>r.Left))
        {
            if(result.Count>0&&result[^1] is var old&&old.Y==span.Y&&old.Right>=span.Left)
                result[^1]=old with{Right=Math.Max(old.Right,span.Right)};
            else result.Add(span);
        }
        return result.ToArray();
    }
    public static int RowPitch(BrushSpan[] solid)
    {
        int best=0,run=0,previous=int.MinValue;
        foreach(int y in solid.Select(r=>r.Y).Distinct().Order())
        {run=y==previous+1?run+1:1;previous=y;best=Math.Max(best,run);}
        return Math.Max(1,best);
    }
}
