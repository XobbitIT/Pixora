namespace CanvasForge.Core;

public sealed record PaintBatch(double Size, IReadOnlyList<ScreenLine> Segments, int SourceStrokes);

// Join only axis-aligned paths that stay inside the original fine coverage or
// inside a same-color area large enough for the calibrated wide brush.
public static class TransferSchedule
{
    public const int MaximumStrokes = 16;
    public const int MaximumConnector = 32;
    public static bool Fast(Settings s) => s.Bool("fast_transfer") && !(s.Bool("line_mode") && s.Text("coverage_mode") == "Fast");
    public static int MoveSpan(Settings s) => Math.Clamp(s.Int("fast_move_span_px", 256), 32, 512);
    public static int Length(ScreenLine l) => Math.Max(Math.Abs(l.X2-l.X1), Math.Abs(l.Y2-l.Y1));
    public static ScreenLine Reverse(ScreenLine l) => new(l.X2,l.Y2,l.X1,l.Y1);

    public static Dictionary<int,List<PaintBatch>> Build(PaintPlan plan, Settings s)
        => Build(plan, s, AdaptiveBrush.Build(plan,s));

    public static Dictionary<int,List<PaintBatch>> Build(PaintPlan plan, Settings s, Dictionary<int,List<BrushStroke>> source)
    {
        var output = new Dictionary<int,List<PaintBatch>>();
        var rect=s.Calibration.Rect("canvas");
        bool join=Fast(s) && s.Int("brush_shape_slot",3) is 3 or 4
            && (!s.Bool("use_fixed_opacity",true) || s.Number("paint_opacity_value",1)==1)
            && rect.Valid && (long)rect.Width*rect.Height<=16_000_000;
        var fine=join?new bool[rect.Width*rect.Height]:[];
        var xb=join?Coverage.Partition(rect.Left,rect.Right,plan.Width):[];
        var yb=join?Coverage.Partition(rect.Top,rect.Bottom,plan.Height):[];
        static int Cell(int[] boundaries,int pixel)
        {
            // Upper bound also handles empty cells when the plan exceeds Canvas resolution.
            int lo=0,hi=boundaries.Length;
            while(lo<hi){int mid=(lo+hi)/2;if(boundaries[mid]<=pixel)lo=mid+1;else hi=mid;}
            return lo-1;
        }
        bool SafePoint(int x,int y,int radius,int color)
        {
            if(x-radius<rect.Left||x+radius>=rect.Right||y-radius<rect.Top||y+radius>=rect.Bottom)return false;
            if(radius==0)return fine[(y-rect.Top)*rect.Width+x-rect.Left];
            int x1=Cell(xb,x-radius),x2=Cell(xb,x+radius),y1=Cell(yb,y-radius),y2=Cell(yb,y+radius);
            for(int py=y1;py<=y2;py++)for(int px=x1;px<=x2;px++)
                if(plan.Indices[py*plan.Width+px]!=color)return false;
            return true;
        }
        bool SafeConnector(ScreenLine line,int radius,int color)
        {
            if(line.X1!=line.X2&&line.Y1!=line.Y2 || Length(line)>MaximumConnector)return false;
            int dx=Math.Sign(line.X2-line.X1),dy=Math.Sign(line.Y2-line.Y1);
            for(int k=0;k<=Length(line);k++)if(!SafePoint(line.X1+k*dx,line.Y1+k*dy,radius,color))return false;
            return true;
        }
        foreach(var(color,strokes)in source)
        {
            var batches=new List<PaintBatch>();output[color]=batches;
            if(join)
            {
                Array.Clear(fine);
                foreach(var op in strokes.Where(x=>x.Size==0))
                {
                    var l=op.Line;int dx=Math.Sign(l.X2-l.X1),dy=Math.Sign(l.Y2-l.Y1);
                    for(int k=0;k<=Length(l);k++)
                    {
                        int x=l.X1+k*dx,y=l.Y1+k*dy;
                        if(x>=rect.Left&&x<rect.Right&&y>=rect.Top&&y<rect.Bottom)fine[(y-rect.Top)*rect.Width+x-rect.Left]=true;
                    }
                }
            }
            List<ScreenLine>? segments=null;double size=0;int count=0;
            void Flush(){if(segments is not null)batches.Add(new(size,segments,count));segments=null;count=0;}
            foreach(var op in strokes)
            {
                var next=op.Line;
                bool connected=false;
                if(join&&segments is not null&&op.Size==size&&count<MaximumStrokes)
                {
                    var end=segments[^1];
                    foreach(var candidate in new[]{next,Reverse(next)}.OrderBy(l=>Math.Abs(l.X1-end.X2)+Math.Abs(l.Y1-end.Y2)))
                    {
                        var connector=new ScreenLine(end.X2,end.Y2,candidate.X1,candidate.Y1);
                        if(!SafeConnector(connector,op.Size>0?op.OuterRadius:0,color))continue;
                        if(Length(connector)>0)segments.Add(connector);
                        next=candidate;connected=true;break;
                    }
                }
                if(!connected){Flush();segments=[];size=op.Size;}
                segments!.Add(next);count++;
            }
            Flush();
        }
        return output;
    }

    public static double EstimateBatch(Settings s,SpeedProfile speed,PaintBatch batch)
    {
        if(!Fast(s))
        {
            int length=Length(batch.Segments[0]);
            bool shift=s.Bool("line_mode")&&s.Text("coverage_mode")=="Fast"&&length>=s.Int("min_line_width",4)*s.Int("cell_px",3);
            return StrokeTiming.Estimate(s,speed,length,shift);
        }
        double travel=batch.Segments.Sum(l=>Math.Max(1,Math.Ceiling(Length(l)/(double)MoveSpan(s))))*StrokeTiming.Frame(s);
        return StrokeTiming.Settle(s,speed)+Math.Max(.04,StrokeTiming.Frame(s)+travel)+StrokeTiming.Release(s);
    }
}
