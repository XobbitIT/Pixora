namespace CanvasForge.Core;

// A moving stroke can reach beyond a stationary dot. Only the independent
// spatial slow controls may measure this scene guard, before speed trials.
// It never widens the allowed solid-core offsets or changes RGB acceptance.
public static class ProbeStrokeEnvelope
{
    public static int MeasurementRadius(int dotRadius)
    {
        if(dotRadius is <0 or >512)throw new ArgumentOutOfRangeException(nameof(dotRadius));
        // The measurement search is bounded in advance by the existing input
        // quantisation limit. Paint outside this cap cannot become new evidence.
        return Math.Min(512,dotRadius+ProbeSpatialCalibration.MaxOffset);
    }

    public static ProbeAnalysisResult Measure(PixelImage before,PixelImage after,ScreenLine line,int outer,int inner)
    {
        int cap=MeasurementRadius(outer);
        var result=ProbeAnalysis.SpatialControl(before,after,line,outer,inner,cap);
        if(!result.Passed)return result;
        var changed=new bool[result.CoreMask.Length];
        for(int i=0;i<changed.Length;i++)changed[i]=RustSlider.Delta(before.Color(i),after.Color(i))>12;
        var seen=new bool[changed.Length];var queue=new Queue<int>();
        for(int i=0;i<changed.Length;i++)if(result.CoreMask[i]&&changed[i]){seen[i]=true;queue.Enqueue(i);}
        int radius=outer;
        while(queue.TryDequeue(out int index))
        {
            int x=index%before.Width,y=index/before.Width;
            int dx=Math.Max(0,Math.Max(Math.Min(line.X1,line.X2)-x,x-Math.Max(line.X1,line.X2)));
            int dy=Math.Max(0,Math.Max(Math.Min(line.Y1,line.Y2)-y,y-Math.Max(line.Y1,line.Y2)));
            radius=Math.Max(radius,Math.Max(dx,dy));
            if(radius>cap||x==0||y==0||x==before.Width-1||y==before.Height-1)return Reject(result,ProbeFailure.ClippedCore);
            for(int oy=-1;oy<=1;oy++)for(int ox=-1;ox<=1;ox++)
            {
                int nx=x+ox,ny=y+oy;
                if(nx<0||ny<0||nx>=before.Width||ny>=before.Height)continue;
                int next=ny*before.Width+nx;
                if(changed[next]&&!seen[next]){seen[next]=true;queue.Enqueue(next);}
            }
        }
        // An unrelated patch inside the measurement search must not be absorbed
        // into the paint envelope. Apply the existing scene-change budget.
        int unrelated=0,background=0;
        for(int i=0;i<seen.Length;i++)if(!seen[i])
        {background++;if(RustSlider.Delta(before.Color(i),after.Color(i))>16)unrelated++;}
        if(unrelated>Math.Max(32,background/100))return Reject(result,ProbeFailure.SceneChanged);
        // Re-inspect using the observed radius, rather than granting the entire
        // measurement search to later candidates. Candidate code never calls Measure.
        return ProbeAnalysis.SpatialControl(before,after,line,outer,inner,radius);
    }

    private static ProbeAnalysisResult Reject(ProbeAnalysisResult result,ProbeFailure failure)=>result with
    {
        Failure=failure,
        CoreCoverage=new(result.CoreCoverage.Expected,0,0,result.CoreCoverage.Expected,
            new bool[result.CoreMask.Length],result.CoreCoverage.Reference)
    };
}
