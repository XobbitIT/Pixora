namespace CanvasForge.Core;

// Contrast is an observation of a physical change, never a replacement for
// full-width frozen-color acceptance. A faint/wrong color remains rejected.
public sealed record ProbeGeometryInspection(int Slices,int ObservedSlices,int InEnvelopeSlices,
    int ColorRejectedSlices,int NoChangeSlices,int OutsideEnvelopeSlices,Rgb? ObservedRgb,
    int? MedianRgbDelta,ProbeOffsetTrajectory Trajectory)
{
    public static ProbeGeometryInspection Measure(PixelImage before,PixelImage after,ScreenLine line,
        int outer,int inner,int[] allowed,AuditReference reference,int[][] confirmed)
    {
        int n=TransferSchedule.Length(line),dx=Math.Sign(line.X2-line.X1),dy=Math.Sign(line.Y2-line.Y1);
        int observed=0,inEnvelope=0,colorRejected=0,noChange=0,outside=0;
        var paths=new List<int[]>();var colors=new List<Rgb>();var deltas=new List<int>();
        for(int k=ProbeAnalysis.EndMargin;k<=n-ProbeAnalysis.EndMargin;k++)
        {
            var positions=new List<(int Offset,int Score,Rgb Color)>();bool anyChange=false;
            for(int offset=-outer+inner;offset<=outer-inner;offset++)
            {
                int score=255;Rgb color=default;bool valid=true;
                for(int p=offset-inner;p<=offset+inner;p++)
                {
                    int x=line.X1+k*dx+(dy!=0?p:0),y=line.Y1+k*dy+(dx!=0?p:0);
                    if(x<0||y<0||x>=before.Width||y>=before.Height){valid=false;break;}
                    int i=y*before.Width+x;int delta=RustSlider.Delta(before.Color(i),after.Color(i));
                    anyChange|=delta>12;score=Math.Min(score,delta);
                    if(p==offset)color=after.Color(i);
                }
                if(valid&&score>=32)positions.Add((offset,score,color));
            }
            int index=k-ProbeAnalysis.EndMargin;
            if(!anyChange)noChange++;
            bool inside=positions.Any(p=>allowed.Contains(p.Offset));
            if(inside){inEnvelope++;if(confirmed[index].Length==0)colorRejected++;}
            if(positions.Count==0){paths.Add([]);continue;}
            observed++;if(!inside)outside++;
            int best=positions.Max(p=>p.Score);var strongest=positions.Where(p=>p.Score==best).ToArray();
            paths.Add(strongest.Select(p=>p.Offset).ToArray());
            // A tied geometry is retained as ambiguous, not assigned an offset.
            if(strongest.Length==1){colors.Add(strongest[0].Color);deltas.Add(RustSlider.Delta(strongest[0].Color,reference.Color));}
        }
        byte Median(Func<Rgb,byte> select)=>colors.Select(select).Order().ElementAt(colors.Count/2);
        Rgb? rgb=colors.Count==0?null:new Rgb(Median(c=>c.R),Median(c=>c.G),Median(c=>c.B));
        return new(paths.Count,observed,inEnvelope,colorRejected,noChange,outside,rgb,
            deltas.Count==0?null:deltas.Order().ElementAt(deltas.Count/2),ProbeOffsetTrajectory.Measure(paths.ToArray(),outer));
    }
}
