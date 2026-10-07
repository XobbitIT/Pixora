namespace CanvasForge.Core;

public sealed record PaintBatch(double Size, IReadOnlyList<ScreenLine> Segments, int SourceStrokes, int ShapeSlot=0, string? ProfileId=null);

// Join only axis-aligned paths that stay inside the original fine coverage or
// inside a same-color area large enough for the calibrated wide brush.
public static class TransferSchedule
{
    public const int MaximumStrokes = 16;
    public const int MaximumConnector = 32;
    public static bool Fast(Settings s) => s.Bool("fast_transfer") && !SpeedCalibration.Use(s) && !(s.Bool("line_mode") && s.Text("coverage_mode") == "Fast");
    public static bool FastBatch(Settings s,PaintBatch batch)=>Fast(s)
        &&(batch.Segments.Count!=1||SpeedCalibration.Resolve(s,batch.Size>0?batch.Size:PaintTimingPlan.DefaultSize(s),batch.Segments[0],batch.ShapeSlot) is null);
    public static int Length(ScreenLine l) => Math.Max(Math.Abs(l.X2-l.X1), Math.Abs(l.Y2-l.Y1));
    public static ScreenLine Reverse(ScreenLine l) => new(l.X2,l.Y2,l.X1,l.Y1);

    public static List<int> Order(PaintPlan plan, Dictionary<int,List<PaintBatch>> groups)
    {
        var order = groups.Keys.OrderByDescending(i => plan.Counts.GetValueOrDefault(i)).ToList();
        if (plan.BackgroundColor is int background)
        {
            order.Remove(background);
            order.Insert(0, background);
        }
        return order;
    }

    public static Dictionary<int,List<PaintBatch>> Build(PaintPlan plan, Settings s,CancellationToken token=default)
        => Build(plan, s, AdaptiveBrush.Build(plan,s,token),token);

    public static Dictionary<int,List<PaintBatch>> Build(PaintPlan plan, Settings s, Dictionary<int,List<BrushStroke>> source,CancellationToken token=default)
    {
        s=BrushFootprints.Snapshot(s);
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
            token.ThrowIfCancellationRequested();
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
            List<ScreenLine>? segments=null;double size=0;int count=0,shape=0;string? profileId=null;
            void Flush(){if(segments is not null)batches.Add(new(size,segments,count,shape,profileId));segments=null;count=0;}
            foreach(var op in strokes)
            {
                token.ThrowIfCancellationRequested();
                var next=op.Line;
                bool connected=false;
                if(join&&segments is not null&&op.Size==size&&op.ShapeSlot==shape&&op.ProfileId==profileId&&count<MaximumStrokes
                    &&SpeedCalibration.Resolve(s,op.Size>0?op.Size:PaintTimingPlan.DefaultSize(s),op.Line,op.ShapeSlot) is null)
                {
                    var end=segments[^1];
                    foreach(var candidate in new[]{next,Reverse(next)}.OrderBy(l=>Math.Abs(l.X1-end.X2)+Math.Abs(l.Y1-end.Y2)))
                    {
                        var connector=new ScreenLine(end.X2,end.Y2,candidate.X1,candidate.Y1);
                        if(op.ProfileId is not null)
                        {
                            var p=BrushFootprints.Find(s,op.Size,op.ShapeSlot);
                            if(p is null||p.Id!=op.ProfileId||connector.X1!=connector.X2&&connector.Y1!=connector.Y2||Length(connector)>MaximumConnector)continue;
                            bool safe=true;
                            for(int k=0;k<=Length(connector);k++)
                            {
                                int cx=connector.X1+k*Math.Sign(connector.X2-connector.X1)-rect.Left,cy=connector.Y1+k*Math.Sign(connector.Y2-connector.Y1)-rect.Top;
                                safe&=BrushFootprints.Safe(p,cx,cy,rect.Width,rect.Height,(px,py)=>plan.Indices[Cell(yb,py+rect.Top)*plan.Width+Cell(xb,px+rect.Left)]==color);
                                if(!safe)break;
                            }
                            if(!safe)continue;
                        }
                        else if(!SafeConnector(connector,op.Size>0?op.OuterRadius:0,color))continue;
                        if(Length(connector)>0)segments.Add(connector);
                        next=candidate;connected=true;break;
                    }
                }
                if(!connected){Flush();segments=[];size=op.Size;shape=op.ShapeSlot;profileId=op.ProfileId;}
                segments!.Add(next);count++;
            }
            Flush();
        }
        return output;
    }

    public static double EstimateBatch(Settings s,SpeedProfile speed,PaintBatch batch,
        Func<double,ScreenLine,int,SpeedSample?>? resolve=null,bool? fast=null)
    {
        double size=batch.Size>0?batch.Size:speed.BrushSize;
        if(batch.Segments.Count==1&&(resolve is null
            ?SpeedCalibration.Resolve(s,size,batch.Segments[0],batch.ShapeSlot)
            :resolve(size,batch.Segments[0],batch.ShapeSlot)) is { } sample)
            return CalibratedMotion.Estimate(batch.Segments[0],sample);
        if(!(fast??Fast(s)))
        {
            int length=Length(batch.Segments[0]);
            bool shift=s.Bool("line_mode")&&s.Text("coverage_mode")=="Fast"&&length>=s.Int("min_line_width",4)*s.Int("cell_px",3);
            return StrokeTiming.Estimate(s,speed,length,shift,false);
        }
        double travel=batch.Segments.Sum(l=>StrokeMotion.TravelSeconds(s,speed,Length(l))+StrokeTiming.EndHold(s,speed));
        return StrokeTiming.Settle(s,speed)+Math.Max(.04,StrokeTiming.Frame(s)+travel)+StrokeTiming.Release(s);
    }
}
