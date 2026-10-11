namespace CanvasForge.Core;

public sealed record PhysicalCoverageResult(int Target,int Confirmed,int Missing,int Unknown,bool[] MissingMask,bool[] UnknownMask,string? UnavailableReason=null)
{
    public bool Passed=>Target>0&&Confirmed==Target&&Missing==0&&Unknown==0&&UnavailableReason is null;
    public double ConfirmedPercent=>Target>0?100d*Confirmed/Target:0;
    public double MissingPercent=>Target>0?100d*Missing/Target:0;
    public double UnknownPercent=>Target>0?100d*Unknown/Target:0;
}

// Final reporting is independent of command progress and optional per-color
// repair. Local tiles reuse the existing audit unchanged: low-contrast pixels
// still need its minimum evidence, and identical before/after is never proof.
public static class FinalCoverageAudit
{
    public static int[] Labels(PaintPlan plan,ScreenRect canvas)
    {
        if(!canvas.Valid||(long)canvas.Width*canvas.Height>4_000_000)throw new ArgumentException("Final audit supports up to 4 million pixels.");
        var labels=new int[checked(canvas.Width*canvas.Height)];var xb=Coverage.Partition(0,canvas.Width,plan.Width);var yb=Coverage.Partition(0,canvas.Height,plan.Height);
        for(int y=0;y<plan.Height;y++)for(int x=0;x<plan.Width;x++)for(int py=yb[y];py<yb[y+1];py++)
            Array.Fill(labels,plan.Indices[y*plan.Width+x],py*canvas.Width+xb[x],xb[x+1]-xb[x]);
        return labels;
    }
    public static PhysicalCoverageResult Unavailable(int[] labels,string reason)
    {var unknown=labels.Select(c=>c>=0).ToArray();int target=unknown.Count(x=>x);return new(target,0,0,target,new bool[labels.Length],unknown,reason);}
    public static PhysicalCoverageResult Read(PixelImage before,PixelImage after,int[] labels,CancellationToken token=default,int tileSize=128)
    {
        if(before.Width!=after.Width||before.Height!=after.Height||labels.Length!=before.Width*before.Height||tileSize<8)
            throw new ArgumentException("Invalid final audit images.");
        int outside=0,changed=0;
        for(int i=0;i<labels.Length;i++)if(labels[i]<0){outside++;if(RustSlider.Delta(before.Color(i),after.Color(i))>16)changed++;}
        if(changed>Math.Max(32,outside/100))return Unavailable(labels,"Scene changed outside the target.");
        int confirmed=0,missing=0,unknown=0;var gaps=new bool[labels.Length];var uncertain=new bool[labels.Length];
        for(int top=0;top<before.Height;top+=tileSize)for(int left=0;left<before.Width;left+=tileSize)
        {
            token.ThrowIfCancellationRequested();int right=Math.Min(before.Width,left+tileSize),bottom=Math.Min(before.Height,top+tileSize);
            var initial=ImageProcessing.Crop(before,left,top,right,bottom);var final=ImageProcessing.Crop(after,left,top,right,bottom);
            var local=new int[initial.Width*initial.Height];for(int y=0;y<initial.Height;y++)Array.Copy(labels,(top+y)*before.Width+left,local,y*initial.Width,initial.Width);
            foreach(int color in local.Where(c=>c>=0).Distinct())
            {
                token.ThrowIfCancellationRequested();var mask=local.Select(c=>c==color).ToArray();var scoped=final.Clone();
                // Other target colors are legitimate changes. Global outside-
                // target scene evidence was checked above with the same limits.
                for(int i=0;i<local.Length;i++)if(!mask[i])scoped.Set(i,initial.Color(i),initial.Alpha(i));
                var result=CoverageAudit.Read(initial,scoped,mask);
                confirmed+=result.Covered;missing+=result.Missing;unknown+=result.Unknown;
                for(int i=0;i<local.Length;i++)if(mask[i])
                {
                    int global=(top+i/initial.Width)*before.Width+left+i%initial.Width;
                    gaps[global]=result.MissingMask[i];
                    uncertain[global]=!result.MissingMask[i]&&(result.Reference is null
                        ||RustSlider.Delta(initial.Color(i),result.Reference.Color)<32
                        ||RustSlider.Delta(scoped.Color(i),result.Reference.Color)>result.Reference.Tolerance);
                }
            }
        }
        return new(confirmed+missing+unknown,confirmed,missing,unknown,gaps,uncertain);
    }
    public static PixelImage UnknownOverlay(PixelImage after,bool[] unknown)
    {
        if(unknown.Length!=after.Width*after.Height)throw new ArgumentException("Invalid uncertainty mask.");var image=after.Clone();
        for(int i=0;i<unknown.Length;i++)if(unknown[i])image.Set(i,new(255,174,55));return image;
    }
}
