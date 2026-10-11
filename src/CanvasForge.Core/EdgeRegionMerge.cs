namespace CanvasForge.Core;

public sealed record EdgeMergeResult(int ChangedPixels,int MergedRegions,int Passes,int MinimumArea,int Thickness);

// Target preprocessing only. No brush footprint, input or proof is changed.
// Decisions in each pass use one frozen connected-component map. Transparent
// cells are neither targets nor destinations. Larger regions keep their pixels.
public static class EdgeRegionMerge
{
    public const string Revision="thin-target-components-v1";
    public static int MinimumArea(Settings s,int width,int height)
    {
        var canvas=s.Calibration.Rect("canvas");
        double scale=canvas.Valid?canvas.Width/(double)width*canvas.Height/height:1;
        // Preprocessing must not change merely because the user applies the
        // mode recommendation; both modes must receive the same target.
        int core=BrushFootprints.Read(s,true)
            .Where(p=>p.SolidCore.Valid).Select(p=>p.SolidCore.Width*p.SolidCore.Height).DefaultIfEmpty(1).Min();
        return Math.Max(1,(int)Math.Ceiling(core/scale));
    }
    public static EdgeMergeResult Apply(int[] grid,int width,int height,int thickness,int minimumArea,CancellationToken token=default,int passes=3)
    {
        if(width<1||height<1||(long)width*height!=grid.Length||thickness is <1 or >4||minimumArea<1||passes is <1 or >3)
            throw new ArgumentException("Invalid edge merge parameters.");
        var original=(int[])grid.Clone();int merged=0,completed=0;
        IEnumerable<int> Neighbors(int i)
        {int x=i%width,y=i/width;if(x>0)yield return i-1;if(x+1<width)yield return i+1;if(y>0)yield return i-width;if(y+1<height)yield return i+width;}
        for(int pass=0;pass<passes;pass++)
        {
            token.ThrowIfCancellationRequested();var ids=new int[grid.Length];Array.Fill(ids,-1);
            var parts=new List<List<int>>();var queue=new Queue<int>();
            for(int start=0;start<grid.Length;start++)if(grid[start]>=0&&ids[start]<0)
            {
                int id=parts.Count;var pixels=new List<int>();ids[start]=id;queue.Enqueue(start);
                while(queue.TryDequeue(out int i))
                {
                    if((pixels.Count&1023)==0)token.ThrowIfCancellationRequested();pixels.Add(i);
                    foreach(int n in Neighbors(i))if(ids[n]<0&&grid[n]==grid[start]){ids[n]=id;queue.Enqueue(n);}
                }
                parts.Add(pixels);
            }
            // Maximum inscribed axis-aligned square defines pixel thickness.
            // Unlike 2*boundary-depth-1, this distinguishes even-width strips
            // correctly (a two-pixel strip is not a one-pixel strip).
            var squares=new int[grid.Length];var thicknesses=new int[parts.Count];
            for(int i=0;i<grid.Length;i++)if(ids[i]>=0)
            {
                if((i&4095)==0)token.ThrowIfCancellationRequested();int id=ids[i];
                squares[i]=i%width>0&&i>=width&&ids[i-1]==id&&ids[i-width]==id&&ids[i-width-1]==id
                    ?1+Math.Min(squares[i-1],Math.Min(squares[i-width],squares[i-width-1])):1;
                thicknesses[id]=Math.Max(thicknesses[id],squares[i]);
            }
            var replacements=new List<(List<int> Pixels,int Color)>();
            for(int id=0;id<parts.Count;id++)
            {
                token.ThrowIfCancellationRequested();var pixels=parts[id];var votes=new Dictionary<int,int>();
                foreach(int i in pixels)
                {
                    foreach(int n in Neighbors(i))if(ids[n]!=id)
                    {if(ids[n]>=0)votes[ids[n]]=votes.GetValueOrDefault(ids[n])+1;}
                }
                if(pixels.Count>=minimumArea&&thicknesses[id]>thickness)continue;
                // Avoid simultaneous swaps and oscillation of tiny neighboring
                // islands: merge toward larger components, with a stable tie.
                var eligible=votes.Where(v=>parts[v.Key].Count>pixels.Count||parts[v.Key].Count==pixels.Count&&v.Key<id).ToArray();
                if(eligible.Length==0)continue;
                int targetColor=eligible.GroupBy(v=>grid[parts[v.Key][0]])
                    .OrderByDescending(g=>g.Sum(v=>v.Value)).ThenByDescending(g=>g.Sum(v=>parts[v.Key].Count)).ThenBy(g=>g.Key).First().Key;
                replacements.Add((pixels,targetColor));
            }
            if(replacements.Count==0)break;
            foreach(var (pixels,color) in replacements)foreach(int i in pixels)grid[i]=color;
            merged+=replacements.Count;completed++;
        }
        return new(grid.Where((value,i)=>value!=original[i]).Count(),merged,completed,minimumArea,thickness);
    }
}
