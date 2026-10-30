namespace CanvasForge.Core;

// Reorient a rectangle of identical, consecutive scanlines along its long axis.
// This preserves every integer center-path point, including multiplicity. It
// never joins across a gap, reduces row density or changes the target image.
// Center-path equivalence is not a certification of Rust's physical footprint.
public static class StandardCoverageRuns
{
    public static List<ScreenLine> Optimize(IEnumerable<ScreenLine> source)
    {
        var lines=source.ToArray();
        if(lines.Any(l=>l.X1!=l.X2&&l.Y1!=l.Y2))throw new ArgumentException("Expected axis-aligned coverage.");
        var output=new List<ScreenLine>(lines.Length);
        foreach(bool vertical in new[]{false,true})
        {
            var groups=lines.Where(l=>(l.X1==l.X2&&l.Y1!=l.Y2)==vertical)
                .GroupBy(l=>vertical?(A:Math.Min(l.Y1,l.Y2),B:Math.Max(l.Y1,l.Y2)):(A:Math.Min(l.X1,l.X2),B:Math.Max(l.X1,l.X2)))
                .OrderBy(g=>g.Key.A).ThenBy(g=>g.Key.B);
            foreach(var group in groups)
            {
                var tracks=group.Select(l=>vertical?l.X1:l.Y1).Order().ToArray();
                int first=tracks[0],last=first;
                void Flush()
                {
                    long across=(long)group.Key.B-group.Key.A+1,along=(long)last-first+1;
                    if(across<along)
                    {
                        for(long t=group.Key.A;t<=group.Key.B;t++)output.Add(vertical
                            ?new(first,(int)t,last,(int)t):new((int)t,first,(int)t,last));
                    }
                    else
                        for(long t=first;t<=last;t++)output.Add(vertical
                            ?new((int)t,group.Key.A,(int)t,group.Key.B):new(group.Key.A,(int)t,group.Key.B,(int)t));
                }
                foreach(int track in tracks.Skip(1))
                {
                    if((long)track!= (long)last+1){Flush();first=track;}
                    last=track;
                }
                Flush();
            }
        }
        // Stable order, also used by evidence export, timing and checkpoints.
        return output.OrderBy(l=>l.Y1==l.Y2?0:1).ThenBy(l=>l.Y1==l.Y2?l.Y1:l.X1)
            .ThenBy(l=>l.Y1==l.Y2?l.X1:l.Y1).ToList();
    }
}
